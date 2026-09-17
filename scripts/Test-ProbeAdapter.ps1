param([string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $smartRoot 'artifacts\probe-adapter-verification' }
if (-not [IO.Path]::IsPathRooted($ArtifactsPath)) { $ArtifactsPath = Join-Path $smartRoot $ArtifactsPath }
$ArtifactsPath = Join-Path $ArtifactsPath ([Guid]::NewGuid().ToString('N'))
$results = Join-Path $ArtifactsPath 'test-results'
# Tests inject managed transports. This command never launches the hardware CLI or opens native devices.
& $dotnetPath test (Join-Path $smartRoot 'validation\Smart.ProbeAdapter.slnx') -c Release --artifacts-path $ArtifactsPath --nologo --logger trx --results-directory $results
if ($LASTEXITCODE -ne 0) { throw 'Offline probe adapter tests failed.' }
& (Join-Path $PSScriptRoot 'Assert-ReleaseOutput.ps1') -Solution (Join-Path $smartRoot 'validation\Smart.ProbeAdapter.slnx') -ArtifactsPath $ArtifactsPath
Write-Output 'PROBE_ADAPTER_OFFLINE_VERIFIED'
