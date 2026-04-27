param(
    [switch]$SkipZip,
    [string]$ReleaseName = "cunnei_release",
    [switch]$NoDriver
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseRoot = Join-Path $projectRoot "release"
$releaseName = $ReleaseName
$releaseDir = Join-Path $releaseRoot $releaseName
$zipPath = Join-Path $releaseRoot ($releaseName + ".zip")
$luacPath = Join-Path $projectRoot "luac.exe"

if (-not (Test-Path $luacPath)) {
    throw "Missing required compiler: luac.exe"
}

function Ensure-Directory {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function Set-ObjectProperty {
    param(
        [object]$Object,
        [string]$Name,
        $Value
    )

    if ($null -eq $Object) {
        return
    }

    if ($Object.PSObject.Properties.Match($Name).Count -gt 0) {
        $Object.$Name = $Value
    }
    else {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
    }
}

function Compile-LuaFile {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    Ensure-Directory -Path (Split-Path -Parent $DestinationPath)
    & $luacPath -o $DestinationPath $SourcePath
    if ($LASTEXITCODE -ne 0) {
        throw "luac failed: $SourcePath -> $DestinationPath"
    }
}

function Publish-CompiledRootFile {
    param(
        [string]$RelativeSourcePath,
        [string]$RelativeDestinationPath
    )

    $source = Join-Path $projectRoot $RelativeSourcePath
    if (-not (Test-Path $source)) {
        throw "Missing required script: $RelativeSourcePath"
    }

    $destination = Join-Path $releaseDir $RelativeDestinationPath
    Compile-LuaFile -SourcePath $source -DestinationPath $destination
}

function Publish-CompiledLuaText {
    param(
        [string]$LuaText,
        [string]$DestinationPath
    )

    Ensure-Directory -Path (Split-Path -Parent $DestinationPath)

    $tempSource = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString() + ".lua")
    try {
        [System.IO.File]::WriteAllText($tempSource, $LuaText, [System.Text.UTF8Encoding]::new($false))
        & $luacPath -o $DestinationPath $tempSource
        if ($LASTEXITCODE -ne 0) {
            throw "luac failed for generated script -> $DestinationPath"
        }
    }
    finally {
        if (Test-Path $tempSource) {
            Remove-Item $tempSource -Force
        }
    }
}

function Publish-CompiledTree {
    param(
        [string]$RelativeSourceDir,
        [string]$RelativeDestinationDir
    )

    $sourceDir = Join-Path $projectRoot $RelativeSourceDir
    if (-not (Test-Path $sourceDir)) {
        throw "Missing required directory: $RelativeSourceDir"
    }

    $destinationDir = Join-Path $releaseDir $RelativeDestinationDir
    Ensure-Directory -Path $destinationDir

    $sourceDirPrefix = $sourceDir.TrimEnd('\', '/')
    foreach ($item in Get-ChildItem -Path $sourceDir -Recurse -File) {
        $relativePath = $item.FullName.Substring($sourceDirPrefix.Length).TrimStart('\', '/')
        if ($item.Extension -ieq ".lua") {
            $compiledRelativePath = [System.IO.Path]::ChangeExtension($relativePath, ".luac")
            $destinationPath = Join-Path $destinationDir $compiledRelativePath
            Compile-LuaFile -SourcePath $item.FullName -DestinationPath $destinationPath
        }
        elseif ($item.Extension -ieq ".luac") {
            $pairedLuaPath = [System.IO.Path]::ChangeExtension($item.FullName, ".lua")
            if (Test-Path $pairedLuaPath) {
                continue
            }

            $destinationPath = Join-Path $destinationDir $relativePath
            Ensure-Directory -Path (Split-Path -Parent $destinationPath)
            Copy-Item -Path $item.FullName -Destination $destinationPath -Force
        }
        else {
            $destinationPath = Join-Path $destinationDir $relativePath
            Ensure-Directory -Path (Split-Path -Parent $destinationPath)
            Copy-Item -Path $item.FullName -Destination $destinationPath -Force
        }
    }
}

if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}

Ensure-Directory -Path $releaseRoot
Ensure-Directory -Path $releaseDir

$binaryFilesToCopy = @(
    "AetherRunner_vmp.exe"
)

foreach ($relativePath in $binaryFilesToCopy) {
    $source = Join-Path $projectRoot $relativePath
    if (-not (Test-Path $source)) {
        throw "Missing required file: $relativePath"
    }

    Copy-Item -Path $source -Destination (Join-Path $releaseDir $relativePath) -Force
}

$compiledRootFiles = @(
    @{ Source = "Main.lua"; Destination = "Main.luac" },
    @{ Source = "ReleaseBootstrap.lua"; Destination = "ReleaseBootstrap.luac" },
    @{ Source = "ReleaseLauncher.lua"; Destination = "ReleaseLauncher.luac" }
)

foreach ($file in $compiledRootFiles) {
    Publish-CompiledRootFile -RelativeSourcePath $file.Source -RelativeDestinationPath $file.Destination
}

$avepointStandaloneText = @'
_G.__CUNNEI_AVEPOINT_RUNTIME_MODE = "api"
_G.__CUNNEI_AVEPOINT_PROTECT_PROCESS = false

local function extend_package_path(script_path)
    if type(package) ~= "table" or type(package.path) ~= "string" then
        return
    end

    local dir = script_path and script_path:match("^(.*)[/\\][^/\\]+$")
    if not dir or dir == "" then
        return
    end

    local patterns = {
        dir .. "/?.lua",
        dir .. "/?/init.lua",
        dir .. "/?.luac",
        dir .. "/?/init.luac"
    }

    for _, pattern in ipairs(patterns) do
        if not package.path:find(pattern, 1, true) then
            package.path = pattern .. ";" .. package.path
        end
    end
end

local entry_script = "scripts/AvePoint.lua"
local candidates = {
    "scripts/AvePoint.luac",
    entry_script
}

extend_package_path(entry_script)

local last_err = nil
for _, candidate in ipairs(candidates) do
    local chunk, err = loadfile(candidate)
    if chunk then
        log.info("AvePoint standalone entry starting")
        return chunk()
    end
    last_err = err
end

error("AvePoint standalone load failed: " .. tostring(last_err or entry_script))
'@
Publish-CompiledLuaText -LuaText $avepointStandaloneText -DestinationPath (Join-Path $releaseDir "AvePointStandalone.luac")

$optionalFilesToCopy = @(
    "key.txt"
)

foreach ($relativePath in $optionalFilesToCopy) {
    $source = Join-Path $projectRoot $relativePath
    if (Test-Path $source) {
        $destination = Join-Path $releaseDir $relativePath
        Copy-Item -Path $source -Destination $destination -Force
        if ($relativePath -ieq "key.txt") {
            $item = Get-Item $destination -Force
            if ($item.IsReadOnly) {
                $item.IsReadOnly = $false
            }
        }
    }
}

Publish-CompiledTree -RelativeSourceDir "scripts" -RelativeDestinationDir "scripts"

$assetDirsToCopy = @(
    "Ha",
    "map"
)

foreach ($relativePath in $assetDirsToCopy) {
    $source = Join-Path $projectRoot $relativePath
    if (-not (Test-Path $source)) {
        throw "Missing required directory: $relativePath"
    }

    Copy-Item -Path $source -Destination (Join-Path $releaseDir $relativePath) -Recurse -Force
}

$configPath = Join-Path $projectRoot "config.json"
if (-not (Test-Path $configPath)) {
    throw "Missing required file: config.json"
}

$config = Get-Content $configPath -Raw | ConvertFrom-Json
$config.mainScript = "AvePointStandalone.luac"
Set-ObjectProperty -Object $config -Name "licenseProfile" -Value "release"
$config.enableConsole = $false
$config.enableLogConsole = $false
$config.enableLogFile = $true
Set-ObjectProperty -Object $config -Name "enableLogDebugView" -Value $false
Set-ObjectProperty -Object $config -Name "enableWindow" -Value $true
$config.openedFiles = @()
$config.recentFiles = @()
$config.activeTabIndex = 0

$savedUserCard = ""
if ($null -ne $config.savedUserCard) {
    $savedUserCard = [string]$config.savedUserCard
}
if (-not [string]::IsNullOrWhiteSpace($savedUserCard)) {
    $config.savedUserCard = $savedUserCard
}

$savedDriverCard = ""
if ($null -ne $config.savedDriverCard) {
    $savedDriverCard = [string]$config.savedDriverCard
}
if ([string]::IsNullOrWhiteSpace($savedDriverCard) -and $null -ne $config.savedDevCard) {
    $savedDriverCard = [string]$config.savedDevCard
}
Set-ObjectProperty -Object $config -Name "savedDriverCard" -Value $savedDriverCard
Set-ObjectProperty -Object $config -Name "savedDevCard" -Value ""
Set-ObjectProperty -Object $config -Name "savedDriverCard" -Value ""
Set-ObjectProperty -Object $config -Name "avepointRuntimeMode" -Value "api"
Set-ObjectProperty -Object $config -Name "avepointProtectProcess" -Value $false

if ($null -ne $config.gridMapEditor) {
    $config.gridMapEditor.expression = ""
    $config.gridMapEditor.mapFilePath = ""
    $config.gridMapEditor.mapName = "world"
    $config.gridMapEditor.processName = ""
}

if ($null -ne $config.waypointMapEditor) {
    $config.waypointMapEditor.expression = ""
    $config.waypointMapEditor.mapFilePath = ""
    $config.waypointMapEditor.processName = ""
    $config.waypointMapEditor.pid = 0
}

$releaseConfigPath = Join-Path $releaseDir "config.json"
$config | ConvertTo-Json -Depth 16 | Set-Content -Path $releaseConfigPath -Encoding UTF8

$releaseLaunchAvePointPath = Join-Path $releaseDir "LaunchAvePoint.cmd"
@(
    "@echo off",
    "cd /d ""%~dp0""",
    "wscript.exe //nologo ""%~dp0LaunchAvePoint.vbs"""
) | Set-Content -Path $releaseLaunchAvePointPath -Encoding ASCII

$releaseLaunchAvePointPs1Path = Join-Path $releaseDir "LaunchAvePoint.ps1"
@(
    '$ErrorActionPreference = "Stop"',
    '$baseDir = Split-Path -Parent $MyInvocation.MyCommand.Path',
    '$configPath = Join-Path $baseDir "config.json"',
    '$exePath = Join-Path $baseDir "AetherRunner_vmp.exe"',
    '',
    'if (-not (Test-Path $exePath)) {',
    '    throw "Missing required file: AetherRunner_vmp.exe"',
    '}',
    '',
    '$showWindow = $true',
    'if (Test-Path $configPath) {',
    '    $config = Get-Content $configPath -Raw | ConvertFrom-Json',
    '    $savedUserCard = ""',
    '    if ($null -ne $config.savedUserCard) {',
    '        $savedUserCard = [string]$config.savedUserCard',
    '    }',
    '',
    '    $showWindow = [string]::IsNullOrWhiteSpace($savedUserCard)',
    '    if ($config.PSObject.Properties.Match("enableConsole").Count -gt 0) { $config.enableConsole = $false }',
    '    if ($config.PSObject.Properties.Match("enableLogConsole").Count -gt 0) { $config.enableLogConsole = $false }',
    '    if ($config.PSObject.Properties.Match("enableLogDebugView").Count -gt 0) { $config.enableLogDebugView = $false }',
    '    if ($config.PSObject.Properties.Match("enableWindow").Count -gt 0) {',
    '        $config.enableWindow = $showWindow',
    '    } else {',
    '        $config | Add-Member -NotePropertyName "enableWindow" -NotePropertyValue $showWindow -Force',
    '    }',
    '',
    '    $config | ConvertTo-Json -Depth 16 | Set-Content -Path $configPath -Encoding UTF8',
    '}',
    '',
    'if ($showWindow) {',
    '    Start-Process -FilePath $exePath -WorkingDirectory $baseDir',
    '} else {',
    '    Start-Process -FilePath $exePath -WorkingDirectory $baseDir -WindowStyle Hidden',
    '}'
) | Set-Content -Path $releaseLaunchAvePointPs1Path -Encoding ASCII

$releaseLaunchAvePointVbsPath = Join-Path $releaseDir "LaunchAvePoint.vbs"
@(
    "Set fso = CreateObject(""Scripting.FileSystemObject"")",
    "Set shell = CreateObject(""WScript.Shell"")",
    "baseDir = fso.GetParentFolderName(WScript.ScriptFullName)",
    "shell.CurrentDirectory = baseDir",
    "cmd = ""powershell.exe -ExecutionPolicy Bypass -File "" & Chr(34) & fso.BuildPath(baseDir, ""LaunchAvePoint.ps1"") & Chr(34)",
    "shell.Run cmd, 0, False"
) | Set-Content -Path $releaseLaunchAvePointVbsPath -Encoding ASCII

$releaseTemplateDir = Join-Path $projectRoot "release_templates"
$readmeTemplatePath = Join-Path $releaseTemplateDir "README_release.txt"
$scriptGuideTemplatePath = Join-Path $releaseTemplateDir "AvePointGuide.txt"

if (-not (Test-Path $readmeTemplatePath)) {
    throw "Missing required template: release_templates/README_release.txt"
}

if (-not (Test-Path $scriptGuideTemplatePath)) {
    throw "Missing required template: release_templates/AvePointGuide.txt"
}

Copy-Item -Path $readmeTemplatePath -Destination (Join-Path $releaseDir "README_release.txt") -Force
Copy-Item -Path $scriptGuideTemplatePath -Destination (Join-Path $releaseDir "AvePointGuide.txt") -Force

Write-Output ("Release directory: " + $releaseDir)

if (-not $SkipZip) {
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $releaseDir "*") -DestinationPath $zipPath -Force
    Write-Output ("Release zip: " + $zipPath)
}
