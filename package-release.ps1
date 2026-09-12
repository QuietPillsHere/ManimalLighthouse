# Build fresh plugins and package only manifest-declared runtime payloads.
# Authored Unity bundles are supplied by a staged install or -AssetSourcePath.
[CmdletBinding()]
param(
    [string]$SPTPath,
    [string]$AssetSourcePath,
    [string]$OutputDirectory,
    [switch]$ValidateOnly,
    [switch]$StageOnly
)

# Windows associates .ps1 files with Windows PowerShell on many systems. This
# project builds against modern .NET, so transparently move the same command to
# PowerShell 7 instead of failing at the old #requires directive before useful
# output can be shown.
if ($PSVersionTable.PSVersion -lt [version]'7.2') {
    $pwsh = Get-Command pwsh.exe -ErrorAction SilentlyContinue
    if (-not $pwsh) {
        throw 'PowerShell 7.2 or newer is required. Install PowerShell 7, then run package-release.ps1 again.'
    }
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($PSBoundParameters.ContainsKey('SPTPath')) { $arguments += @('-SPTPath', $SPTPath) }
    if ($PSBoundParameters.ContainsKey('AssetSourcePath')) { $arguments += @('-AssetSourcePath', $AssetSourcePath) }
    if ($PSBoundParameters.ContainsKey('OutputDirectory')) { $arguments += @('-OutputDirectory', $OutputDirectory) }
    if ($ValidateOnly) { $arguments += '-ValidateOnly' }
    if ($StageOnly) { $arguments += '-StageOnly' }
    & $pwsh.Source @arguments
    exit $LASTEXITCODE
}

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'dist' }
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.ModVersion
$sourceUrl = [string]$props.Project.PropertyGroup.ModSourceUrl
if (-not $SPTPath) { $SPTPath = [string]$props.Project.PropertyGroup.SPTPath.'#text' }
if (-not $AssetSourcePath) {
    $AssetSourcePath = Join-Path $root 'build/install-test'
    if (-not (Test-Path -LiteralPath $AssetSourcePath)) { $AssetSourcePath = $SPTPath }
}
$AssetSourcePath = (Resolve-Path -LiteralPath $AssetSourcePath).Path
$clientRelative = 'BepInEx/plugins/ManimalLighthouse'
$serverRelative = 'SPT_Runtime/user/mods/ManimalLighthouse'
$clientSource = Join-Path $AssetSourcePath $clientRelative
$serverSource = Join-Path $AssetSourcePath $serverRelative
$manifestPath = Join-Path $clientSource 'lighthouse-content.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($sourceUrl)) {
    if ($manifest.Mode -ne 'test') { throw 'Public release requires ModSourceUrl in Directory.Build.props and a source link on the mod listing.' }
    Write-Warning 'Local test package only: source repository URL is not set; not ready for public publication.'
}
if ((Get-FileHash -LiteralPath $manifestPath).Hash -ne (Get-FileHash -LiteralPath (Join-Path $serverSource 'lighthouse-content.json')).Hash) { throw 'Asset source has mismatched client/server manifests.' }
if ($manifest.Schema -ne 1 -or $manifest.TargetClientBuild -ne '0.16.9.40743' -or @($manifest.Scenes).Count -ne 29) { throw 'Unsupported or incomplete Lighthouse content manifest.' }
if (($manifest.Mode -ne 'test' -or $manifest.Ready) -and ($manifest.Mode -ne 'rework' -or -not $manifest.Ready)) { throw 'Only complete test or rework payloads can be packaged.' }
function Resolve-Payload([string]$Base, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or $Relative.Contains('\') -or $Relative.Contains(':') -or $Relative.StartsWith('/') -or ($Relative.Split('/') | Where-Object { $_ -in '', '.', '..' })) { throw "Unsafe payload path: $Relative" }
    $prefix = [IO.Path]::GetFullPath($Base).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath((Join-Path $prefix $Relative))
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Payload path escapes root.' }
    return $resolved
}
function Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$inputs = [Collections.Generic.List[object]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-Input([string]$Source, [string]$Destination, [string]$Expected = '') {
    if (-not $seen.Add($Destination)) { throw "Duplicate package path: $Destination" }
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw "Missing package input: $Source" }
    if ($Destination -match '(?i)(\.(md|txt|xml|pdb|cs|csproj|ps1|py|bak)$|/(diagnostics|dumps|cache)/)') { throw "Development file cannot ship: $Destination" }
    $actual = Hash $Source
    if ($Expected -and $actual -ne $Expected) { throw "Asset checksum mismatch: $Source" }
    $inputs.Add([pscustomobject]@{source=$Source;path=$Destination;sha256=$actual;bytes=(Get-Item -LiteralPath $Source).Length})
}
Write-Host 'Checking authored bundles and sidecars...'
foreach ($entry in @($manifest.Bundles) + @($manifest.Sidecars)) {
    Add-Input (Resolve-Payload $clientSource $entry.Path) "$clientRelative/$($entry.Path)" $entry.Sha256
}
# Repo-owned database files replace the old direct-registration payload.
$serverFiles = [Collections.Generic.List[object]]::new()
$serverDbRoot = [IO.Path]::GetFullPath((Join-Path $root 'lighthouse-server/db')).TrimEnd('\') + '\'
foreach ($file in Get-ChildItem -LiteralPath $serverDbRoot -Recurse -File -Filter '*.json') {
    $relative = 'db/' + $file.FullName.Substring($serverDbRoot.Length).Replace('\','/')
    Add-Input $file.FullName "$serverRelative/$relative"
    $serverFiles.Add([pscustomobject]@{Path=$relative;Sha256=$inputs[$inputs.Count-1].sha256})
}
$bundleList = Join-Path $root 'lighthouse-server/bundles.json'
Add-Input $bundleList "$serverRelative/bundles.json"
$serverFiles.Add([pscustomobject]@{Path='bundles.json';Sha256=$inputs[$inputs.Count-1].sha256})
$bundleManifest = Get-Content -LiteralPath $bundleList -Raw | ConvertFrom-Json
foreach ($bundle in $bundleManifest.manifest) {
    $relative = 'bundles/' + $bundle.key
    $declaration = @($manifest.ServerFiles | Where-Object Path -eq $relative)
    if ($declaration.Count -ne 1) { throw "Item bundle not declared exactly once: $relative" }
    Add-Input (Resolve-Payload $serverSource $relative) "$serverRelative/$relative" $declaration[0].Sha256
    $serverFiles.Add([pscustomobject]@{Path=$relative;Sha256=$inputs[$inputs.Count-1].sha256})
}
if (-not ($serverFiles.Path -contains 'db/CustomItems/LighthouseItems.json')) { throw 'Missing CommonLib item definitions.' }
if ($ValidateOnly) { Write-Host "Verified $($inputs.Count) authored runtime inputs. Mode: $($manifest.Mode)."; return }
& (Join-Path $root 'build.ps1') -SPTPath $SPTPath
if (-not $?) { throw 'Build failed.' }
Add-Input (Join-Path $root 'lighthouse-client/bin/Release/netstandard2.1/ManimalLighthouse.dll') "$clientRelative/ManimalLighthouse.dll"
Add-Input (Join-Path $root 'lighthouse-shared/bin/Release/netstandard2.1/ManimalLighthouse.Shared.dll') "$clientRelative/ManimalLighthouse.Shared.dll"
Add-Input (Join-Path $root 'lighthouse-server/bin/Release/net10.0/ManimalLighthouse.Server.dll') "$serverRelative/ManimalLighthouse.Server.dll"
Add-Input (Join-Path $root 'lighthouse-shared/bin/Release/netstandard2.1/ManimalLighthouse.Shared.dll') "$serverRelative/ManimalLighthouse.Shared.dll"
$manifest.ServerFiles = @($serverFiles.ToArray())
if (-not $manifest.ContentId.EndsWith('-commonlib1')) { $manifest.ContentId += '-commonlib1' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$manifestJson = $manifest | ConvertTo-Json -Depth 30
$packageFiles = [Collections.Generic.List[object]]::new()
foreach ($entry in $inputs) { $packageFiles.Add($entry) }
function Add-Generated([string]$Destination, [string]$Content) {
    if (-not $seen.Add($Destination)) { throw "Duplicate package path: $Destination" }
    $bytes = [Text.Encoding]::UTF8.GetBytes($Content)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $sha = $hasher.ComputeHash($bytes) }
    finally { $hasher.Dispose() }
    $shaText = ([BitConverter]::ToString($sha)).Replace('-', '').ToLowerInvariant()
    $packageFiles.Add([pscustomobject]@{source=$null;content=$bytes;path=$Destination;sha256=$shaText;bytes=$bytes.Length})
}
foreach ($side in @($clientRelative,$serverRelative)) { Add-Generated "$side/lighthouse-content.json" $manifestJson }
if ($manifest.Mode -eq 'test') {
    Add-Generated "$serverRelative/allow-test" ''
    Add-Generated 'BepInEx/config/com.manimal.lighthouse.cfg' "[Development]`nAllowTestContent = true`n"
}
$packageIndex = @($packageFiles | ForEach-Object { [pscustomobject]@{path=$_.path;sha256=$_.sha256;bytes=$_.bytes} })
$manifestRecord = @($packageIndex | Where-Object path -eq "$clientRelative/lighthouse-content.json")
if ($manifestRecord.Count -ne 1) { throw 'Generated client manifest is missing.' }
$report = [pscustomobject]@{status='passed';readyToDeploy=$true;mode=$manifest.Mode;streamedDirectly=$true;version=$version;manifestSha256=$manifestRecord[0].sha256;files=$packageIndex}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'package-verification.json')
if ($StageOnly) { Write-Host "Verified package plan: $($packageFiles.Count) runtime files; no multi-gigabyte staging copy created."; return }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$tag = if ($manifest.Mode -eq 'test') { '-test' } else { '' }
$archivePaths = [Collections.Generic.List[string]]::new()
foreach ($kind in @('full','update','binaries')) {
    $suffix = if ($kind -eq 'full') { '' } else { "-$kind" }
    $zipPath = Join-Path $OutputDirectory "Manimal-Lighthouse$suffix-$version$tag.zip"
    Write-Host "Creating $kind archive..."
    # Stream source files straight into the archive. The map payload is over
    # eight GiB, so copying it into build/ first can exhaust the system drive.
    $stream = [IO.File]::Open($zipPath, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    $selected = @($packageFiles | Where-Object {
        $kind -eq 'full' -or ($kind -eq 'update' -and -not $_.path.EndsWith('.bundle')) -or ($kind -eq 'binaries' -and $_.path.EndsWith('.dll'))
    })
    try {
        foreach ($entry in $selected) {
            if ($entry.source) {
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.source, $entry.path, [IO.Compression.CompressionLevel]::Fastest)
            } else {
                $generated = $zip.CreateEntry($entry.path, [IO.Compression.CompressionLevel]::Fastest)
                $generatedStream = $generated.Open()
                try { $generatedStream.Write($entry.content, 0, $entry.content.Length) }
                finally { $generatedStream.Dispose() }
            }
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
    # Reopen the ZIP64 archive and verify its exact entry list and lengths.
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        if ($zip.Entries.Count -ne $selected.Count) { throw 'Archive entry count mismatch.' }
        foreach ($entry in $selected) {
            $archived = $zip.GetEntry($entry.path)
            if (-not $archived -or $archived.Length -ne $entry.bytes) { throw "Archive entry mismatch: $($entry.path)" }
        }
    } finally { $zip.Dispose() }
    $archivePaths.Add($zipPath)
}
$archivePaths | ForEach-Object { '{0}  {1}' -f (Hash $_), [IO.Path]::GetFileName($_) } | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt')
Write-Host "Packages verified in $OutputDirectory. Update ZIP requires the same authored bundles; full ZIP is for new installs."
