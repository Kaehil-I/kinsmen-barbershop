# Booking backend architecture and data design

This Part 2 implementation replaces the planned Azure SQL/Entity Framework persistence with MongoDB Atlas. It retains C#/ASP.NET Core and integrates the MVC front end, Auth0 identity provider and booking API. The system is deployed as two Docker web services on Render. This document describes the implemented design; client decisions that still need confirmation are explicitly identified in the run guide.

## Layers and responsibilities

```mermaid
flowchart LR
    UI[ASP.NET Core MVC customer and staff UI] -->|Server-to-server HTTPS and bearer token| API[ASP.NET Core endpoints]
    Identity[Auth0] -->|Validated subject and role claims| API
    API --> Rules[BookingService\nOwnership, pricing, scheduling, states]
    Rules --> Interface[IBookingStore and IBookingSession]
    Interface --> Store[MongoBookingStore\nTransactions and query mapping]
    Store --> DB[(MongoDB replica set\nServices, barbers, bookings, timeBlocks)]
```

Endpoints handle HTTP serialization, authentication and response codes. `BookingService` owns business rules and depends on repository interfaces, a clock and a configurable policy. `MongoBookingStore` implements persistent queries and transactions. Tests substitute a transactional test double for fast rule checks and also run against real MongoDB for concurrency/rollback behavior. No in-memory repository is registered by the production application.

The patterns actually used are dependency injection, repository abstraction and a transaction/unit-of-work boundary. Do not claim Factory, Strategy or Observer implementations just because they appeared in the Part 1 plan. Booking and review emails are delivered through Brevo after the relevant operation commits; there is no event/outbox system in this version.

## Domain relationships

```mermaid
classDiagram
    class CustomerIdentity {
        string subjectId
    }
    class Barber {
        string id
        string userId
        string name
        bool active
        WorkingPeriod[] hours
    }
    class ServiceItem {
        string id
        string name
        int priceCents
        int durationMinutes
        bool active
    }
    class Booking {
        string id
        string customerId
        string barberId
        DateTime startUtc
        DateTime endUtc
        int totalCents
        BookingStatus status
        long version
        string notes
        string creationFingerprint
    }
    class ServiceSnapshot {
        string serviceId
        string name
        int priceCents
        int durationMinutes
    }
    class TimeBlock {
        string id
        string barberId
        DateTime startUtc
        DateTime endUtc
        string reason
    }
    CustomerIdentity "1" --> "0..*" Booking : owns
    Barber "1" --> "0..*" Booking : assigned
    Barber "1" --> "0..*" TimeBlock : unavailable during
    Booking "1" *-- "1..10" ServiceSnapshot : captures
    ServiceSnapshot "0..*" --> "1" ServiceItem : references original
```

CustomerIdentity is an external authentication/account integration concept, not an implemented collection. The API enforces ownership using trusted token subject IDs. Customer registration and verifying administrator-supplied customer IDs against the account store are Kyra integration tasks.

## MongoDB collection model

```mermaid
erDiagram
    BARBERS ||--o{ BOOKINGS : assigned
    BARBERS ||--o{ TIME_BLOCKS : has
    BOOKINGS ||--|{ SERVICE_SNAPSHOTS : embeds
    SERVICES ||--o{ SERVICE_SNAPSHOTS : referenced_by
    BARBERS {
        string _id PK
        string userId UK
        string name
        array hours
        bool active
        long revision
    }
    SERVICES {
        string _id PK
        string name
        int priceCents
        int durationMinutes
        bool active
    }
    BOOKINGS {
        string _id PK
        string customerId
        string barberId FK
        date startUtc
        date endUtc
        array services
        int totalCents
        string status
        date createdUtc
        long version
        string notes
        string creationFingerprint
    }
    SERVICE_SNAPSHOTS {
        string serviceId FK
        string name
        int priceCents
        int durationMinutes
    }
    TIME_BLOCKS {
        string _id PK
        string barberId FK
        date startUtc
        date endUtc
        string reason
    }
```

