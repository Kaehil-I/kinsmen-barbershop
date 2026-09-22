[CmdletBinding()]
param(
    [string]$Database = 'kinsmen_dev'
)

$ErrorActionPreference = 'Stop'

function ConvertFrom-SecureInput([Security.SecureString]$InputValue) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($InputValue)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

# The Atlas URI and password are held only for this PowerShell process. Do not put
# either value in source control, appsettings.json, screenshots, or a shared chat.
$secureUri = Read-Host 'Paste the Atlas connection string exactly as copied from Atlas' -AsSecureString
$atlasUri = ConvertFrom-SecureInput $secureUri
if ([string]::IsNullOrWhiteSpace($atlasUri) -or -not $atlasUri.StartsWith('mongodb+srv://')) {
    throw 'Use the mongodb+srv:// connection string copied from Atlas.'
}
if ($atlasUri.Contains('<db_password>')) {
    $securePassword = Read-Host 'Enter the database-user password' -AsSecureString
    $databasePassword = ConvertFrom-SecureInput $securePassword
    try {
        $atlasUri = $atlasUri.Replace('<db_password>', [Uri]::EscapeDataString($databasePassword))
    }
    finally {
        $databasePassword = $null
    }
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
