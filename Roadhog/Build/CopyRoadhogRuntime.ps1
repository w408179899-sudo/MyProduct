param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$DestinationRoot
)

$ErrorActionPreference = "Stop"

$runtimeFiles = @("Roadhog.exe", "Roadhog.dll")
$destinationNames = @("1", "2", "3")

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

$sourceFiles = foreach ($fileName in $runtimeFiles) {
    $sourcePath = Join-Path -Path $SourceDirectory -ChildPath $fileName
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        Write-Warning "[Roadhog copy] Source file missing, skip all runtime copies: $sourcePath"
        exit 0
    }

    [PSCustomObject]@{
        Name = $fileName
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
            Copy-Item -LiteralPath $sourceFile.Path -Destination $destinationDirectory -Force
        }

        Write-Host "[Roadhog copy] Updated $destinationDirectory"
    }
    catch {
        Write-Warning "[Roadhog copy] Skip $destinationDirectory. $($_.Exception.Message)"
    }
}
