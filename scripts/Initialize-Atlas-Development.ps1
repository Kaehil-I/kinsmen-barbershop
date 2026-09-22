[CmdletBinding()]
param(
    [string]$Database = 'kinsmen_dev'
)

$ErrorActionPreference = 'Stop'

# The Atlas URI is held only for this PowerShell process. Do not put it in source
# control, appsettings.json, screenshots, or a shared chat.
$secureUri = Read-Host 'Paste the Atlas connection string (with the database-user password inserted)' -AsSecureString
$uriPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureUri)
try {
    $atlasUri = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($uriPointer)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($uriPointer)
}

if ([string]::IsNullOrWhiteSpace($atlasUri) -or -not $atlasUri.StartsWith('mongodb+srv://')) {
    throw 'Use the mongodb+srv:// connection string copied from Atlas.'
}
if ($atlasUri.Contains('<db_password>')) {
    throw 'Replace <db_password> with the database-user password before continuing.'
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Mongo__ConnectionString = $atlasUri
$env:Mongo__Database = $Database
$keyBytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $rng.GetBytes($keyBytes)
    $env:Auth__DevelopmentSigningKey = [Convert]::ToBase64String($keyBytes)
}
finally {
    $rng.Dispose()
}

try {
    dotnet run --project src/Kinsmen.Api -- --initialize --seed-demo
    if ($LASTEXITCODE -ne 0) { throw "Initialization failed with exit code $LASTEXITCODE." }
    Write-Output "PASS: Atlas database '$Database' was initialized and demo services/barbers were seeded."
}
finally {
    Remove-Item Env:Mongo__ConnectionString -ErrorAction SilentlyContinue
    Remove-Item Env:Auth__DevelopmentSigningKey -ErrorAction SilentlyContinue
}