FK labels show logical references; MongoDB does not automatically enforce SQL-style foreign keys. Services are validated by application rules when a booking is created. Barbers must exist. Administrator code must deactivate referenced services/barbers instead of deleting their historical records. Embedded service snapshots replace the old BookingService junction-table concept for this slice, preserving booked prices, names and durations when the catalogue changes. A booking and its snapshots can be read together.

`services`, `barbers`, `bookings` and `timeBlocks` have MongoDB JSON-schema validators. The executable definitions are in `src/Kinsmen.Api/Infrastructure/Schemas.cs`; export copies are under `schemas/` beside this document. Validators enforce required fields, BSON types, basic ranges and booking-status vocabulary. Application rules additionally enforce temporal relationships, totals, references, access and overlap constraints. Validators alone do not provide referential integrity or prevent double booking.

Money is stored in integer ZAR cents. Timestamps are BSON dates in UTC; presentation uses Africa/Johannesburg. Working periods store ISO weekdays (Monday=1 through Sunday=7) and minutes since local midnight. Periods should have start < end; overnight working shifts are not implemented. An appointment can end at midnight when a same-day period ends at minute 1440.

| Collection | Index | Purpose |
|---|---|---|
| All | `_id` unique, MongoDB default | Stable record lookup |
| barbers | `userId` unique | One barber profile per staff identity |
| bookings | `barberId`, `startUtc`, `endUtc` | Barber schedule and overlap query filtering |
| bookings | `customerId`, `startUtc` | Customer booking list |
| timeBlocks | `barberId`, `startUtc`, `endUtc` | Barber availability exclusions |

These indexes support queries; performance/load targets have not yet been measured on a hosted environment. The overlap query is `existing.start < requested.end AND existing.end > requested.start`. A unique start-time index alone would not prevent partial overlaps between appointments of different lengths.

## Implementation classes

```mermaid
classDiagram
    class BookingService {
        -IBookingStore store
        -TimeProvider clock
        -BookingPolicy policy
        +Create(actor, request) Task~Booking~
        +Availability(date, services, barber) Task
        +List(actor, from, to, barber) Task
        +Get(actor, id) Task~Booking~
        +Reschedule(actor, id, request) Task~Booking~
        +Cancel(actor, id, version) Task~Booking~
        +ChangeStatus(actor, id, request) Task~Booking~
        +AddBlock(actor, barber, request) Task~TimeBlock~
        +RemoveBlock(actor, id) Task~bool~
    }
    class IBookingStore {
        <<interface>>
        +Read(action, cancellationToken) Task
        +Write(action, cancellationToken) Task
    }
    class IBookingSession {
        <<interface>>
        +TouchBarbers(ids) Task
        +BookingById(id) Task~Booking~
        +Bookings(customer, barber, from, to) Task
        +Blocks(barber, from, to) Task
        +SaveBooking(booking, insert) Task
        +SaveBlock(block) Task
    }
    class MongoBookingStore {
        -IMongoClient client
        -IMongoDatabase db
        +Initialize() Task
        +SeedDemo() Task
        +Ping() Task
    }
    class MongoSession {
        -IClientSessionHandle session
    }
    BookingService --> IBookingStore
    MongoBookingStore ..|> IBookingStore
    MongoSession ..|> IBookingSession
    MongoBookingStore --> MongoSession : creates per operation
```

## Preventing concurrent booking conflicts

Each schedule-changing transaction first increments `barbers.revision` for every relevant barber, then queries booking/time-block overlaps and performs the change. Transactions use snapshot reads and majority writes. Two concurrent transactions touching the same barber cannot both commit against an old schedule: MongoDB produces a write conflict, and the driver's transaction callback retries against a fresh snapshot.

The transaction includes both the revision write and the booking or block write. A failed reschedule aborts all writes and leaves the original booking intact. This works across separate application/store instances; an in-process lock alone would not. Any-barber creation touches candidates in sorted ID order and is intentionally conservative for a small single-shop workload. It trades extra contention for a simple correctness boundary. Reassess this design if the client later expands to many branches/barbers.

