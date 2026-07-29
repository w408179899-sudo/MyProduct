param(
    [string]$PrivateKeyPath = (Join-Path $env:USERPROFILE "Desktop\RoadhogOwnerLicense\owner-signing-key.dat"),
    [string]$OutputPath = (Join-Path $env:USERPROFILE "Desktop\RoadhogOwnerLicense\owner-license.json"),
    [string]$DeviceHash
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Security

$payloadPrefix = "Roadhog.OwnerLicenseGrant.v1|"
$keyEntropy = [System.Text.Encoding]::UTF8.GetBytes("Roadhog.OwnerLicenseSigningKey.v1")
$embeddedPublicKeyBlobBase64 = "RUNTMSAAAABM0sdmd3tY/wVzVw9U4/RU9s7T1hGonX0fQXJivBYMOVN4O91pl3OOszWXgPX1KPPR8Xc/Y3kTHmXJ8HHp65WJ"

function Get-RoadhogDeviceHash {
    $machineGuid = Get-ItemPropertyValue `
        -LiteralPath "HKLM:\SOFTWARE\Microsoft\Cryptography" `
        -Name "MachineGuid"
    if ([string]::IsNullOrWhiteSpace($machineGuid)) {
        throw "Windows MachineGuid is unavailable."
    }

    $material = "Roadhog.Device.v1|" + $machineGuid.Trim().ToUpperInvariant()
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $hasher.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($material))
        return ([BitConverter]::ToString($hash) -replace "-", "").ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
    }
}

function Write-BytesAtomically {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][byte[]]$Bytes
    )

    $directory = Split-Path -Parent $Path
    if ([string]::IsNullOrWhiteSpace($directory)) {
        throw "Output directory is invalid: $Path"
    }

    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporaryPath = $Path + "." + [Guid]::NewGuid().ToString("N") + ".tmp"
    try {
        [System.IO.File]::WriteAllBytes($temporaryPath, $Bytes)
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

$resolvedDeviceHash = if ([string]::IsNullOrWhiteSpace($DeviceHash)) {
    Get-RoadhogDeviceHash
}
else {
    $DeviceHash.Trim().ToLowerInvariant()
}

if ($resolvedDeviceHash -notmatch "^[0-9a-f]{64}$") {
    throw "DeviceHash must contain exactly 64 hexadecimal characters."
}

$privateKeyDirectory = Split-Path -Parent $PrivateKeyPath
if ([string]::IsNullOrWhiteSpace($privateKeyDirectory)) {
    throw "Private key directory is invalid: $PrivateKeyPath"
}

[System.IO.Directory]::CreateDirectory($privateKeyDirectory) | Out-Null

$key = $null
$ecdsa = $null
try {
    if (-not (Test-Path -LiteralPath $PrivateKeyPath)) {
        throw "Owner signing key is missing: $PrivateKeyPath. Restore the original key; a new key will not match the public key embedded in Roadhog."
    }

    $protectedKey = [System.IO.File]::ReadAllBytes($PrivateKeyPath)
    $privateKeyBlob = [System.Security.Cryptography.ProtectedData]::Unprotect(
        $protectedKey,
        $keyEntropy,
        [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    $key = [System.Security.Cryptography.CngKey]::Import(
        $privateKeyBlob,
        [System.Security.Cryptography.CngKeyBlobFormat]::EccPrivateBlob)
    $publicKeyBlob = $key.Export(
        [System.Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
    $publicKeyBlobBase64 = [Convert]::ToBase64String($publicKeyBlob)
    if (-not [string]::Equals(
        $publicKeyBlobBase64,
        $embeddedPublicKeyBlobBase64,
        [System.StringComparison]::Ordinal)) {
        throw "Owner signing key does not match the public key embedded in Roadhog."
    }

    $ecdsa = New-Object System.Security.Cryptography.ECDsaCng $key
    $payload = $payloadPrefix + $resolvedDeviceHash
    $payloadBytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $signature = $ecdsa.SignData(
        $payloadBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256)

    $document = [ordered]@{
        version = 1
        deviceHash = $resolvedDeviceHash
        signature = [Convert]::ToBase64String($signature)
    }
    $json = $document | ConvertTo-Json
    $jsonBytes = New-Object byte[] ([System.Text.Encoding]::UTF8.GetByteCount($json))
    [System.Text.Encoding]::UTF8.GetBytes($json, 0, $json.Length, $jsonBytes, 0) | Out-Null
    Write-BytesAtomically -Path $OutputPath -Bytes $jsonBytes

    Write-Host "Owner license grant created: $OutputPath"
    Write-Host "Device hash: $resolvedDeviceHash"
    Write-Host "Public key blob (embed in Roadhog):"
    Write-Host $publicKeyBlobBase64
    Write-Host "Private signing key (keep private): $PrivateKeyPath"
}
finally {
    if ($null -ne $ecdsa) {
        $ecdsa.Dispose()
    }

    if ($null -ne $key) {
        $key.Dispose()
    }
}
