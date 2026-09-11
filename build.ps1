#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$SPTPath,
    [string]$CommonLibPath
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$identity = $props.Project.PropertyGroup
if ([string]$identity.ModVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'ModVersion must be a three-part semantic release version.' }
if ([string]$identity.ModUsername -notmatch '^[A-Za-z0-9]+$' -or [string]$identity.ModDisplayName -notmatch '^[A-Za-z0-9]+$') { throw 'ModUsername and ModDisplayName must contain only letters and numbers.' }
if ([string]$identity.ModGuid -notmatch '^com\.[a-z0-9]+\.[a-z0-9]+$') { throw 'ModGuid must use com.username.modname.' }
if (-not [string]::IsNullOrWhiteSpace([string]$identity.ModSourceUrl)) {
    $sourceUri = $null
    if (-not [Uri]::TryCreate([string]$identity.ModSourceUrl, [UriKind]::Absolute, [ref]$sourceUri) -or $sourceUri.Scheme -ne 'https') { throw 'ModSourceUrl must be an absolute HTTPS source repository URL.' }
}
if (-not $SPTPath) { $SPTPath = [string]$props.Project.PropertyGroup.SPTPath.'#text' }
$SPTPath = (Resolve-Path -LiteralPath $SPTPath).Path
$serverPath = Join-Path $SPTPath 'SPT_Runtime'
if (-not $CommonLibPath) { $CommonLibPath = Join-Path $serverPath 'user/mods/WTT-ServerCommonLib/WTT-ServerCommonLib.dll' }
foreach ($inputPath in @($CommonLibPath, (Join-Path $serverPath 'user/mods/WTT-ContentBackport/WTT-ContentBackport.dll'))) {
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw "Required build dependency missing: $inputPath" }
}
$commonVersion = [Reflection.AssemblyName]::GetAssemblyName($CommonLibPath).Version
if ($commonVersion -lt [Version]'3.0.6.0' -or $commonVersion -ge [Version]'3.1.0.0') { throw "CommonLib 3.0.6+ (3.0.x) required; found $commonVersion" }
$backportVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $serverPath 'user/mods/WTT-ContentBackport/WTT-ContentBackport.dll')).Version
if ($backportVersion -lt [Version]'2.0.1.0' -or $backportVersion -ge [Version]'2.1.0.0') { throw "ContentBackport 2.0.1+ (2.0.x) required; found $backportVersion" }
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_PROCESSOR_COUNT = '1'
& dotnet restore (Join-Path $root 'ManimalLighthouse.sln') --configfile (Join-Path $root 'NuGet.Config') "-p:SPTPath=$SPTPath" "-p:ServerPath=$serverPath" "-p:CommonLibPath=$CommonLibPath"
if ($LASTEXITCODE -ne 0) { throw 'Lighthouse restore failed.' }
& dotnet build (Join-Path $root 'ManimalLighthouse.sln') -c $Configuration --no-restore "-p:SPTPath=$SPTPath" "-p:ServerPath=$serverPath" "-p:CommonLibPath=$CommonLibPath"
if ($LASTEXITCODE -ne 0) { throw 'Lighthouse build failed.' }

