# Booking API contract

Version 0.1, 20 September 2026. The executable source is `src/Kinsmen.Api`; [openapi.json](openapi.json) is the matching importable API definition. Local base URL: `http://127.0.0.1:5080`. Use HTTPS in hosting.

## Shared conventions

- Request and response JSON uses camelCase. Status values are `pending`, `confirmed`, `cancelled`, `completed`, `noShow`.
- JSON request timestamps must include an explicit timezone, e.g. `2026-09-23T10:00:00+02:00` or `2026-09-23T08:00:00Z`. Responses store/return UTC. Availability `date` is the shop's local calendar date.
- Amounts are integer **ZAR cents**, so `30000` means R300.00. Clients do not provide calculated price or duration.
- IDs are opaque strings. Clients must not rely on GUID formatting or use names as identity keys.
- Unknown request JSON fields are rejected. Errors from domain validation use `application/problem+json` with `status`, `title` and `detail`.
- Protected routes require `Authorization: Bearer <access-token>`. Valid tokens must have `sub` (stable account ID) and `role` exactly `Customer`, `Barber` or `Admin`. These claims must come from the trusted authentication system, never a role selector in the browser.
- Updates carry the latest `version`. A stale version returns `409`; refresh before applying a new change. UI availability is advisory; the create transaction is authoritative.
- Results from collection endpoints are arrays. Booking list requests require a positive interval no greater than 31 days and select records overlapping that interval. Sort/display as appropriate in the UI. Pagination is not implemented in this first slice.

## Endpoints

| Method and route | Access | Input | Success |
|---|---|---|---|
| `GET /api/services` | Public | None | `200` active services |
| `GET /api/barbers` | Public | None | `200` active barber IDs, names and working periods; staff account IDs omitted |
| `GET /api/availability` | Public | `date`, repeated `serviceIds`, optional `barberId` | `200` available barber/start/end combinations |
| `POST /api/bookings` | Customer/Admin | `barberId` or null, `serviceIds`, `start`; admin additionally supplies `customerId` | `201` persisted Pending booking |
| `GET /api/bookings` | Authenticated | `from`, `to`, optional `barberId` | `200`; customer sees own bookings, barber sees own schedule, admin sees matching records |
| `GET /api/bookings/{id}` | Owning Customer/assigned active Barber/Admin | Booking ID | `200` one booking including its current status/version |
| `PATCH /api/bookings/{id}/reschedule` | Owning Customer/Admin | `start`, `version` | `200` updated booking; same barber and service/price snapshots |
| `POST /api/bookings/{id}/cancel` | Owning Customer/Admin | `version` | `200` cancelled booking; slot released |
| `PATCH /api/bookings/{id}/status` | Assigned active Barber/Admin | `status`, `version` | `200` updated booking |
| `GET /api/barbers/{barberId}/blocks` | Same active Barber/Admin | `from`, `to` | `200` private time-block records |
| `POST /api/barbers/{barberId}/blocks` | Same active Barber/Admin | `start`, `end`, `reason` | `201` time block |
| `DELETE /api/blocks/{id}` | Same active Barber/Admin | None | `204` |
| `GET /api/admin/services` | Admin | None | `200` all services, including inactive |
| `POST /api/admin/services` | Admin | `name`, `priceCents`, `durationMinutes` | `201` new active service |
| `PUT /api/admin/services/{id}` | Admin | `name`, `priceCents`, `durationMinutes` | `200` updated service |
| `PATCH /api/admin/services/{id}/active` | Admin | `active` | `200` updated service |
| `GET /api/admin/barbers` | Admin | None | `200` all barber profiles, including linked `userId` and `active` |
| `POST /api/admin/barbers` | Admin | `name`, `userId`, `hours` | `201` new active barber profile |
| `PUT /api/admin/barbers/{id}` | Admin | `name`, `userId`, `hours` | `200` updated barber profile |
| `PATCH /api/admin/barbers/{id}/active` | Admin | `active` | `200` updated barber profile |
| `GET /health/live` | Public | None | `200` process alive |
| `GET /health/ready` | Public | None | `200` database checks pass; otherwise `503` |

