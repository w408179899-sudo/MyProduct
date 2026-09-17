param([string]$ManifestPath, [string]$ArtifactsPath, [switch]$SelfContained, [string]$NativeWorkerProject)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
if (-not $ManifestPath) {
    $publishParameters = @{ SelfContained = $SelfContained }
    if ($ArtifactsPath) { $publishParameters.ArtifactsPath = $ArtifactsPath }
    if ($NativeWorkerProject) { $publishParameters.NativeWorkerProject = $NativeWorkerProject }
    $output = @(& (Join-Path $PSScriptRoot 'Publish-Applications.ps1') @publishParameters | Tee-Object -Variable publicationOutput)
    $output | Write-Output
    $marker = @($publicationOutput | Where-Object { $_ -is [string] -and $_.StartsWith('APPLICATIONS_PUBLISHED ') })
    if ($marker.Count -ne 1) { throw 'Publication did not produce exactly one manifest.' }
    $ManifestPath = $marker[0].Substring('APPLICATIONS_PUBLISHED '.Length)
}
$ManifestPath = (Resolve-Path -LiteralPath $ManifestPath).Path
& (Join-Path $PSScriptRoot 'Assert-ApplicationPublish.ps1') -ManifestPath $ManifestPath
$payload = Split-Path -Parent $ManifestPath
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$smoke = Join-Path (Split-Path -Parent $payload) ('smoke\' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($smoke) | Out-Null
$originalDotnetRoot = $env:DOTNET_ROOT
$originalDotnetRootX64 = $env:DOTNET_ROOT_X64
$env:DOTNET_ROOT = Split-Path -Parent $dotnetPath
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$reports = @()
try {
    foreach ($target in $manifest.Targets) {
        $source = Join-Path $payload $target.Directory
        $copy = Join-Path $smoke $target.Directory
        Copy-Item -LiteralPath $source -Destination $copy -Recurse
        $stdout = Join-Path $smoke ($target.Name + '.stdout.log')
        $stderr = Join-Path $smoke ($target.Name + '.stderr.log')
        $arguments = if ($target.Name -eq 'Console') { @('--duration-ms', '300') } else { @('--smoke') }
        $process = Start-Process -FilePath (Join-Path $copy $target.EntryPoint) -ArgumentList $arguments -WorkingDirectory $copy `
            -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        try {
            $childHandle = $process.Handle
            if (-not $process.WaitForExit(20000)) { $process.Kill(); throw "Published $($target.Name) Mock smoke timed out." }
            if ($process.ExitCode -ne 0) { throw "Published $($target.Name) Mock smoke failed: $($process.ExitCode). See $stderr" }
            if ($target.Name -eq 'Console') {
                $lines = @(Get-Content -LiteralPath $stdout | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                if ($lines.Count -ne 1) { throw 'Published Console must emit one Mock account result.' }
                $result = $lines[0] | ConvertFrom-Json
                if ($result.Mode -ne 0 -or $result.Status.State -ne 0 -or $result.Status.Generation -lt 1 -or
                    $null -ne $result.Status.Error -or $null -ne $result.Failure) { throw 'Published Console did not complete its Mock session.' }
            }
            $reports += [ordered]@{ Target = $target.Name; ExitCode = $process.ExitCode; Mode = 'Mock'; Output = $stdout; Error = $stderr }
        }
        finally { $process.Dispose() }
        # --help exits before creating a pipe or initializing native libraries/devices.
        # Mock hosts do not start the worker, so verify this deployed apphost separately.
        $workerDirectory = Join-Path $copy $target.NativeWorker.Directory
        $workerStdout = Join-Path $smoke ($target.Name + '.worker.stdout.log')
        $workerStderr = Join-Path $smoke ($target.Name + '.worker.stderr.log')
        $worker = Start-Process -FilePath (Join-Path $workerDirectory $target.NativeWorker.EntryPoint) -ArgumentList '--help' `
            -WorkingDirectory $workerDirectory -WindowStyle Hidden -RedirectStandardOutput $workerStdout -RedirectStandardError $workerStderr -PassThru
        try {
            $workerHandle = $worker.Handle
            if (-not $worker.WaitForExit(10000)) { $worker.Kill(); throw "Published $($target.Name) worker help timed out." }
            if ($worker.ExitCode -ne 0 -or (Get-Content -LiteralPath $workerStdout -Raw) -notmatch 'no device is opened without its private pipe handshake') {
                throw "Published $($target.Name) worker did not complete its offline help path. See $workerStderr"
            }
            $reports += [ordered]@{ Target = $target.Name + '/native-worker'; ExitCode = $worker.ExitCode; Mode = 'Help'; Output = $workerStdout; Error = $workerStderr }
        }
        finally { $worker.Dispose() }
    }
}
finally { $env:DOTNET_ROOT = $originalDotnetRoot; $env:DOTNET_ROOT_X64 = $originalDotnetRootX64 }
# Smoke runs only in copies. The distributable remains byte-for-byte identical and has no new data/logs.
& (Join-Path $PSScriptRoot 'Assert-ApplicationPublish.ps1') -ManifestPath $ManifestPath
[ordered]@{ FrameworkVersion = $manifest.FrameworkVersion; Publication = $ManifestPath; Targets = $reports } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $smoke 'smoke-results.json') -Encoding UTF8
Write-Output ("PUBLISHED_APPLICATIONS_MOCK_VERIFIED " + $smoke)
