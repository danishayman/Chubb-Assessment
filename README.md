# Claims Platform

A claims platform for motor and property claims across six APAC markets.
- **Claimants** report an incident, track their claim, answer requests for more information (with photos, reports or receipts attached) and see the decision.
- **Claims staff** pick up claims, set reserves, ask for information, approve or reject, and settle. They also see team workload and outstanding liability.

Built for the YGP full-stack take-home. Most of the time went into the backend, as the brief asks.

## Running it

You need the **.NET 10 SDK** and **Node 20.19+ / 22.12+ / 24+**. No database, Docker or Kafka is required for the default setup.

```bash
# API: http://localhost:5177 (Swagger UI at /swagger)
cd backend
dotnet run --project src/Claims.Api --launch-profile http

# Web: http://localhost:4200 (proxies /api to the API)
cd frontend
npm install
npm start
```

The API creates `claims.db` (SQLite), applies migrations and seeds demo data on first start. Uploaded documents go in `uploads/` next to it. Delete both to reset.

Keep both terminals running. If the login page says it cannot reach the API, the backend isn't running on port 5177.

**Demo accounts.** The password for all of them is `Password123!`. The login page also has one-click buttons.

| Who | Email | Role |
|---|---|---|
| Aisha Tan | aisha@example.com | Claimant (SG, motor + home) |
| Raj Kumar | raj@example.com | Claimant (MY, motor) |
| Mei Ling Wong | mei@example.com | Claimant (HK, home) |
| Daniel Lim | daniel@chubb.test | Claims officer |
| Priya Nair | priya@chubb.test | Claims officer |
| Sarah Chen | sarah@chubb.test | Manager (can reassign and act on any claim) |

**Tests:** `cd backend && dotnet test`. This runs 42 tests: domain rules, plus API integration tests against in-memory SQLite (including file uploads).

### Using real Kafka (optional)

The event bus defaults to an in-process implementation, so the app runs anywhere. To use Kafka:

```bash
docker compose up -d                      # single-node Kafka (KRaft) on localhost:9092
cd backend
Messaging__Provider=Kafka dotnet run --project src/Claims.Api --launch-profile http
```

On PowerShell, set the variable with `$env:Messaging__Provider="Kafka"`.

## Architecture

```
 Angular SPA ──REST/JSON──▶ Claims API (ASP.NET Core)
                              │
                              ├─ Claim aggregate (Claims.Domain): state machine + rules
                              │
                              ├─ SQLite: claims, policies, users, audit trail, notifications
                              │     └─ OutboxMessages ◀── written in the same transaction
                              │
                              └─ OutboxDispatcher ──▶ Kafka topic "claims.events" (key = claim id)
                                                         │
                                       ┌─────────────────┴──────────────┐
                                 NotificationProjector             PaymentHandoff
                                 (in-app notifications)         (stub for finance)
```

### Key decisions

**One deployable service, with modules inside.** The domain is a single bounded context: one team, one data model, and a 3-hour budget. Splitting it into microservices now would add network hops, distributed transactions and deployment overhead without solving a real problem. The code is still set up for a split:
- The domain lives in its own project, with no infrastructure dependencies.
- Side effects (notifications, payments) only happen through events.

So notifications or payments could move into their own service, on its own consumer group, without touching the claim logic.

**REST vs Kafka**

| | Used for | Why |
|---|---|---|
| REST | All commands and queries from the UI (submit, assign, approve, list, dashboards) | The user is waiting and needs an immediate answer, including validation errors. |
| Kafka | Domain events: submitted, assigned, status changed, info requested/provided, reserve changed, settled | Consumers can be added without touching the claim service. Notifications, payments, analytics and fraud checks can all run behind the user's request. |

- **One topic, keyed by claim id.** Kafka only guarantees order within a partition, so this keeps each claim's events in order and still spreads load across partitions.
- **Outbox pattern.** An event is saved in the same database transaction as the state change. A background dispatcher then publishes it. If Kafka is down, nothing is lost and the API keeps working; events wait in the outbox and retry. The dispatcher stops at the first failure so ordering is kept.
- **At-least-once delivery, idempotent consumers.** Offsets are committed only after the handlers succeed. Notifications have a unique key on `(user, source event)`, so a redelivered event creates nothing new.
- **Synchronous audit trail, asynchronous notifications.** The audit trail is written in the same transaction as the change, because an audit log that can lag or go missing isn't one. Notifications can be eventually consistent.

**Async in the UI.** Notifications arrive after the request completes, so the Angular app polls every 10 seconds. When something new arrives, the open page refreshes. Server-sent events would be the next step.

**The contract.** The backend owns the contract. It serves OpenAPI at `/openapi/v1.json` (browse it at `/swagger`). The Angular models in `frontend/src/app/core/models.ts` mirror the DTOs. With more time I'd generate them from the OpenAPI document so the two can't drift.

### Domain

Core entities:
- `Policy`: holder, type, market, currency, limit, excess, period
- `Claim`: the aggregate root
- `InformationRequest`
- `Attachment`: a document the claimant sent, optionally linked to the request it answers
- `ClaimNote` (internal only)
- `ClaimActivity` (audit trail)
- `Notification`

