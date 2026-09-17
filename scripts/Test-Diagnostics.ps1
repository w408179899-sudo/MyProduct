param([string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $smartRoot 'artifacts\diagnostic-verification' }
if (-not [IO.Path]::IsPathRooted($ArtifactsPath)) { $ArtifactsPath = Join-Path $smartRoot $ArtifactsPath }
$ArtifactsPath = Join-Path $ArtifactsPath ([Guid]::NewGuid().ToString('N'))
# Fake memory transports and loopback UDP only. Never launch the hardware command-line entrypoints.
& $dotnetPath test (Join-Path $smartRoot 'validation\Smart.Diagnostics.slnx') -c Release --artifacts-path $ArtifactsPath --nologo --logger trx --results-directory (Join-Path $ArtifactsPath 'test-results')
if ($LASTEXITCODE -ne 0) { throw 'Offline diagnostic tests failed.' }
& (Join-Path $PSScriptRoot 'Assert-ReleaseOutput.ps1') -Solution (Join-Path $smartRoot 'validation\Smart.Diagnostics.slnx') -ArtifactsPath $ArtifactsPath
Write-Output 'DIAGNOSTICS_OFFLINE_VERIFIED'
