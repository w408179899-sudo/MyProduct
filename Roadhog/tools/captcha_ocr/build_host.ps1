param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot '..\..\..\.tmp\lh-ocr-host'
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework x86 compiler is unavailable.' }
$source = Join-Path $PSScriptRoot 'LhOcrHost.cs.txt'
$executable = Join-Path $outputRoot 'LhOcrHost.exe'
& $compiler /nologo /target:exe /platform:x86 /optimize+ /warnaserror+ /r:System.Web.Extensions.dll "/out:$executable" $source
if ($LASTEXITCODE -ne 0) { throw 'OCR host compilation failed.' }
$executable
