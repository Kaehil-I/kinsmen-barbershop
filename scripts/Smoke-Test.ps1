param(
    [string]$BaseUrl = 'http://127.0.0.1:5080',
    [Parameter(Mandatory)][string]$CustomerToken,
    [Parameter(Mandatory)][string]$StaffToken
)
$ErrorActionPreference = 'Stop'
$customerHeaders = @{ Authorization = "Bearer $($CustomerToken.Trim())" }
$createHeaders = @{ Authorization = $customerHeaders.Authorization; 'Idempotency-Key' = [Guid]::NewGuid().ToString('N') }
$staffHeaders = @{ Authorization = "Bearer $($StaffToken.Trim())" }
Invoke-RestMethod "$BaseUrl/health/ready" | Out-Null
$shopZone = [TimeZoneInfo]::FindSystemTimeZoneById('Africa/Johannesburg')
$localToday = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $shopZone).Date
$slots = @()
for ($offset = 1; $offset -le 7; $offset++) {
    $date = $localToday.AddDays($offset).ToString('yyyy-MM-dd')
    $slots = Invoke-RestMethod "$BaseUrl/api/availability?date=$date&serviceIds=haircut&serviceIds=beard&barberId=barber-a"
    if (@($slots).Count -ge 2) { break }
}
if (@($slots).Count -lt 2) { throw 'Need two available demo barber slots within the next seven days.' }
$bookingBody = @{ barberId='barber-a'; serviceIds=@('haircut','beard'); start=$slots[0].startUtc } | ConvertTo-Json
$booking = Invoke-RestMethod "$BaseUrl/api/bookings" -Method Post -Headers $createHeaders -ContentType 'application/json' -Body $bookingBody
$replay = Invoke-RestMethod "$BaseUrl/api/bookings" -Method Post -Headers $createHeaders -ContentType 'application/json' -Body $bookingBody
if ($replay.id -ne $booking.id) { throw 'A repeated request created a different booking.' }
$confirmed = Invoke-RestMethod "$BaseUrl/api/bookings/$($booking.id)/status" -Method Patch -Headers $staffHeaders -ContentType 'application/json' -Body (@{status='confirmed';version=$booking.version}|ConvertTo-Json)
$moved = Invoke-RestMethod "$BaseUrl/api/bookings/$($booking.id)/reschedule" -Method Patch -Headers $customerHeaders -ContentType 'application/json' -Body (@{start=$slots[1].startUtc;version=$confirmed.version}|ConvertTo-Json)
$cancelled = Invoke-RestMethod "$BaseUrl/api/bookings/$($booking.id)/cancel" -Method Post -Headers $customerHeaders -ContentType 'application/json' -Body (@{version=$moved.version}|ConvertTo-Json)
if ($cancelled.status -ne 'cancelled' -or $cancelled.version -ne 4 -or $cancelled.totalCents -ne 30000) {
    throw 'Unexpected booking lifecycle result. Check synthetic seed data and API responses.'
}
$details = Invoke-RestMethod "$BaseUrl/api/bookings/$($booking.id)" -Headers $customerHeaders
if ($details.status -ne 'cancelled' -or $details.version -ne 4) { throw 'Booking details do not reflect the final update.' }
Write-Output "PASS: booking $($cancelled.id) created, safely retried, confirmed, rescheduled, cancelled and retrieved through the running API."
