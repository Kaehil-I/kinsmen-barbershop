# Zario backend handoff

Prepared 20 September 2026. This is the first implemented backend slice for the group's Part 2 project. Source branch: `feature/zario-booking-backend`, based on `main` commit `8007150ba10ed0b640ff64db78774aee24551021`. It has not been pushed or deployed.

## What is available

- ASP.NET Core/.NET 10 booking API with MongoDB persistence and transaction-backed scheduling.
- Public service/barber catalogue and availability endpoints.
- Customer booking creation, viewing, rescheduling and cancellation.
- Barber schedule access, private time blocks and status transitions.
- Server-calculated totals/durations, price snapshots, ownership checks and stale-update protection.
- Database-backed idempotency keys for safe create retries and an authorized booking-details endpoint.
- MongoDB validators/index initialization and repeatable synthetic seed data.
- Rule, HTTP/authentication and real MongoDB tests.
- Runnable local setup, smoke-test script, OpenAPI contract, schemas and architecture/state/sequence diagrams.

Start with [GETTING-STARTED.md](GETTING-STARTED.md). Use [API-CONTRACT.md](API-CONTRACT.md) to connect screens and [ARCHITECTURE.md](ARCHITECTURE.md) for the technical presentation and Part 1 documentation revisions.

## Next actions by person

| Person | Next action | Integration detail |
|---|---|---|
| Zario | Walk through booking rules and review the implementation with Kaehil | Explain the shared barber revision write, transaction retry and version checks; confirm policy defaults with the client |
| Greg | Replace prototype booking arrays and role-switcher behavior with API calls | Fetch services/barbers/availability; submit service IDs, timestamp and a stable Idempotency-Key; display 409 conflicts; retain latest booking version; format UTC in shop time |
| Kyra | Connect the real authentication flow and extend security tests | Supply signed access tokens with trusted `sub` and `role`; map staff subject to barber.userId; integrate customer account validation; replace development tools in the real user journey |
| Kaehil | Review scaffolding and schema, choose host, add CI/deployment | Ensure MongoDB supports transactions; provision secrets and initial validators/indexes; run the full test suite before release; preserve repository contracts in admin scheduling work |

No messages have been sent to the group and no GitHub branch or pull request has been published. This package is reviewable local work, not a claim that Part 2 is complete.

## WBS progress

| WBS item | Status |
|---|---|
| 1.3 Collection model and API contract | Implemented and documented; group/client review pending |
| 3.1 Availability and multi-service booking | Implemented; local rule/API/database validation completed; frontend/hosting integration pending |
| 3.2 Booking changes and staff scheduling | Implemented for same-barber rescheduling, cancellation, time blocks and documented status flow; group review pending |
| 6.1 Technical documentation updates | Backend replacement diagrams, schemas and contracts prepared; existing Word report still needs merging and other owners' sections |

Small pieces of application scaffolding, persistence and authentication validation were necessary to make this slice runnable. They provide an integration starting point; they do not supersede Kaehil's infrastructure ownership or Kyra's complete authentication task.

## Decisions to confirm

1. Retain .NET 10 or agree another supported target before the group installs tools. Current build and tests use the installed .NET 10 SDK.
2. Confirm service prices, durations, opening periods, notice windows and the booking horizon. Seed values are demonstration data only.
3. Confirm that pending bookings need explicit staff approval and how unconfirmed bookings expire or are handled. The state flow now matches the Part 1 Pending -> Confirmed -> final-status plan.
4. Confirm whether customers should change barber/services while rescheduling; current API moves the time only.
5. Confirm hosting and production authentication architecture together; a database decision alone does not settle either.
6. Resolve the existing shop/guest/loyalty scope discrepancies with the group/client. They are not removed by this backend contribution.

## Suggested technical demonstration

1. Show catalogue and availability loaded from MongoDB.
2. Create a multi-service Pending booking; explain totals and embedded historical snapshots.
3. Confirm as the assigned barber, then reschedule as the customer and show the incrementing version.
4. Show a conflict rejection and the original booking remaining unchanged.
5. Show another customer denied access and a barber unable to block another barber's time.
6. Run the full tests with real MongoDB enabled, then show the relevant transaction and test code.
7. Clearly separate local evidence from future hosted/frontend integration evidence.

The test record in [VALIDATION.md](VALIDATION.md) states exactly what has been checked and what remains unverified. Read the code and run the demo before presenting it as your contribution.
