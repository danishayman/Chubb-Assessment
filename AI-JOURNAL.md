# AI working journal

Tool: Claude Code (desktop app), one long session. I gave the direction and the AI wrote most of the code.
Each entry records what I asked, what the AI did, and what I decided.
Entries marked **[review]** are calls the AI made by itself. I still need to accept or override them.

---

## 1. Reading the brief and planning
- **Asked:** read the brief and plan the build.
- **AI proposed:** .NET 10 API, EF Core + Postgres, Kafka in Docker Compose, a lean Angular UI, an outbox for events.
- **My decisions:**
  - .NET over Spring Boot. The SDK is already installed, and Maven isn't.
  - Lean Angular UI. The brief weights the backend more.
  - **No Docker on this PC.** So the AI changed the plan:
    - SQLite instead of Postgres. Nothing to install, and the app still starts with one command.
    - The event bus sits behind an interface. An in-memory provider is the default, and Kafka is a config switch. `docker-compose.yml` is kept for anyone who has Docker.
  - Commits are under my name with short messages. AI use is documented here, not in commit trailers.

## 2. Domain model
- **Asked:** build the domain first (entities, status flow) with tests.
- **AI did:** wrote a `Claim` aggregate that owns its state machine, info requests, notes, an audit trail and domain events. Behaviour sits on the entity instead of in services.
- **[review] Initial reserve.** The AI sets the reserve at submission to claimed amount minus excess, capped at the limit. Exposure is then visible from day one instead of after an officer picks the claim up. The trade-off is that claimants could inflate early exposure figures.
- **[review] Officer authority.** Only the assigned officer can approve, reject, settle, set the reserve or request info. A manager reassigns rather than acting directly.
- **[review] Audit trail is synchronous.** The plan had the timeline built by an event consumer. The AI changed this so audit entries are written in the same transaction as the change. Reason: an audit trail that can lag or be lost is not an audit trail. Notifications stay event-driven.
- **[review] AwaitingInformation → Rejected allowed.** The AI added this after noticing that otherwise a claimant who never replies leaves the claim stuck forever.
- **[review] No total exposure across markets.** Six markets means six currencies, so exposure is grouped by market and currency. There is no FX table, so a single total would be wrong.
- **[review] Single Kafka topic.** The AI plans one `claims.events` topic keyed by claim id, not one topic per event type. Kafka only orders messages within a partition, so this keeps every claim's events in order.

## 3. Persistence and auth
- **AI did:** EF Core with SQLite, migrations applied on startup, and seed data covering every status so each screen has something on it. JWT auth with seeded users and PBKDF2 password hashing.
- **Bugs found while running it:**
  - The outbox code changed the change-tracker collection while iterating over it, which crashed startup. Fixed with `.ToList()`.
  - SQLite returns `DateTime` without a UTC kind, so Angular would have shown local times shifted. Fixed with a UTC value converter.
- **[review] Child entity keys.** Keys are generated in the domain and configured `ValueGeneratedNever`. Otherwise EF treats new info requests and notes on a loaded claim as updates, not inserts.

## 4. Claims API
- **AI did:** minimal API endpoints. Each action loads the aggregate, calls one domain method and saves. Errors map to ProblemDetails:
  - validation → 400
  - wrong officer → 403
  - wrong state or concurrent edit → 409
- **[review] Separate claimant and staff views.** Claimants and staff get the same DTO shape, but claimants get nulls for reserve, notes and max payable. Staff names show as "Claims team" to claimants.
- **[review] 404 for other people's claims.** A claimant asking for someone else's claim gets 404, not 403, so the API doesn't confirm the claim exists.
- **Checked:** a smoke script drove the full flow over HTTP (submit → assign → info → respond → approve → settle) plus the negative cases. All behaved as expected.

## 5. Events
- **AI did:**
  - an outbox table and dispatcher
  - an `IEventPublisher` interface with in-memory and Kafka implementations
  - a Kafka consumer with manual commits and retries
  - two handlers: notifications, and a payment handoff stub