The frontend uses repeated array parameters, not a comma-separated string:

```text
/api/availability?date=2026-09-23&serviceIds=haircut&serviceIds=beard&barberId=barber-a
```

## Create a booking

```json
{
  "barberId": "barber-a",
  "serviceIds": ["haircut", "beard"],
  "start": "2026-09-23T10:00:00+02:00"
}
```

Use `"barberId": null` for any available barber. Selection is deterministic by barber ID at the requested time. The availability endpoint returns all available choices; the frontend can take the earliest returned time. A customer identity comes from the token. An admin can create on behalf of `customerId`; account existence must be integrated with Kyra's account store before this becomes a production workflow. An optional `notes` field accepts up to 500 characters for the barber. Display notes as text, never as raw HTML. Notes are returned only with the authenticated booking record, not public availability.

Example response with the synthetic catalogue (IDs/times illustrated):

```json
{
  "id": "example-booking-id",
  "customerId": "customer-demo",
  "barberId": "barber-a",
  "startUtc": "2026-09-23T08:00:00Z",
  "endUtc": "2026-09-23T08:45:00Z",
  "services": [
    {"serviceId":"haircut","name":"Demo haircut","priceCents":20000,"durationMinutes":30},
    {"serviceId":"beard","name":"Demo beard trim","priceCents":10000,"durationMinutes":15}
  ],
  "totalCents": 30000,
  "status": "pending",
  "createdUtc": "2026-09-20T10:00:00Z",
  "version": 1,
  "notes": null
}
```

Only 1-10 distinct active services are accepted, up to a total of 480 minutes. New appointments must fit one working period without crossing a break, booking or time block. Adjacent appointments are allowed: an appointment ending at 10:00 does not block one starting at 10:00.

### Safe retries

Send an `Idempotency-Key` header on each new booking attempt, for example a random UUID. Generate it once when the customer submits that attempt and reuse it with the same payload if the network fails or the user double-clicks. The header is optional for compatibility, but Greg should always supply it. Allowed values contain 16-128 ASCII letters, digits, hyphens or underscores.

```text
Idempotency-Key: 2b4784cb-404d-4971-bc8a-514b686a3583
```

The key is scoped to the authenticated account. Repeating the same request returns the same booking ID with `201`; it cannot reserve a second barber under any-barber selection. Reusing the key for a different customer/barber/start/services/notes request returns `409 idempotency_conflict`. Service order, whitespace at the edges of notes and equivalent timestamp offsets are normalized when comparing requests. Replays return the booking's current state, so a previously cancelled booking remains cancelled and is not recreated. A genuinely new appointment needs a new key.

The database stores a request fingerprint and a deterministic booking ID, not the raw key. The fingerprint is internal and is excluded from API responses. Key retention lasts as long as the booking is retained; application code does not delete booking history.

## Manage a booking

Staff confirmation:

```json
{"status":"confirmed","version":1}
```

Reschedule using the latest version (2 after confirmation):

```json
{"start":"2026-09-23T11:00:00+02:00","version":2}
```

Cancel using the latest version (3 after that move):

```json
{"version":3}
```

Both Pending and Confirmed bookings can be rescheduled or cancelled before the configured notice deadline. A failed reschedule leaves the original appointment intact. It preserves the original service names, prices and durations. Changing barber or selected services during rescheduling is not part of this API version.

The assigned barber/admin can move Pending to Confirmed before the appointment start. Confirmed can become Completed at/after its end, or NoShow at/after its start. Final statuses cannot be reopened through this API. Staff cancellation is an Admin operation; barbers have confirmation/completion/no-show privileges. Pending bookings reserve slots until cancelled or processed; there is no automatic expiry yet.

