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
