# Backend validation record

Initially validated locally on 20 September 2026. The booking backend is now integrated with the MVC application, Auth0, MongoDB Atlas and the Render deployment. This record preserves the reproducible backend result; live deployment verification is tracked separately in `docs/SUBMISSION.md` and must be completed on the final submission commit.

## Environment

- Windows, .NET SDK 10.0.401 and .NET runtime 10.0.12.
- MongoDB C# driver 3.12.0; package lock files included.
- Real MongoDB 8.0.15, single-node local replica set `rs0`, loopback port 27119.
- Release build; synthetic customers, services and barbers only.

## Results

| Check | Result |
|---|---|
| Release compilation | Passed with zero warnings/errors |
| Full automated suite with real MongoDB enabled | **60 passed, 0 failed, 0 skipped** |
| Booking-rule tests | 36 passed |
| HTTP/authentication tests | 15 passed |
| Real MongoDB tests | 9 passed |
| Database initialize and synthetic seed CLI | Passed against local replica set |
| Development token CLI | Passed for Customer and Barber identities |
| Running HTTP API lifecycle | Created Pending booking, safely retried it, staff-confirmed it, customer-rescheduled it, cancelled it and retrieved its details; final version 4 |
| Process restart | Cancelled booking and version recovered after API restart |
| Contract export | OpenAPI JSON references checked; four MongoDB schema exports compared to executable definitions |

## What the tests demonstrate

- Server-calculated service totals and durations; unknown, duplicate or inactive services rejected.
- Notice window, booking horizon, slot alignment, closed-day and closing-time enforcement.
- Adjacent appointments allowed; overlapping appointments rejected.
- Any-available-barber selection and customer ownership restrictions.
- Failed rescheduling preserves the original appointment; successful rescheduling preserves historical pricing/duration.
- Cancellation releases availability; stale versions cannot overwrite later changes.
- Time blocks affect availability and cannot overwrite appointments; one barber cannot manage another's blocks.
- Pending/Confirmed/final-state transitions enforce actor and appointment-time rules.
- Disabled barbers cannot manage their schedule; extreme dates return validation errors.
- Customer notes are available to the assigned barber and have a bounded length.
- Missing, expired or wrong-audience tokens rejected; missing subject and unknown role rejected.
- Malformed bodies, client price injection and offset-free timestamps rejected.
- Separate MongoDB store instances racing for one slot produce exactly one success.
- A booking racing a time block cannot result in both being accepted.
- Rollback, persisted reads from a new connection, repeatable initialization/seeding and database-side negative-price validation.
- Ten concurrent retries with one idempotency key produce one booking, including any-barber selection.
- The same key used for different barbers cannot create two bookings; mismatched payloads return conflicts.
- Fresh connections can replay committed requests, cancelled bookings are not recreated, and the internal fingerprint is absent from HTTP responses.

The full test command was:

```powershell
$env:KINSMEN_TEST_MONGO = 'mongodb://127.0.0.1:27119/?replicaSet=rs0'
dotnet test Kinsmen.slnx --configuration Release --no-restore --logger 'trx;LogFileName=backend-tests-final.trx'
```

Use your own test replica-set connection when reproducing. A run without `KINSMEN_TEST_MONGO` skips nine database tests and is not equivalent to the result above. The tests create/drop only their uniquely named test databases.

## Remaining verification

- The provided Docker Compose file has not been executed on this machine because Docker is not installed; the actual database checks used a portable local MongoDB server with the same server version and replica-set topology.
- OpenAPI references and schema copies were structurally checked; import into the group's chosen API client remains an integration step.
- GitHub Actions now builds the solution, runs the test suite against a throwaway MongoDB replica set, builds both Docker images and smoke-tests them on every pull request to `integration-part2` and `main`. Record the successful final-run link in `docs/SUBMISSION.md` before submission.
- The release architecture uses Render for the MVC app and API and MongoDB Atlas for persistence. Re-check `/health/ready`, a real booking, database persistence and the email/review flow after the final deployment; free-tier services can need time to wake after inactivity.
- The MVC/Auth0 integration and accessibility improvements are implemented. The group must retain final browser evidence for customer, barber and admin paths, including keyboard navigation and small-screen layout.
- Client-approved prices, hours, notice rules and pending-booking handling are still required.

The local test server is stopped after verification. No group repository changes have been pushed.
