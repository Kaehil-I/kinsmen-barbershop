[CmdletBinding()]
param(
    [string]$Database = 'kinsmen_dev',
    [string]$BaseUrl = 'http://127.0.0.1:5080'
)

$ErrorActionPreference = 'Stop'

function ConvertFrom-SecureInput([Security.SecureString]$InputValue) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($InputValue)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$secureUri = Read-Host 'Paste the Atlas connection string exactly as copied from Atlas' -AsSecureString
$atlasUri = (ConvertFrom-SecureInput $secureUri).Trim().Trim('"')
if ([string]::IsNullOrWhiteSpace($atlasUri) -or -not ($atlasUri.StartsWith('mongodb+srv://') -or $atlasUri.StartsWith('mongodb://'))) {
    throw 'Use the MongoDB connection string copied from Atlas.'
}
if ($atlasUri.Contains('<db_password>')) {
    $securePassword = Read-Host 'Enter the database-user password' -AsSecureString
    $databasePassword = ConvertFrom-SecureInput $securePassword
    try { $atlasUri = $atlasUri.Replace('<db_password>', [Uri]::EscapeDataString($databasePassword)) }
    finally { $databasePassword = $null }
}

$keyBytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($keyBytes); $developmentKey = [Convert]::ToBase64String($keyBytes) }
finally { $rng.Dispose() }

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Mongo__ConnectionString = $atlasUri
$env:Mongo__Database = $Database
$env:Auth__DevelopmentSigningKey = $developmentKey
$apiProcess = $null

try {
    dotnet build --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

    $apiProcess = Start-Process -FilePath dotnet -ArgumentList @('run', '--project', 'src/Kinsmen.Api', '--no-build', '--', '--urls', $BaseUrl) -WorkingDirectory (Get-Location) -PassThru -WindowStyle Hidden
    $ready = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            Invoke-RestMethod "$BaseUrl/health/ready" | Out-Null
            $ready = $true
            break
        }
        catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw 'The API did not become ready within 30 seconds. Check that port 5080 is free.' }

    $customerToken = (dotnet run --project src/Kinsmen.Api --no-build -- --demo-token customer-demo Customer).Trim()
    $staffToken = (dotnet run --project src/Kinsmen.Api --no-build -- --demo-token staff-a Barber).Trim()
    & ./scripts/Smoke-Test.ps1 -BaseUrl $BaseUrl -CustomerToken $customerToken -StaffToken $staffToken
    if ($LASTEXITCODE -ne 0) { throw "Booking smoke test failed with exit code $LASTEXITCODE." }
}
finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id -Force }
    Remove-Item Env:Mongo__ConnectionString -ErrorAction SilentlyContinue
    Remove-Item Env:Auth__DevelopmentSigningKey -ErrorAction SilentlyContinue
}
