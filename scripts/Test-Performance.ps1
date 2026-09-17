param([string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$project = Join-Path $smartRoot 'benchmarks\Smart.Benchmarks\Smart.Benchmarks.csproj'
$buildArguments = @()
$destination = Join-Path $smartRoot 'artifacts\benchmark.json'
if ($ArtifactsPath) {
    if (-not [IO.Path]::IsPathRooted($ArtifactsPath)) { $ArtifactsPath = Join-Path $smartRoot $ArtifactsPath }
    $ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
    $buildArguments = @('--artifacts-path', $ArtifactsPath)
    $destination = Join-Path $ArtifactsPath 'benchmark.json'
}
& $dotnetPath build $project -c Release --nologo @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
if ($ArtifactsPath) {
    $assembly = Join-Path $ArtifactsPath 'bin\Smart.Benchmarks\release\Smart.Benchmarks.dll'
    $json = & $dotnetPath $assembly
}
else { $json = & $dotnetPath run --project $project -c Release --no-build }
if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed.' }
$result = ($json -join [Environment]::NewLine) | ConvertFrom-Json
$budgets = Get-Content -Raw -LiteralPath (Join-Path $smartRoot 'benchmarks\budgets.json') | ConvertFrom-Json
[IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
$json | Set-Content -Encoding UTF8 -LiteralPath $destination
foreach ($sample in $result.Results) {
    switch ($sample.Scenario) {
        'cached_read' {
            if ($sample.Captures -ne 1 -or $sample.P99Microseconds -gt $budgets.MaximumCachedP99Microseconds -or
                $sample.AllocatedBytes -gt $budgets.MaximumCachedAllocatedBytes) { throw 'Cached read budget failed.' }
        }
        'capture_merge_publish' {
            if ($sample.Captures -ne ($sample.Iterations + 1) -or $sample.P99Microseconds -gt $budgets.MaximumCaptureP99Microseconds) { throw 'Capture budget failed.' }
        }
        'partial_collection_2000x1000' {
            if ($sample.Objects -ne 1882 -or $sample.TotalMilliseconds -gt $budgets.MaximumCollectionMilliseconds) { throw 'Collection correctness/performance budget failed.' }
        }
        'bounded_work_modules' {
            if ($sample.ProcessCpuMs -gt ($sample.Accounts * $budgets.MaximumCpuMillisecondsPerAccount) -or $sample.Ticks -lt ($sample.Accounts * $budgets.MinimumTicksPerAccount)) { throw 'Module scheduling budget failed.' }
        }
        default { throw ('Unknown benchmark scenario: ' + $sample.Scenario) }
    }
}
Write-Output ("PERFORMANCE_BUDGETS_PASSED " + $destination)
