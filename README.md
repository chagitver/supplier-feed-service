# Supplier Feed Service

ASP.NET Core Web API that ingests a webhook-style reservation feed from external suppliers, de-duplicating resends and rate-limiting abusive suppliers, across multiple instances sharing one SQLite file. See `CLAUDE.md` for architecture/commands.

## Assumptions made where the spec was unclear

- **SQLite over LocalDB**: the spec allowed either; LocalDB is Windows-only and development is on macOS, so SQLite (Dapper + `Microsoft.Data.Sqlite`) was the only real option.
- **Natural key is `(SupplierId, ReservationId)`, not `ReservationId` alone**: reservation IDs are supplier-local identifiers with no shared numbering authority, so two suppliers could plausibly send the same ID for unrelated bookings.
- **Idempotency compares only business fields** (`roomId`, `checkIn`, `checkOut`, `price`); `updatedAtUtc` is excluded from the equality check but used as a version guard: an incoming update older than what's stored is rejected as `IgnoredStale` (distinct from `UnchangedDuplicate`) rather than silently overwriting newer data.
- **"Ingested" (stats) = every request that passed the rate limiter** (created + updated + unchanged + stale); "throttled" = 429s - the spec didn't define these precisely.
- **Deployment topology**: multiple instances on one host/shared local disk, not distributed across hosts without shared storage - this is load-bearing for the whole SQLite locking strategy and was confirmed explicitly rather than assumed silently.

## Where I changed course from what Claude Code first suggested

- I initially proposed treating a stale out-of-order update as a silent no-op merged with a true duplicate resend. I pushed back - an invisible silent drop of a real (if late) data change is worse than a visible conflict - so it became a distinct `IgnoredStale` outcome (HTTP 409, separate stats counter) instead.
- Claude Code's first plan included a background `BackgroundService` for pruning the rate-limit log table. For this project's scale that's an unnecessary moving part; pruning was folded inline into the same transaction that already holds the write lock for the rate-limit check.
- Claude Code started prescribing exact HTTP status codes mid-conversation before being asked; I redirected it to treat that as a design detail to finalize in the written plan, not something to bikeshed field-by-field in chat.
- After reviewing the initial implementation, I asked for global exception handling (built-in `IExceptionHandler`, no new logging package) and a warning-level log on every throttled request - the original pass had no structured error response and no operational signal for the one thing this service exists to catch.

## What I'd do differently with more time

- **Cross-instance timing assumptions aren't hardened**: rate-limiter timestamps are per-instance wall-clock (no centrally synchronized time), and the rate-limit check and the reservation write are two separate transactions - a failure between them could consume rate-limit budget without a matching stats entry. Both are small, mostly-theoretical gaps at this scale, not fixed.
- **The `SQLITE_BUSY`/`SQLITE_LOCKED` retry path is effectively unexercised** - `busy_timeout` likely absorbs all contention this test suite can generate before the retry logic ever triggers. Not fixed.
- **No authentication binds a request to an actual supplier** - anyone can claim any `supplierId`. Left out because it's a real product decision (API keys? mTLS? HMAC?) outside the given spec, but it's the first thing I'd want before this touched real traffic.
- **Domain validation is still shallow**: required fields and a positive price are enforced, but rules like `checkIn < checkOut` were never stated in the spec, so I didn't want to guess at unstated business rules rather than risk encoding the wrong ones.

Two related gaps that looked cheap to close were fixed rather than deferred: the rate limiter's 100/101 boundary is now also tested under genuine concurrent load (not just sequentially), and the cross-instance correctness (two independent server instances sharing one file) that was previously only checked by hand with `curl` now has an automated regression test.
