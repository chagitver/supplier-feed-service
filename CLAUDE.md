# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

Early-stage scaffold. The solution and both projects exist and build, but the domain code (schema, repositories, services, controllers) has not been written yet — see "Planned Architecture" below for the approved design this codebase is being built toward.

## Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test (by fully-qualified name or filter expression)
dotnet test --filter "FullyQualifiedName~ReservationRepositoryTests"
dotnet test --filter "DisplayName~Throttles_101st_request"

# Run the API locally
dotnet run --project src/SupplierFeedService.Api
# Swagger UI opens at the launch URL (http://localhost:5129/swagger by default, see
# src/SupplierFeedService.Api/Properties/launchSettings.json)

# Run two instances on different ports against the same SQLite file, to validate
# cross-instance rate-limiting/idempotency correctness
dotnet run --project src/SupplierFeedService.Api --urls=http://localhost:5001
dotnet run --project src/SupplierFeedService.Api --urls=http://localhost:5002
```

Solution file is `SupplierFeedService.slnx` (new XML solution format), not a `.sln`.

## Planned Architecture

This service sits in front of a reservations table and ingests a webhook-style feed from external suppliers, solving two problems: duplicate/out-of-order resends from suppliers, and per-supplier traffic abuse. It runs as **multiple instances sharing one local SQLite file on the same disk** (not distributed across hosts without shared storage) — this assumption is load-bearing for the whole locking strategy below.

Two projects, no extra class-library splitting:
- `src/SupplierFeedService.Api` — ASP.NET Core Web API (.NET 8, Controllers not minimal APIs, Dapper + `Microsoft.Data.Sqlite` for data access, no EF Core).
- `tests/SupplierFeedService.Tests` — xUnit, references the API project directly (`Program.cs` needs `public partial class Program {}` for `WebApplicationFactory<Program>`).

### Key design decisions (non-obvious, read before changing data-access or rate-limit code)

- **Natural key is `(SupplierId, ReservationId)` composite, never `ReservationId` alone** — reservation IDs are supplier-local identifiers, not globally unique, so two different suppliers could send the same `reservationId` for unrelated bookings.
- **Idempotency check compares only business fields** (`roomId`, `checkIn`, `checkOut`, `price`) against the stored row — `updatedAtUtc` is excluded from the equality check.
- **Regression guard**: if business fields differ AND the incoming `updatedAtUtc` is older than what's stored, the write is skipped (would regress the record to older data) — but this is a distinct outcome (`IgnoredStale`, not `UnchangedDuplicate`) so it stays observable rather than silently dropped.
- **Rate limiting is a SQL-backed sliding-window log**, not in-memory-per-instance counters (those would let the effective limit scale with instance count) and not Redis/external cache (unnecessary given the shared-disk topology). Every request attempt is logged regardless of outcome, so the window clears naturally as old attempts age out.
- **All data access goes through narrow repository interfaces** (`IReservationRepository`, `IRateLimitLogRepository`, `ISupplierStatsRepository`) with `ISqliteConnectionFactory` as the *only* seam that knows about SQLite specifics (pragmas, connection string, `BEGIN IMMEDIATE` semantics). This is deliberate: business/service logic must never reference `Microsoft.Data.Sqlite` types directly, so swapping to a client-server DB (Postgres/SQL Server) later — if instances end up on separate hosts without shared storage — is a contained change, not a rewrite.
- **SQLite concurrency**: every connection sets `PRAGMA journal_mode=WAL; synchronous=NORMAL; busy_timeout=5000; foreign_keys=ON`. Writes that need read-then-decide-then-write atomicity (the upsert-with-classification logic, the rate-limit count-then-log check) use `BEGIN IMMEDIATE` explicitly rather than Microsoft.Data.Sqlite's default deferred transaction, because deferred transactions only acquire the write lock lazily and would let two instances race past the initial read. A single `INSERT ... ON CONFLICT DO UPDATE` is deliberately *not* used for the reservation upsert, because classifying the result (created/updated/unchanged/stale) requires reading the row's prior state first — a conditional upsert can tell you whether a write happened, not why it didn't.
- **Log pruning is an implementation detail hidden inside `RateLimitLogRepository`**, not a separate background service — it's an inline `DELETE` riding along on the same transaction that already holds the write lock for the rate-limit check.
- Prices are stored as invariant-culture decimal strings, not `REAL` — avoids floating-point round-trip artifacts that would cause false "changed" detections in the equality check.

### Full design reference

The complete approved design — exact schema, SQL statements, DTO shapes, HTTP status mapping, and the full test plan — lives in the plan doc at `/Users/hag/.claude/plans/i-need-your-asistent-zany-sifakis.md` (outside this repo, a Claude Code session artifact). If that file is unavailable, treat the summary above as authoritative and consult git history / existing code once it's written, since the plan will not be checked into this repository.

## External agent config detected

An OpenAI Codex config exists at `~/.codex/config.toml` (user-level, not project-specific). This was not read or imported. If you'd like to import any of it (MCP servers, instructions, etc.), reply `/import` to scan what's importable, then `/import --yes=<digest>` to apply it.