```mermaid
sequenceDiagram
    actor Customer
    participant API
    participant Rules as BookingService
    participant DB as MongoDB transaction
    Customer->>API: POST booking with service IDs and start
    API->>Rules: Validated identity and request
    Rules->>DB: Begin transaction
    Rules->>DB: Increment candidate barber revisions
    Rules->>DB: Read catalogue, bookings and blocks
    Rules->>Rules: Compute total and duration; check hours and overlaps
    alt Time is available
        Rules->>DB: Insert Pending booking with snapshots
        DB-->>Rules: Commit
        Rules-->>API: Persisted booking
        API-->>Customer: 201
    else Slot unavailable
        Rules->>DB: Abort transaction
        API-->>Customer: 409 booking_conflict
    end
```

The same transaction discipline must be used by future admin scheduling code. Do not introduce direct writes that bypass this boundary. Tests exercise independent store instances racing for the same slot, a booking racing a time block, and failed rescheduling with a fresh connection reading the original data.

### Idempotent creation

When an `Idempotency-Key` is supplied, a SHA-256 hash of the authenticated subject and key becomes the booking ID. A separate hash captures the normalized original request, including target customer, requested barber or any-barber choice, service IDs, UTC start and notes. Retrying the same request reads and returns the existing booking. A different request using the same key returns a conflict.

The unique MongoDB `_id` index also protects cases where competing requests with the same key try different barbers and therefore touch different schedule documents. One insert wins. The losing transaction aborts; the service reads the winner outside the aborted transaction and checks its fingerprint. Thus request deduplication is database-backed across application instances, not an in-memory cache. Price/catalogue changes do not alter the result of a retry, because the existing booking's snapshots are retained. The internal fingerprint is excluded from HTTP responses. No automatic TTL deletion is applied to booking history or keys.

## State transitions and update conflicts

```mermaid
stateDiagram-v2
    [*] --> Pending: Customer or admin creates
    Pending --> Confirmed: Assigned barber or admin confirms before start
    Pending --> Cancelled: Customer owner or admin within notice policy
    Confirmed --> Cancelled: Customer owner or admin within notice policy
    Confirmed --> Completed: Assigned barber or admin after end
    Confirmed --> NoShow: Assigned barber or admin after start
    Cancelled --> [*]
    Completed --> [*]
    NoShow --> [*]
```

Rescheduling is allowed for Pending/Confirmed before the notice deadline and preserves status. Every successful booking mutation increments `version`; stale versions produce `409 stale_version`. Pending, Confirmed, Completed and NoShow records occupy their original interval; Cancelled does not. Historical records are retained, not deleted. There is no staff override of the notice policy or reopening of final states in this version.

## Deployment boundary and limitations

```mermaid
flowchart TB
    Browser[Public browser] -->|HTTPS| Web[Render: Kinsmen.Web]
    Web -->|HTTPS, server-to-server bearer token| Api[Render: Kinsmen.Api]
    Api -->|TLS and authenticated connection| Mongo[(MongoDB Atlas replica set)]
    Web -->|OpenID Connect| Identity[Auth0]
    Api -->|JWT validation and Management API| Identity
    Api -->|HTTPS transactional email| Brevo[Brevo]
    Env[Render environment secrets] --> Web
    Env --> Api
```

Render hosts the public MVC app and the API as separate Docker web services; MongoDB Atlas hosts the replica set used by booking transactions. Render receives the secrets through its dashboard rather than the repository, and the web application keeps Auth0 access tokens server-side while it calls the API. MongoDB replaces Azure SQL; no Azure packages, App Service settings, SQL migration pipeline or Azure connection strings are used. `render.yaml` pins staging to `integration-part2` and only triggers an automatic deployment after GitHub checks pass.

Auth0 provides account registration, password reset and production token issuance. The integrated system includes booking emails, reviews, administrator catalogue management, barber-service skill matching and staff-role management. Shop checkout, loyalty and guest booking are deliberately out of the agreed Part 2 scope. Before submission, validate that the final Render deployment has the required secrets, confirm the API readiness route, check the live booking and role workflows with real accounts, and retain the evidence. Automated tests support the deployment but do not replace live-service verification.

Official implementation references: [MongoDB transactions](https://www.mongodb.com/docs/drivers/csharp/current/crud/transactions/), [MongoDB atomicity](https://www.mongodb.com/docs/manual/core/write-operations-atomicity/), [ASP.NET Core authentication and authorization](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/security?view=aspnetcore-10.0).
