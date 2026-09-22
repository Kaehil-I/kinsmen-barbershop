# Run the booking backend

This is Zario's first backend contribution for Part 2. It implements booking, availability, customer booking management, barber time blocks and staff status updates with real MongoDB persistence. It is a separate ASP.NET Core API that can be integrated with the group's eventual MVC/front-end project. The existing HTML prototype is not yet connected to it.

## Requirements

- .NET 10 SDK. The solution uses `net10.0` and `Kinsmen.slnx`; older .NET/Visual Studio installations may require an upgrade. A supported editor plus `dotnet` CLI also works.
- A MongoDB replica set or sharded cluster, because booking writes use transactions. A standalone `mongod` is insufficient.
- Docker Desktop is one option for the local database using the supplied Compose file. Alternatively, use a development replica set already available to the team. No Azure service is used.
- PowerShell 7 for the optional smoke-test script.

Commands below run from the repository root. No database credentials or signing keys belong in Git.

## Start a local database

```powershell
docker compose -f compose.mongo.yaml up -d --wait
```

The database binds only to `127.0.0.1:27017`, uses replica-set name `rs0`, and stores data in a named local Docker volume. This unauthenticated database setup is for development only. Use authenticated connections and appropriate network restrictions for a hosted database. `docker compose -f compose.mongo.yaml down` stops the service and retains its data.

If using another development replica set, set `Mongo__ConnectionString` accordingly. Use a dedicated development database, never a client's production database for seed or smoke tests.

## Build, initialize and run

For a shared Atlas development database, run the helper below from the repository root. It asks for the connection string without saving it to a file or Git:

```powershell
./scripts/Initialize-Atlas-Development.ps1
```

Copy the `mongodb+srv://` URI from Atlas without editing it and paste it only at the first prompt. At the second prompt, enter the database-user password. The script URL-encodes the password safely, creates the required validators and indexes, and adds any missing synthetic demo services and barbers. It deliberately does not retain the URI after the command ends.

To run the API after initialization, set the same `Mongo__ConnectionString` and `Mongo__Database` values in the terminal that starts the API. Use the commands below for a fully manual setup.

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Mongo__ConnectionString = 'mongodb://127.0.0.1:27017/?replicaSet=rs0'
$env:Mongo__Database = 'kinsmen_dev'
$env:Auth__DevelopmentSigningKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet run --project src/Kinsmen.Api --no-build -- --initialize --seed-demo
```

Initialization explicitly creates or updates collection validators and indexes. It does not migrate old SQL data or repair incompatible existing MongoDB documents. The seed uses insert-only defaults, so running it again does not overwrite edited services or barbers. Seed prices, names and hours are synthetic examples, not client-approved data.

Before starting the server, generate a local test token in the same terminal:

```powershell
$customerToken = dotnet run --project src/Kinsmen.Api --no-build -- --demo-token customer-demo Customer
$staffToken = dotnet run --project src/Kinsmen.Api --no-build -- --demo-token staff-a Barber
# Display only in your own local terminal so you can copy them to the API-test terminal.
$customerToken
$staffToken
dotnet run --project src/Kinsmen.Api --no-build -- --urls http://127.0.0.1:5080
```

Keep this terminal open. For API requests in another terminal, use the token generated above. Tokens expire after 30 minutes. Restarting with a different random signing key invalidates older tokens. Never put these tokens or the key in a commit or a public README.

The test-token command is a **local development tool**, not a login endpoint. It is disabled outside Development. Without an identity provider or development signing key, public reads work in Development but protected routes reject requests. Production startup requires an HTTPS `Auth__Authority`; Kyra must supply/integrate the real identity system and appropriate audience. Do not deploy with `ASPNETCORE_ENVIRONMENT=Development`.

## Quick checks

```powershell
Invoke-RestMethod http://127.0.0.1:5080/health/live
Invoke-RestMethod http://127.0.0.1:5080/health/ready
Invoke-RestMethod http://127.0.0.1:5080/api/services
```

`/health/live` checks the process. `/health/ready` checks MongoDB connectivity, transaction-capable topology and required collections; it is not a substitute for the booking smoke test or a hosted reliability test.

The supplied `scripts/Smoke-Test.ps1` performs a real customer booking, retries it safely with the same request key, confirms it as staff, reschedules and cancels it, then retrieves its details from the running API. It chooses a future available slot and uses the synthetic customer, service and barber records. Supply both tokens in the calling PowerShell session:

```powershell
./scripts/Smoke-Test.ps1 -CustomerToken $customerToken -StaffToken $staffToken
```

It leaves a cancelled booking as evidence. Run it only against your development instance. See [API-CONTRACT.md](API-CONTRACT.md) for individual requests and [openapi.json](openapi.json) for the importable contract.

## Tests

```powershell
dotnet test --no-restore
```

Without `KINSMEN_TEST_MONGO`, database integration tests are explicitly skipped. To run the full suite:

```powershell
$env:KINSMEN_TEST_MONGO = 'mongodb://127.0.0.1:27017/?replicaSet=rs0'
dotnet test --no-restore --logger 'trx;LogFileName=backend-tests.trx'
```

Real MongoDB tests create uniquely named `kinsmen_test_<guid>` databases and delete only those databases afterward. The test account therefore needs create/index/validator/read/write/drop permissions on test databases. Use a local or dedicated test cluster. Never point this setting at production. Tests freeze the business clock, so they remain valid after the dates in their test scenarios.

## Configuration and team decisions

| Setting | Current default | Action |
|---|---|---|
| `Mongo__ConnectionString` | Local replica set | Kaehil configures the selected hosted MongoDB connection |
| `Mongo__Database` | `kinsmen_dev` | Use separate development, test and production databases |
| `Booking__MinimumNoticeMinutes` | 60 | Confirm with client |
| `Booking__CancellationNoticeMinutes` | 60 | Confirm with client; applies to rescheduling too |
| `Booking__HorizonDays` | 30 | Confirm with client |
| `Booking__SlotMinutes` | 15 | Confirm start-time granularity; duration can be different |
| `Auth__Authority` | Unset | Kyra's HTTPS identity-provider endpoint for production |
| `Auth__Audience` | `kinsmen-api` | Must match the real access-token audience |
| `Auth__DevelopmentSigningKey` | Unset | Temporary local secret only |

The shop timezone is `Africa/Johannesburg`. Demo hours are Monday-Saturday, 09:00-17:00. New bookings reserve time in **Pending** status and require barber/admin confirmation before their start. No pending-booking expiry rule is invented; agree one if required. The UI should send one `Idempotency-Key` per booking attempt and retain it for retries, as described in the API contract. Never generate a new key merely because the previous response was lost.

## What is intentionally still separate work

- Customer registration/login/password recovery, identity provisioning and token issuance in production.
- Greg's UI integration and accessibility verification.
- Kaehil's service/barber administration, hosted environment and deployment pipeline.
- Email confirmations, customer reviews, retained shop/cart/payment-in-person implementation.
- Client-approved rules/data, deployment validation and final presentation.

These boundaries do not mean those requirements have been dropped. They remain group tasks in the WBS. The development seed and token tools unblock local integration; they do not complete those features.