## Time blocks

```json
{"start":"2026-09-23T12:00:00+02:00","end":"2026-09-23T13:00:00+02:00","reason":"Lunch break"}
```

Blocks must have positive duration, start in the future, last no longer than 31 days and end within the configured horizon plus one day. A block cannot overwrite an existing appointment or overlap another block. The reason is visible only to the relevant barber/admin. Public availability does not reveal appointment identities or break reasons.

## Admin catalogue

Admins manage services and barber profiles. Nothing is deleted: deactivating hides a record from the public catalogue and from new bookings, while existing bookings keep valid references. Customers never see inactive records; the admin list endpoints return everything.

```json
{"name":"Skin fade","priceCents":25000,"durationMinutes":45}
```

- Service names are 1–80 characters (trimmed), unique among **active** services (case-insensitive), priced R0–R10 000 and 1–480 minutes long. Editing a service does not change existing bookings: they keep the name, price and duration captured when booked.
- Reactivating a service fails with `409 duplicate_name` if another active service now uses its name.

```json
{"name":"Sipho","userId":"auth0|65f0c1a2b3","hours":[{"weekday":1,"startMinute":540,"endMinute":1020},{"weekday":6,"startMinute":540,"endMinute":780}]}
```

- `userId` is the staff member's identity-provider subject (`sub`, e.g. from Auth0) and links their login to this barber. Each account can be linked to one barber only (`409 duplicate_user`, also enforced by a unique database index).
- `hours` uses ISO weekdays (Monday=1 … Sunday=7) and minutes since local midnight, at most 21 periods. Split shifts are allowed; periods on the same day must not overlap. An empty array means the barber currently has no working days.
- Changing hours is refused with `409 booking_conflict` when an upcoming Pending/Confirmed booking would fall outside the new hours. Deactivating a barber is refused while they have upcoming Pending/Confirmed bookings. Move or cancel those bookings first.
- Barber edits and deactivation lock the barber's schedule in the same way as booking writes, so a booking made at the same moment either lands before the change (and the change is checked against it) or sees the new profile.
- Catalogue edits are last-write-wins; there is no version field on services or barbers.

## Error handling for Greg

| Status | Meaning | UI response |
|---|---|---|
| `400` | Invalid services, timestamp, range, slot alignment or body | Show validation feedback; keep entered values |
| `401` | Missing/invalid/expired identity | Ask user to log in again |
| `403` | Authenticated role has no permission | Hide unavailable action and display denial |
| `404` | Missing record or customer attempting another customer's update | Show unavailable record; do not disclose ownership |
| `409` `booking_conflict` | Time/status/notice conflict | Refresh availability or booking; explain conflict |
| `409` `stale_version` | Another update already changed this booking | Refresh record/version before retry |
| `409` `idempotency_conflict` | A request key was reused with a different payload | Keep the key for exact retries; generate a new key for a genuinely new attempt |
| `409` `duplicate_name` | Another active service already uses this name | Ask the admin for a different name |
| `409` `duplicate_user` | The account is already linked to another barber | Show which account is taken; pick another |
| `429` | Request limit reached | Pause and retry later |
| `503` | Database unavailable/not initialized/not transaction-capable | Show service unavailable; refresh bookings before retrying a write |
| `500` | Unexpected server error | Show generic failure; retain returned trace ID for diagnosis |

Authentication middleware and rate limiting can return empty error bodies. Do not assume every non-success response has JSON. The API currently allows 120 requests/minute per direct source IP per process. Kaehil must configure trusted proxy forwarding and hosting-level limits for production; do not blindly trust forwarded headers. CORS is not opened to arbitrary origins. Choose same-origin hosting or add exact approved frontend origins during integration.

For uncertain create results, retry with the original key and payload or query the customer's bookings. Do not generate a new key for a network retry. Requests without an idempotency key retain the legacy behavior and must not be automatically retried.
