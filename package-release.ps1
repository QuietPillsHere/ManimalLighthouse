#requires -Version 7.2
# Build fresh plugins and package only manifest-declared runtime payloads.
# Authored Unity bundles are supplied by a staged install or -AssetSourcePath.
[CmdletBinding()]
param(
    [string]$SPTPath,
    [string]$AssetSourcePath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [switch]$ValidateOnly,
    [switch]$StageOnly
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
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
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'lighthouse-server/db') -Recurse -File -Filter '*.json') {
    $relative = 'db/' + [IO.Path]::GetRelativePath((Join-Path $root 'lighthouse-server/db'), $file.FullName).Replace('\','/')
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
$stage = Join-Path $root ('build/package-stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
Write-Host 'Staging runtime payload...'
foreach ($entry in $inputs) {
    $destination = Resolve-Payload $stage $entry.path
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $entry.source -Destination $destination
}
$manifestJson = $manifest | ConvertTo-Json -Depth 30
foreach ($side in @($clientRelative,$serverRelative)) {
    [IO.File]::WriteAllText((Join-Path $stage "$side/lighthouse-content.json"), $manifestJson)
}
if ($manifest.Mode -eq 'test') {
    [IO.File]::WriteAllText((Join-Path $stage "$serverRelative/allow-test"), '')
    $cfg = Join-Path $stage 'BepInEx/config/com.manimal.lighthouse.cfg'
    New-Item -ItemType Directory -Path (Split-Path $cfg -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($cfg, "[Development]`nAllowTestContent = true`n")
}
$staged = @(Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\','/')
    [pscustomobject]@{path=$relative;sha256=(Hash $_.FullName);bytes=$_.Length}
})
foreach ($entry in $inputs) {
    $copy = @($staged | Where-Object path -eq $entry.path)
    if ($copy.Count -ne 1 -or $copy[0].sha256 -ne $entry.sha256) { throw "Staged content mismatch: $($entry.path)" }
}
$report = [pscustomobject]@{status='passed';readyToDeploy=$true;mode=$manifest.Mode;stageRoot=$stage;version=$version;manifestSha256=(Hash (Join-Path $stage "$clientRelative/lighthouse-content.json"));files=$staged}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'package-verification.json')
if ($StageOnly) { Write-Host "Verified staging: $stage"; return }
Add-Type -AssemblyName System.IO.Compression
$tag = if ($manifest.Mode -eq 'test') { '-test' } else { '' }
$archivePaths = [Collections.Generic.List[string]]::new()
foreach ($kind in @('full','update','binaries')) {
    $suffix = if ($kind -eq 'full') { '' } else { "-$kind" }
    $zipPath = Join-Path $OutputDirectory "Manimal-Lighthouse$suffix-$version$tag.zip"
    $pending = "$zipPath.$([Guid]::NewGuid().ToString('N')).partial"
    Write-Host "Creating $kind archive..."
    $zip = [IO.Compression.ZipFile]::Open($pending, [IO.Compression.ZipArchiveMode]::Create)
    $selected = @($staged | Where-Object {
        $kind -eq 'full' -or ($kind -eq 'update' -and -not $_.path.EndsWith('.bundle')) -or ($kind -eq 'binaries' -and $_.path.EndsWith('.dll'))
    })
    try {
        foreach ($entry in $selected) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $stage $entry.path), $entry.path, [IO.Compression.CompressionLevel]::Fastest)
        }
    } finally { $zip.Dispose() }
    # Reopen the ZIP64 archive and verify its exact entry list and lengths.
    $zip = [IO.Compression.ZipFile]::OpenRead($pending)
    try {
        if ($zip.Entries.Count -ne $selected.Count) { throw 'Archive entry count mismatch.' }
        foreach ($entry in $selected) {
            $archived = $zip.GetEntry($entry.path)
            if (-not $archived -or $archived.Length -ne $entry.bytes) { throw "Archive entry mismatch: $($entry.path)" }
        }
    } finally { $zip.Dispose() }
    [IO.File]::Move($pending,$zipPath,$true)
    $archivePaths.Add($zipPath)
}
$archivePaths | ForEach-Object { '{0}  {1}' -f (Hash $_), [IO.Path]::GetFileName($_) } | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt')
# Delete only the unique staging directory this invocation created under build/.
$stagePrefix = [IO.Path]::GetFullPath((Join-Path $root 'build')).TrimEnd('\') + '\'
if (-not [IO.Path]::GetFullPath($stage).StartsWith($stagePrefix,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $stage -Leaf) -notmatch '^package-stage-[a-f0-9]{32}$') { throw 'Unsafe staging cleanup path.' }
Remove-Item -LiteralPath $stage -Recurse -Force
Write-Host "Packages verified in $OutputDirectory. Update ZIP requires the same authored bundles; full ZIP is for new installs."