Claim states:

```
Submitted ──assign──▶ UnderReview ──approve──▶ Approved ──settle──▶ Settled
                         │   ▲  └──reject──▶ Rejected
              request info│   │all answered
                         ▼   │
                  AwaitingInformation ──reject──▶ Rejected
```

The `Claim` aggregate enforces these rules, so no endpoint can get around them:
- Transitions follow the diagram above. Anything else returns **409**.
- Only the **assigned** officer can work a claim. Officers pick up work for themselves; only a manager can assign to someone else.
- A manager can also act on any assigned claim directly, for example to cover an absent officer. The claim keeps its owner, and the audit trail records the manager as the actor.
- A claim can't be approved while any information request is still open.
- The approved amount can't exceed claimed amount minus excess, capped at the coverage limit. A reserve can't exceed the limit.
- Rejection needs a reason, which the claimant sees.
- There is optimistic concurrency on the claim, so two officers acting at once can't silently overwrite each other. The loser gets a **409**.

### Outstanding liability

Exposure is the sum of **reserves on open claims** (Submitted through Approved).
- When a claim comes in, its reserve starts at what the policy would pay (claimed amount minus excess, capped at the limit). That way exposure is visible before an officer picks the claim up.
- Officers then adjust the reserve as they learn more.
- Approval fixes the reserve at the approved amount.
- Settlement or rejection releases it.

Exposure is reported **per market and currency**. Six markets means six currencies, and there's no FX source, so a grand total would be misleading. The same goes for "largest claims", which are ranked within each market.

### Documents

Claimants can attach PDF, JPG, PNG or WEBP files when they report an incident, when they answer an information request, or at any point while the claim is open. Staff can download them from the claim workbench, and the assigned officer gets a notification for each new document.

- **The file's own bytes decide its type.** The browser's Content-Type and the file name are both controlled by the user, so the server reads the first bytes (the "magic number"). It rejects the file unless they match an allowed type *and* the extension agrees. An executable renamed to `.pdf` is refused.
- **Stored under a generated key.** Each file is saved under a generated `claim/attachment` id and never under the user's file name, so a crafted name can't escape the storage folder. Downloads are sent as `attachment` with `nosniff`, so the browser never renders them inline.
- **The domain checks run before any bytes are written:** only the claimant, only while the claim is open, at most 10 MB per file and 20 per claim. If the database save fails, the stored file is deleted again.
- **Storage sits behind `IFileStore`.** It's local disk here; in production it would be blob storage plus a virus scan before the file is made available.
- **Uploads are multipart REST, not Kafka.** The claimant waits for the result and needs an immediate error if the file is rejected. The `attachment-added` event that follows goes through the outbox like every other event.

### What claimants and staff see

Claimants only see their own claims; anything else returns 404, not 403, so the API doesn't reveal which claims exist. They never see:
- reserves
- internal notes
- internal timeline entries, such as assignments
- which officer is handling the claim (it shows as "Claims team")

### What a claims officer gets
- A work queue (unassigned / mine / all) with status, market and search filters. Age is highlighted after 14 days.
- A claim workbench showing limit, excess and max payable next to the reserve, the claimant's documents (linked to the request they answer), the available actions for the current state, internal notes and the full audit trail.
- A team dashboard:
  - open claims by status
  - ageing buckets
  - per-officer load: open, waiting on the claimant, oldest, closed in the last 30 days, average days to close
  - exposure by market and type

## What I left out, and what I'd do next

- **Virus scanning and blob storage** for uploaded documents. They're on local disk and only type-checked today.
- **Staff uploads** (assessor reports, repair invoices). Only claimants upload today.
- **Real identity.** The demo uses local users with JWT. In production this would be the corporate IdP (OIDC) for staff and a customer identity provider for claimants.
- **FX and reporting currency** for a single group-wide exposure number.
- **Dashboards at scale.** These aggregate in memory, which is fine for the demo. At volume they'd become SQL aggregates or a read model fed by the same events.
- **A dead-letter topic.** A Kafka message whose handler keeps failing is currently logged and skipped.
- **SSE/WebSocket push** instead of polling.
- **Frontend tests**, and OpenAPI-generated client types.
- **Postgres** instead of SQLite in any shared environment. EF Core makes this a provider swap plus a fresh migration.
- **Verify Kafka end to end.** The Kafka path is built and fails safely when there's no broker (events stay in the outbox). There was no Docker on my machine, so I haven't run it against a live broker.

## Repo layout

```
backend/
  src/Claims.Domain/   entities, state machine, events, exposure calc (no infrastructure)
  src/Claims.Api/      endpoints, EF Core + migrations + seed, auth, outbox, Kafka/in-memory bus, consumers
  tests/Claims.Tests/  domain unit tests + API integration tests
frontend/              Angular 21 (standalone components, signals)
docker-compose.yml     optional Kafka
AI-JOURNAL.md          how I worked with the AI
```