- **Checked:** with `Provider=Kafka` and no broker, the API stays up and the events stay in the outbox with the error recorded. That is the outbox doing its job.
- **Not verified:** the Kafka path has not run against a real broker (no Docker on this PC).
- **[review] One consumer group runs all handlers.** In production each downstream service would have its own group.
- **[review] Dead letters.** A message that keeps failing is logged and skipped. The intended fix, a dead-letter topic, is noted but not built.

## 6. Dashboards
- **AI did:** a workload endpoint (by status, ageing, per officer) and an exposure endpoint (by market and type).
- **AI caught its own mistake:** the first version of "largest open claims" ranked raw amounts across currencies, which would put an IDR claim above an SGD one. It changed this to the top 3 per market.
- **[review] In-memory aggregation.** It's fine for the demo; the scaling path is noted in the README.

## 7. Frontend
- **AI did:** Angular 21 with standalone components, signals and lazy routes. Separate claimant and staff areas use role guards, a JWT interceptor, and notification polling that refreshes open pages.
- **Angular version:** Angular 22 needs Node 24.15+, and this PC has 24.13, so the AI used Angular 21 instead of asking me to upgrade Node.
- **Checked in the browser:** the AI drove the full flow as claimant, officer and claimant again. That surfaced three problems, all fixed:
  - Two timeline entries from one action had the same timestamp and came back in the wrong order. Fixed in the domain with a per-operation tick offset, and there's a test.
  - The timeline showed raw enum names ("UnderReview") to claimants.
  - The queue's "Include closed" checkbox stretched across the filter bar. Also, the loss amount defaulted to 0 instead of empty.

## 8. Docs
- **AI did:** wrote the README covering run steps, architecture decisions, the REST/Kafka split, and what was left out.

## 9. Document uploads
- **Asked:** add attachments, so claimants can send the police report and receipts an officer asks for.
- **AI did:**
  - an `Attachment` entity owned by the claim, plus a migration
  - multipart upload and authenticated download endpoints
  - local file storage behind `IFileStore`
  - an `attachment-added` event that notifies the assigned officer
- **[review] Type is decided by content.** The AI checks the file's first bytes and requires the extension to match, instead of trusting the browser's Content-Type or the file name. The tests include an `.exe` renamed to `.pdf`.
- **[review] Safe storage and download.** Files are stored under generated ids, never the user's file name. Downloads are served as `attachment` with `nosniff`.
- **[review] Limits.** 10 MB per file, 20 per claim, and PDF/JPG/PNG/WEBP only. Only the claimant can upload; staff can download.
- **[review] Order of operations.** Domain checks run before any bytes are written. If the database save fails, the stored file is deleted.
- **[review] Antiforgery is off for the upload endpoint.** Auth is a bearer token, not a cookie, so there's no CSRF risk.
- **[review] Replies with only a file.** A claimant can answer a request with just files; the UI fills in "I've attached N documents: …" as the text. Files upload one at a time, and each leaves the list once it's stored, so retrying after a failure doesn't create duplicates.
- **Environment:** my own API was running and locking the Debug build, so the AI built, generated the migration and ran the tests in Release instead of stopping it.

## 10. UI pass
- **Asked:** improve the overall UI. I also sent a screenshot showing the description box dragged wider than its card.
- **AI did:** kept the existing look (navy bar, blue accent, system font) and fixed what it found by using the app:
  - the textarea overflow (resize is vertical only now)
  - no loading states (pages were blank until data arrived; they show skeletons now)
  - no hover or focus styles on buttons
  - an emoji bell and text arrows used as icons (replaced with one SVG icon set)
  - the notifications panel didn't close on outside click or Escape
  - queue rows that only opened with a mouse (claim numbers are links now)
  - a browser `confirm()` for rejecting a claim (now an inline two-step confirm)
  - empty states that didn't say what to do next
- **Report form:** sections for policy, incident and documents, with a "What happens next" panel beside them.
- **Dashboard:** the four big-number cards became one summary strip with context, such as how old the oldest unassigned claim is.
- **Checked in the browser** at desktop and phone width: submitting with files (a `.txt` was rejected on the client), replying with just a file, the officer view and download, and the notification panel. No console errors and no horizontal scroll on phone. On phone, claim numbers in the queue were breaking across four lines, so the table now scrolls inside its card.
