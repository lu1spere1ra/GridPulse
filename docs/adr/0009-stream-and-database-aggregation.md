# ADR-0009: Aggregate twice, in the stream for latency and in the database for truth

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

1- and 15-minute aggregates are needed in two places:

- **Live:** the dashboard and the threshold alerts need a closed 1-minute window as soon as the
  minute ends, not when a database refresh policy next runs.
- **Historical:** charts over hours or days, the daily report and analytics need aggregates that
  are complete. That means they must include late readings and survive restarts.

## Options considered

1. **Database only (continuous aggregates).** One implementation. But freshness depends on the
   refresh policy (or on real-time aggregation scanning raw rows on every read), and alerting would
   have to poll the database.
2. **Stream only.** Persist the in-memory aggregates. It is fast, but late readings are lost from
   the aggregates, and a crash loses the open windows. The in-memory state becomes the source of truth.
3. **Both, with clear roles.**

## Decision

Both, with explicit roles:

| | Stream (`WindowAggregator`, in ingestion) | Database (`readings_1m/15m/1h`) |
|---|---|---|
| Purpose | Latency: live charts, threshold alerts | Truth: history, reports, analytics |
| Time semantics | Event time; a window closes when a newer reading arrives or at `end + 5 s` | Event time (`time_bucket`), recomputed on refresh |
| Late readings | Counted as `late`, excluded | Included |
| Persisted | No | Yes |

- **Consistency.** Both sides use the same bucket alignment (UTC, epoch-aligned, equal to
  `time_bucket` for intervals that divide a day) and the same energy semantics (last register
  minus first register inside the bucket).
- **The consistency is tested, not assumed.** An integration test feeds the same irregular readings
  to both paths and requires identical count, sum, minimum, maximum and energy, for 1- and 15-minute
  windows, in both TimescaleDB editions.
- **Time is injectable.** Everything that depends on the clock (window closing, the offline
  detector, the simulator scheduler) takes a `TimeProvider`, so the timing rules are unit-tested with
  `FakeTimeProvider` instead of `Thread.Sleep`.

## Consequences

- The stream and the database can briefly disagree. A late reading changes the stored bucket but
  not the live one that was already shown. The dashboard's history views read from the database,
  so after a refresh the operator sees the true value.
- **Energy totals over a range** use `max(last) - min(first)` of the register, not a sum of
  per-bucket deltas. The per-bucket delta misses the energy between the last reading of one bucket
  and the first of the next; the register difference over the whole range does not.
