$ErrorActionPreference = 'Stop'
$smartRoot = Split-Path -Parent $PSScriptRoot
$toolsDirectory = Join-Path $smartRoot '.tools'
New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
$installer = Join-Path $toolsDirectory 'dotnet-install.ps1'
Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
$version = (Get-Content -Raw -LiteralPath (Join-Path $smartRoot 'global.json') | ConvertFrom-Json).sdk.version
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -Version $version -InstallDir (Join-Path $toolsDirectory 'dotnet') -NoPath
if ($LASTEXITCODE -ne 0) { throw 'SDK installation failed.' }
