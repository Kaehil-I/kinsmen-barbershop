# Kinsmen Barbershop — Website & Booking System

INSY7315 Work Integrated Learning project for **Kinsmen Barbers**, Shop 5, 8 Mackeurtan Ave, Durban North.
Built by **Kaehil, Kyra, Zario and Gregory** — BCA3, IIE Emeris.

Customers browse services and barbers, book a chair online and manage their own bookings. Barbers see
their schedule, confirm appointments and block off time. Admins manage the service catalogue and barber
profiles.

| | Link |
|---|---|
| **Live system** | *Deployment in progress (Render). The link will be added here once the hosted services are live.* |
| **Part 1 prototype** | https://kaehil-i.github.io/kinsmen-barbershop/kinsmen_prototype.html |

## How it works

```
Browser ──> Kinsmen.Web (ASP.NET Core MVC) ──server-to-server, bearer token──> Kinsmen.Api (ASP.NET Core)
                 │                                                                    │
                 └── login via Auth0 (Universal Login)                                └──> MongoDB Atlas
```

- **Kinsmen.Web** renders every page and calls the API from the server, so the browser never talks to
  the API directly and access tokens stay server-side.
- **Kinsmen.Api** owns all booking rules: availability, pricing from the catalogue, double-booking
  prevention (MongoDB transactions), ownership and role checks, and status transitions
  (Pending → Confirmed → Completed / Cancelled / No-Show).
- **Auth0** handles registration, login, password reset and email verification; the API trusts only
  tokens signed by our Auth0 tenant with audience `kinsmen-api` and a `role` of Customer, Barber or Admin.

More detail: [architecture and data model](docs/backend/ARCHITECTURE.md) ·
[API contract](docs/backend/API-CONTRACT.md) · [OpenAPI definition](docs/backend/openapi.json).

## Tech stack

| Layer | Choice |
|---|---|
| Front end | ASP.NET Core MVC (.NET 10), Razor views, plain CSS/JS (neomorphic design from the Part 1 prototype) |
| Back end | ASP.NET Core minimal API (.NET 10) |
| Database | MongoDB Atlas (`kinsmen_dev` on the `kinsmen-dev` cluster): schema validators, indexes, multi-document transactions |
| Authentication | Auth0 (free plan): OpenID Connect login in the web app, JWT bearer validation in the API |
| Hosting | Render (two Docker web services, defined in [`render.yaml`](render.yaml)) |
| CI | GitHub Actions: build, full test suite against a real MongoDB replica set, Docker image build and smoke test |

The Part 1 report proposed SQL Server, Entity Framework Core and Azure. The team replaced these with
MongoDB, Auth0 and Render: the data design is in [`docs/backend/ARCHITECTURE.md`](docs/backend/ARCHITECTURE.md),
login in [`docs/auth/AUTH0-SETUP.md`](docs/auth/AUTH0-SETUP.md) and hosting in
[`docs/deployment/RENDER.md`](docs/deployment/RENDER.md). The Word report in `Documentation/` has not yet
been updated to match.

## Running locally

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` should print 10.0.x)
- A MongoDB **replica set** (transactions do not work on a standalone server). Either:
  - Docker Desktop, using the included `compose.mongo.yaml`, or
  - the team's Atlas development cluster (ask Kaehil for a connection string; never commit it)
- For login: access to the Auth0 tenant (see [Auth0 setup](docs/auth/AUTH0-SETUP.md))

### 1. Start and initialise the database

```powershell
docker compose -f compose.mongo.yaml up -d --wait

dotnet build Kinsmen.slnx
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Mongo__ConnectionString = 'mongodb://127.0.0.1:27017/?replicaSet=rs0'
$env:Mongo__Database = 'kinsmen_dev'
dotnet run --project src/Kinsmen.Api --no-build -- --initialize --seed-demo
```

This creates the collections, validators and indexes, and adds synthetic demo services and barbers. It is
safe to run again. For Atlas, use `./scripts/Initialize-Atlas-Development.ps1` instead, which prompts for
the connection string without saving it.

### 2. Run the API

In the same terminal, point the API at Auth0 so it accepts signed-in users' tokens, then start it:

```powershell
$env:Auth__Authority = 'https://kinsmen.eu.auth0.com/'
dotnet run --project src/Kinsmen.Api --no-build
```

The API listens on `http://localhost:54427` (and `https://localhost:54426`), which is where the web app
expects it. Check it with `Invoke-RestMethod http://localhost:54427/health/ready` and
`Invoke-RestMethod http://localhost:54427/api/services`.

For API-only testing without Auth0, leave `Auth__Authority` unset and use a local signing key with
short-lived test tokens instead, as described in
[`docs/backend/GETTING-STARTED.md`](docs/backend/GETTING-STARTED.md). These tokens only work in
Development and expire after 30 minutes. (That guide runs the API on port 5080; pass `--urls` to match.)

