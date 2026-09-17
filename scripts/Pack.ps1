param([string]$Version, [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
if (-not $Version) { [xml]$props = Get-Content -LiteralPath (Join-Path $smartRoot 'Directory.Build.props'); $Version = $props.Project.PropertyGroup.SmartFrameworkVersion }
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Provide a semantic version.' }
$destination = Join-Path $smartRoot 'artifacts\packages'
$buildArguments = @()
if ($ArtifactsPath) {
    if (-not [IO.Path]::IsPathRooted($ArtifactsPath)) { $ArtifactsPath = Join-Path $smartRoot $ArtifactsPath }
    $ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
    $destination = Join-Path $ArtifactsPath 'packages'
    $buildArguments = @('--artifacts-path', $ArtifactsPath)
}
foreach ($project in Get-ChildItem -LiteralPath (Join-Path $smartRoot 'src') -Filter '*.csproj' -Recurse) {
    & $dotnetPath pack $project.FullName -c Release --nologo -o $destination "-p:SmartFrameworkVersion=$Version" -p:SmartUsePackages=false @buildArguments
    if ($LASTEXITCODE -ne 0) { throw "Package failed: $($project.Name)" }
}
Write-Output ("PACKAGES_VERIFIED " + $destination)
