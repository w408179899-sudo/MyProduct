param([ValidateRange(10,86400)][int]$DurationSeconds = 60, [ValidateRange(1,32)][int]$Accounts = 8, [string]$ArtifactsPath)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$buildArguments = @()
$destination = Join-Path $smartRoot 'artifacts\soak.json'
if ($ArtifactsPath) {
    if (-not [IO.Path]::IsPathRooted($ArtifactsPath)) { $ArtifactsPath = Join-Path $smartRoot $ArtifactsPath }
    $ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
    $buildArguments = @('--artifacts-path', $ArtifactsPath)
    $destination = Join-Path $ArtifactsPath 'soak.json'
}
Push-Location $smartRoot
try {
    & $dotnetPath build benchmarks/Smart.Benchmarks -c Release --nologo @buildArguments
    if ($LASTEXITCODE -ne 0) { throw 'Soak build failed.' }
    if ($ArtifactsPath) {
        $assembly = Join-Path $ArtifactsPath 'bin\Smart.Benchmarks\release\Smart.Benchmarks.dll'
        $runDirectory = Join-Path $ArtifactsPath 'runtime'
        [IO.Directory]::CreateDirectory($runDirectory) | Out-Null
        # The benchmark writes relative diagnostic paths. Keep those separate from an already-running soak too.
        Push-Location $runDirectory
        try { $result = & $dotnetPath $assembly --soak $DurationSeconds $Accounts }
        finally { Pop-Location }
    }
    else { $result = & $dotnetPath run --project benchmarks/Smart.Benchmarks -c Release --no-build -- --soak $DurationSeconds $Accounts }
    if ($LASTEXITCODE -ne 0) { throw 'Soak acceptance failed.' }
    $result | Set-Content -Encoding UTF8 -LiteralPath $destination
    $result
    Write-Output 'SOAK_ACCEPTANCE_PASSED'
}
finally { Pop-Location }