### 3. Run the web app

The web app will not start without the Auth0 client secret. Get it from Kyra privately and store it once,
outside the repository:

```powershell
dotnet user-secrets set "Auth0:ClientSecret" "<client secret>" --project src/Kinsmen.Web
dotnet dev-certs https --trust
```

Then, in a second terminal:

```powershell
dotnet run --project src/Kinsmen.Web --launch-profile https
```

The site opens at `https://localhost:7090`. Use the HTTPS profile: browsers reject the login cookies over
plain HTTP. The Auth0 domain and client ID are in `src/Kinsmen.Web/appsettings.json`, and the API address
(`Api:BaseUrl`) is in `src/Kinsmen.Web/appsettings.Development.json`. See
[Auth0 setup](docs/auth/AUTH0-SETUP.md) for roles and for linking a barber account.

## Tests

```powershell
dotnet test Kinsmen.slnx
```

Tests that need external services are skipped unless configured:

| Tests | Enable with | Notes |
|---|---|---|
| Real MongoDB (transactions, races, rollback) | `$env:KINSMEN_TEST_MONGO = 'mongodb://127.0.0.1:27017/?replicaSet=rs0'` | Each test creates and drops its own `kinsmen_test_<guid>` database. Never point this at a shared or production database. |
| Live Auth0 round trip (web app) | `$env:KINSMEN_TEST_AUTH0 = '1'` | Needs internet access to the Auth0 tenant; no secret required. |

Every pull request runs the whole suite in CI, including the MongoDB tests against a throwaway replica
set. The live Auth0 tests are skipped there.

## Deployment

The system deploys to Render as two Docker web services (`kinsmen-api`, `kinsmen-web`) from
[`render.yaml`](render.yaml). Staging deploys from `integration-part2`, and only after a commit's GitHub
checks pass; the submission build deploys from `main`.

Secrets (the MongoDB connection string and the Auth0 client secret) are entered in the Render dashboard
and never committed. Setup steps, Atlas network access and free-tier behaviour are in
[`docs/deployment/RENDER.md`](docs/deployment/RENDER.md).

## Working on the code

1. Branch from `integration-part2` (`feature/<name>-<topic>`).
2. Open a pull request back into `integration-part2`. The **Build and test** and **Container images**
   checks must pass, and a teammate reviews before merging.
3. `integration-part2` is merged into `main` for submission.

Never commit connection strings, passwords, client secrets or tokens. Use `dotnet user-secrets` or
environment variables locally.

## Repository structure

```
kinsmen-barbershop/
├── src/
│   ├── Kinsmen.Api/            # booking API: endpoints, domain rules, MongoDB store, Dockerfile
│   └── Kinsmen.Web/            # MVC front end: controllers, Razor views, API client, Dockerfile
├── tests/
│   ├── Kinsmen.Api.Tests/      # rule, HTTP/auth and real-MongoDB tests
│   └── Kinsmen.Web.Tests/      # login redirect safety and authorization tests
├── docs/
│   ├── backend/                # API contract, OpenAPI, architecture, schemas, validation record
│   ├── auth/AUTH0-SETUP.md     # Auth0 tenant, roles and web app integration
│   └── deployment/RENDER.md    # hosting setup
├── scripts/                    # Atlas initialisation and booking smoke tests
├── .github/workflows/          # CI
├── render.yaml                 # Render Blueprint
├── compose.mongo.yaml          # local MongoDB replica set
├── Kinsmen.slnx                # .NET solution
├── kinsmen_prototype.html      # Part 1 prototype (single file)
└── Documentation/              # Task 1 report, WBS/ERD/DevOps, questionnaire, meeting minutes
```

## Team

| Member | Area |
|---|---|
| Kaehil | Group leader; GitHub workflow, CI, hosting and deployment; admin catalogue |
| Zario | Booking API, MongoDB data design and booking rules |
| Gregory | Front end and user experience |
| Kyra | Authentication, security and testing |

## Known gaps

Tracked so the documentation, prototype and system can be aligned before submission:

- **Loyalty:** shown in the prototype, but listed as out of scope in the Task 1 report. Not built.
- **Guest booking:** the report describes a Guest role; the system currently requires an account to book.
- **Shop and checkout:** the report's cart/checkout flow and the prototype's Barber POS are not built.
- **Reviews and confirmation emails:** planned (Kyra); not yet built.
- **Admin screens:** the admin catalogue API is in review; the web screens follow once login is merged.
- **Barber profile pages:** the report describes one page per barber; the site has a single Team page.
- **Client rules:** prices, durations, opening hours, notice periods, booking horizon and pending-booking
  expiry still use demo values and need client confirmation.
