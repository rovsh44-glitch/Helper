param(
    [switch]$PersistForUser
)

$ErrorActionPreference = "Stop"

function New-SecureKey {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$newKey = "helper_" + (New-SecureKey)
$env:HELPER_API_KEY = $newKey

if ($PersistForUser) {
    setx HELPER_API_KEY $newKey | Out-Null
    Write-Host "[KeyRotation] HELPER_API_KEY persisted for current user."
}

Write-Host "[KeyRotation] New HELPER_API_KEY generated for current session:"
Write-Host $newKey
