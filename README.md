# Kinsmen Barbershop — Website & Booking System

INSY7315 Work Integrated Learning project for Kinsmen Barbers, a barbershop in Durban North.
Built by **Kaehil, Kyra, Zario, and Gregory** — BCA3, IIE Emeris.

## What this is

The repository contains the Part 1 HTML prototype and an initial Part 2 booking backend.
The prototype is a self-contained HTML file covering customer booking, shop and staff previews.
The backend is an ASP.NET Core/.NET 10 API with MongoDB persistence, booking rules and automated tests.
The prototype and backend are not connected yet; production authentication and hosting are still group integration work.

## Start the Part 2 backend

- [Local setup and commands](docs/backend/GETTING-STARTED.md)
- [API contract and integration examples](docs/backend/API-CONTRACT.md)
- [Importable OpenAPI definition](docs/backend/openapi.json)
- [Architecture, data model and booking state diagrams](docs/backend/ARCHITECTURE.md)
- [Team handoff and remaining responsibilities](docs/backend/TEAM-HANDOFF.md)
- [Validation record](docs/backend/VALIDATION.md)

The backend requires the .NET 10 SDK and a MongoDB replica set or sharded cluster.
Build with `dotnet build Kinsmen.slnx` and run tests with `dotnet test Kinsmen.slnx`.
Set `KINSMEN_TEST_MONGO` to a dedicated test replica set to include real database integration tests;
otherwise those tests are explicitly skipped. Read the setup guide before running the API.

## Viewing the prototype

Open `kinsmen_prototype.html` directly in a browser, or visit the hosted version:

**Live link:** https://kaehil-i.github.io/kinsmen-barbershop/kinsmen_prototype.html

No login is required for any part of the prototype. Use the role switcher pinned to the bottom of the
screen to preview the Customer, Barber, and Admin experiences.

### Suggested walkthrough order

1. **Customer** (default) — Home → Book a Chair → Services → Team → Gallery → About → Loyalty → Location → Log In / Create Account
2. **Barber** — My Bookings → click a booking to open the checkout (POS)
3. **Admin** — Bookings → Products → Accounts → Analytics

This same guide also appears as a dismissible panel at the top of the prototype itself.

## Repository structure

```
kinsmen-barbershop/
├── kinsmen_prototype.html   # the prototype — single file, open directly in a browser
├── Kinsmen.slnx             # .NET 10 backend and tests
├── src/Kinsmen.Api/         # HTTP endpoints, booking rules and MongoDB persistence
├── tests/Kinsmen.Api.Tests/ # domain, HTTP/auth and real MongoDB tests
├── docs/backend/           # contracts, JSON schemas, diagrams and handoff
├── scripts/Smoke-Test.ps1  # running-API booking lifecycle check
├── compose.mongo.yaml      # optional local development replica set
├── Documentation/
│   ├── WIL Task 1 Documentation.docx        # main Task 1 submission document
│   ├── Kinsmen-WBS-ERD-DevOps.docx          # WBS, ERD, and DevOps sections
│   ├── Kinsmen Barbershop Requirements Questionnaire.docx/.pdf
│   └── Meeting Minutes/                     # dated team + client meeting records
└── Kinsmen Barbershop — Website & Booking System_ Client Requirements Questionnaire.csv.zip
```

## Tech stack and migration status

- **Frontend:** existing HTML/CSS/JavaScript prototype; final MVC/views integration pending.
- **Backend:** ASP.NET Core/.NET 10 HTTP API with separated booking rules and repository interfaces.
- **Database:** MongoDB with the official C# driver, schema validation, indexes and transactions.
- **Hosting:** undecided between Render/Vercel; no Azure dependency in the backend.
- **Authentication:** JWT validation boundary; production account/login integration pending. Local demo tokens are development-only.
- **CI/CD:** tests are runnable locally; GitHub automation and hosted deployment remain Kaehil's integration work.

The original Word report still describes SQL Server/Azure. Use `docs/backend/ARCHITECTURE.md` as
the booking-backend replacement material when updating its Sections 6–9; the Word report has not
yet been edited. MongoDB replaces the database layer, not the application host.

## Design patterns

The backend currently uses dependency injection, repository abstraction and a transaction/unit-of-work boundary.
Factory, Strategy and Observer were proposed in Part 1; this contribution does not claim to implement them.

## Known gaps between this prototype and the current documentation

The prototype, backend and written scope still need to be aligned for Task 2:

- **Loyalty feature:** the prototype includes a working Loyalty screen, but Section 11.4 of the main
  documentation currently lists loyalty as an explicitly *excluded* future enhancement. One of these
  needs to change before submission — either update the documentation to bring loyalty into scope, or
  remove/relabel the prototype screen as a concept preview.
- **Guest vs. Customer role:** the documentation defines Guest and Customer as two distinct roles;
  the prototype's role switcher only offers Customer/Barber/Admin (guest checkout exists within the
  booking flow itself, but isn't a separate top-level role).
- **Shop cart & checkout:** the domain model and ERD (Sections 4 & 6) describe a customer-facing
  Cart/Checkout flow. The prototype's Shop is browse-only for customers — the only checkout that
  exists is staff-side, in the Barber POS.
- **Customer self-service booking management:** the prototype contains customer booking-management
  rendering code. The new backend implements viewing, rescheduling and cancellation; UI integration
  remains pending. Customer reviews are separate work.
- **Barber time-blocking:** implemented in the new backend; staff UI integration remains pending.
- **Admin scope:** the prototype's Admin role manages retail Shop products and staff/customer
  accounts. The documentation additionally expects Admin to manage the barbershop *services* (haircut/
  beard trim pricing) and barber profiles. Service-editing prototype code exists, but persistent admin
  catalogue/profile management is not included in this backend slice.
- **Individual barber profile pages:** the documented journey map describes a dedicated Barber Profile
  screen per barber; the prototype has a single Team page listing all barbers instead.
- **Booking status vocabulary:** aligned in the prototype to match the documented state diagram
  (Pending → Confirmed → Completed / Cancelled / No-Show) as of this update.
- **Multi-service selection:** aligned in the prototype as of this update — a booking can now include
  more than one service, matching the Factory pattern write-up and the team's 30 July / 4 August
  meeting decisions.

## Client

Kinsmen Barbers, Shop 5, 8 Mackeurtan Ave, Durban North.
