param(
    [ValidateSet('Build','Test','Demo','Desktop','Benchmark','TemplateSmoke')]
    [string]$Action = 'Test'
)
$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $smartRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
Push-Location $smartRoot
try {
    switch ($Action) {
        'Build' { & $dotnetPath build Smart.slnx -c Release --nologo }
        'Test' {
            & $dotnetPath build Smart.slnx -c Release --nologo
            if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
            & $dotnetPath test Smart.slnx -c Release --no-build --nologo --logger trx --results-directory artifacts/test-results
        }
        'Demo' { & $dotnetPath run --project template/SampleProject.Console -c Release }
        'Desktop' { & $dotnetPath run --project template/SampleProject.Desktop -c Release }
        'Benchmark' { & $dotnetPath run --project benchmarks/Smart.Benchmarks -c Release }
        'TemplateSmoke' { & (Join-Path $PSScriptRoot 'Test-Template.ps1') -DotNet $dotnetPath }
    }
    if ($LASTEXITCODE -ne 0) { throw "Command failed: $Action ($LASTEXITCODE)" }
}
finally { Pop-Location }
