# Supplier Feed Service

ASP.NET Core Web API that ingests a webhook-style reservation feed from external suppliers,
de-duplicating resends and rate-limiting abusive suppliers, across multiple instances sharing
one SQLite file. See `CLAUDE.md` for architecture/commands.

## Assumptions made where the spec was unclear

- **SQLite over LocalDB**: the spec allowed either; LocalDB is Windows-only and development is
  on macOS, so SQLite (Dapper + `Microsoft.Data.Sqlite`) was the only real option.
- **Natural key is `(SupplierId, ReservationId)`, not `ReservationId` alone**: reservation IDs
  are supplier-local identifiers with no shared numbering authority, so two suppliers could
  plausibly send the same ID for unrelated bookings.
- **Idempotency compares only business fields** (`roomId`, `checkIn`, `checkOut`, `price`);
  `updatedAtUtc` is excluded from the equality check but used as a version guard: an incoming
  update older than what's stored is rejected as `IgnoredStale` (distinct from `UnchangedDuplicate`)
  rather than silently overwriting newer data.
- **"Ingested" (stats) = every request that passed the rate limiter** (created + updated +
  unchanged + stale), **"throttled" = 429s** - the spec didn't define these precisely.
- **Deployment topology**: multiple instances on one host/shared local disk, not distributed
  across hosts without shared storage - this is load-bearing for the whole SQLite locking
  strategy and was confirmed explicitly rather than assumed silently.

## Where I changed course from what Claude Code first suggested

- I initially proposed treating a stale out-of-order update as a silent no-op merged with a
  true duplicate resend. I pushed back - an invisible silent drop of a real (if late) data
  change is worse than a visible conflict - so it became a distinct `IgnoredStale` outcome
  (HTTP 409, separate stats counter) instead.
- Claude Code's first plan included a background `BackgroundService` for pruning the rate-limit
  log table. For this project's scale that's an unnecessary moving part; pruning was folded
  inline into the same transaction that already holds the write lock for the rate-limit check.
- Claude Code started prescribing exact HTTP status codes mid-conversation before being asked;
  I redirected it to treat that as a design detail to finalize in the written plan, not something
  to bikeshed field-by-field in chat.

## What I'd do differently with more time

<!-- TODO: fill in -->
