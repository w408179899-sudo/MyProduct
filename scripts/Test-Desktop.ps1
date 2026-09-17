$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$assemblyPath = Join-Path $smartRoot 'template\SampleProject.Desktop\bin\Release\net10.0-windows\SampleProject.Desktop.dll'
if (-not (Test-Path -LiteralPath $assemblyPath)) { throw 'Build the solution first.' }
$process = Start-Process -FilePath $dotnetPath -ArgumentList @('"' + $assemblyPath + '"', '--smoke') -WorkingDirectory $smartRoot -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(15000)) {
    # Only terminate the exact smoke-test child created above.
    $process.Kill()
    throw 'Desktop smoke test timed out.'
}
if ($process.ExitCode -ne 0) { throw "Desktop smoke failed: $($process.ExitCode)" }
Write-Output 'DESKTOP_SMOKE_PASSED'
