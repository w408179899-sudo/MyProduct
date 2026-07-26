param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$DestinationRoot
)

$ErrorActionPreference = "Stop"

$runtimeFiles = @("Roadhog.exe", "Roadhog.dll")
$runtimeAssets = @("Source\gather_src.xml")
$destinationNames = @("1", "2", "3", "4")

function Test-FileInUse {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $stream = $null
    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        return $false
    }
    catch [System.IO.IOException] {
        return $true
    }
    catch [System.UnauthorizedAccessException] {
        return $true
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

$sourceFiles = foreach ($relativePath in ($runtimeFiles + $runtimeAssets)) {
    $sourcePath = Join-Path -Path $SourceDirectory -ChildPath $relativePath
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        Write-Warning "[Roadhog copy] Source file missing, skip all runtime copies: $sourcePath"
        exit 0
    }

    [PSCustomObject]@{
        RelativePath = $relativePath
        Path = $sourcePath
    }
}

foreach ($destinationName in $destinationNames) {
    $destinationDirectory = Join-Path -Path $DestinationRoot -ChildPath $destinationName

    try {
        if (-not (Test-Path -LiteralPath $destinationDirectory)) {
            New-Item -Path $destinationDirectory -ItemType Directory -Force | Out-Null
        }

        $lockedFiles = @()
        foreach ($fileName in $runtimeFiles) {
            $destinationPath = Join-Path -Path $destinationDirectory -ChildPath $fileName
            if (Test-FileInUse -Path $destinationPath) {
                $lockedFiles += $fileName
            }
        }

        if ($lockedFiles.Count -gt 0) {
            Write-Host "[Roadhog copy] Skip $destinationDirectory, file in use: $($lockedFiles -join ', ')"
            continue
        }

        foreach ($sourceFile in $sourceFiles) {
            $destinationPath = Join-Path -Path $destinationDirectory -ChildPath $sourceFile.RelativePath
            $destinationParent = Split-Path -Parent $destinationPath
            if (-not (Test-Path -LiteralPath $destinationParent)) {
                New-Item -Path $destinationParent -ItemType Directory -Force | Out-Null
            }

            Copy-Item -LiteralPath $sourceFile.Path -Destination $destinationPath -Force
        }

        Write-Host "[Roadhog copy] Updated $destinationDirectory"
    }
    catch {
        Write-Warning "[Roadhog copy] Skip $destinationDirectory. $($_.Exception.Message)"
    }
}
