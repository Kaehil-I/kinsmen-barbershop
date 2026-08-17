# Kinsmen Barbershop — Website & Booking System

INSY7315 Work Integrated Learning project for Kinsmen Barbers, a barbershop in Durban North.
Built by **Kaehil, Kyra, Zario, and Gregory** — BCA3, IIE Emeris.

## What this is

A front-end prototype of a booking system for Kinsmen Barbers, covering the customer booking flow,
a shop, barber/team profiles, and role-based previews for Barber and Admin staff views. It's a single
self-contained HTML file — no build step, no server, no install.

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
├── Documentation/
│   ├── WIL Task 1 Documentation.docx        # main Task 1 submission document
│   ├── Kinsmen-WBS-ERD-DevOps.docx          # WBS, ERD, and DevOps sections
│   ├── Kinsmen Barbershop Requirements Questionnaire.docx/.pdf
│   └── Meeting Minutes/                     # dated team + client meeting records
└── Kinsmen Barbershop — Website & Booking System_ Client Requirements Questionnaire.csv.zip
```

## Tech stack (planned implementation)

- **Frontend:** ASP.NET Core MVC, plain IDE-built Razor views
- **Backend:** ASP.NET Core, Entity Framework Core
- **Database:** Microsoft SQL Server (Azure SQL Database in production)
- **Hosting:** Azure App Service
- **CI/CD:** GitHub Actions (build → test → deploy on merge to `main`)

Full justification for each of these is in `Documentation/WIL Task 1 Documentation.docx`, Sections 6–9.

## Design patterns

Repository, Unit of Work, Factory, Strategy, and Observer — see Section 7.1 of the main documentation
for the reasoning behind each.

## Known gaps between this prototype and the current documentation

This prototype is ahead of the written documentation in some areas and behind it in others — worth
reading both before Task 1 submission rather than assuming they already match:

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
- **Customer self-service booking management:** the user stories (Section 2.2) call for customers to
  view, reschedule, and cancel their own bookings, and to leave reviews. Not yet in the prototype.
- **Barber time-blocking:** user stories call for barbers to block off their own time (breaks, days
  off). Not yet in the prototype.
- **Admin scope:** the prototype's Admin role manages retail Shop products and staff/customer
  accounts. The documentation additionally expects Admin to manage the barbershop *services* (haircut/
  beard trim pricing) and barber profiles — neither is built yet.
- **Individual barber profile pages:** the documented journey map describes a dedicated Barber Profile
  screen per barber; the prototype has a single Team page listing all barbers instead.
- **Booking status vocabulary:** aligned in the prototype to match the documented state diagram
  (Pending → Confirmed → Completed / Cancelled / No-Show) as of this update.
- **Multi-service selection:** aligned in the prototype as of this update — a booking can now include
  more than one service, matching the Factory pattern write-up and the team's 30 July / 4 August
  meeting decisions.

## Client

Kinsmen Barbers, Shop 5, 8 Mackeurtan Ave, Durban North.
