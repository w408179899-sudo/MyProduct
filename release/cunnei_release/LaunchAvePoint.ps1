$ErrorActionPreference = "Stop"
$baseDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$configPath = Join-Path $baseDir "config.json"
$exePath = Join-Path $baseDir "AetherRunner_vmp.exe"

if (-not (Test-Path $exePath)) {
    throw "Missing required file: AetherRunner_vmp.exe"
}

$showWindow = $true
if (Test-Path $configPath) {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    $savedUserCard = ""
    if ($null -ne $config.savedUserCard) {
        $savedUserCard = [string]$config.savedUserCard
    }

    $showWindow = [string]::IsNullOrWhiteSpace($savedUserCard)
    if ($config.PSObject.Properties.Match("enableConsole").Count -gt 0) { $config.enableConsole = $false }
    if ($config.PSObject.Properties.Match("enableLogConsole").Count -gt 0) { $config.enableLogConsole = $false }
    if ($config.PSObject.Properties.Match("enableLogDebugView").Count -gt 0) { $config.enableLogDebugView = $false }
    if ($config.PSObject.Properties.Match("enableWindow").Count -gt 0) {
        $config.enableWindow = $showWindow
    } else {
        $config | Add-Member -NotePropertyName "enableWindow" -NotePropertyValue $showWindow -Force
    }

    $config | ConvertTo-Json -Depth 16 | Set-Content -Path $configPath -Encoding UTF8
}

if ($showWindow) {
    Start-Process -FilePath $exePath -WorkingDirectory $baseDir
} else {
    Start-Process -FilePath $exePath -WorkingDirectory $baseDir -WindowStyle Hidden
}
