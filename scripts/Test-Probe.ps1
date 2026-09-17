param([string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$solution = Join-Path $smartRoot 'validation\Smart.Probe.slnx'
if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $smartRoot 'artifacts\probe-verification' }
if (-not [IO.Path]::IsPathRooted($ArtifactsPath)) { $ArtifactsPath = Join-Path $smartRoot $ArtifactsPath }
$ArtifactsPath = Join-Path $ArtifactsPath ([Guid]::NewGuid().ToString('N'))
# This solution has no Smart core/adapter references and does not rebuild a running host's DLLs.
& $dotnetPath test $solution -c Release --artifacts-path $ArtifactsPath --nologo --logger trx --results-directory (Join-Path $ArtifactsPath 'test-results')
if ($LASTEXITCODE -ne 0) { throw 'Probe protocol/target tests failed.' }
& (Join-Path $PSScriptRoot 'Assert-ReleaseOutput.ps1') -Solution $solution -ArtifactsPath $ArtifactsPath
$run = Join-Path $ArtifactsPath 'target-smoke'
[IO.Directory]::CreateDirectory($run) | Out-Null
$manifest = Join-Path $run 'target.json'
$stdout = Join-Path $run 'stdout.jsonl'
$stderr = Join-Path $run 'stderr.jsonl'
$target = Join-Path $ArtifactsPath 'bin\Smart.ProbeTarget\release\Smart.ProbeTarget.dll'
$process = Start-Process -FilePath $dotnetPath -ArgumentList @('"' + $target + '"', '--mode', 'mixed', '--interval-ms', '20', '--torn-ms', '50', '--duration-ms', '1000', '--manifest', '"' + $manifest + '"') -WorkingDirectory $smartRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
try {
    # Retain the handle before exit so Windows PowerShell can retrieve the child's actual code.
    $childHandle = $process.Handle
    if (-not $process.WaitForExit(10000)) {
        # Only the exact finite probe child created by this script is terminated on failure.
        $process.Kill()
        throw 'Probe target smoke timed out.'
    }
    if ($process.ExitCode -ne 0) { throw ('Probe target failed: ' + (Get-Content -Raw -LiteralPath $stderr)) }
    $lines = @(Get-Content -LiteralPath $stdout)
    if ($lines.Count -ne 1) { throw 'Expected exactly one stdout manifest.' }
    $reported = $lines[0] | ConvertFrom-Json
    $saved = Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
    if ($reported.SessionId -ne $saved.SessionId -or $reported.Address -ne $saved.Address -or $reported.ProcessId -ne $process.Id -or $reported.Length -ne 96 -or $reported.PointerSize -ne 8) {
        throw 'Manifest file/stdout/process identity mismatch.'
    }
    $stopped = Get-Content -Raw -LiteralPath $stderr | ConvertFrom-Json
    if ($stopped.Event -ne 'target.stopped' -or $stopped.LastPublishedCounter -le 0 -or $stopped.TornWindows -le 0) {
        throw 'The finite process did not exercise normal progress and intentional torn windows.'
    }
    Write-Output ('PROBE_TARGET_VERIFIED ' + $run)
}
finally { $process.Dispose() }
