# ADR-0004: EF Core for CRUD, Dapper for analytics, binary COPY for telemetry

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

The system has three very different data-access workloads:

1. **CRUD** on meters, alert rules and alerts: small, relational, with validation and concurrency.
2. **Analytical reads**: range queries over the hypertable and its rollups that use `time_bucket`,
   `first`/`last` and window maths.
3. **Telemetry writes**: tens of thousands of rows per second in batches, which must be
   **idempotent** because MQTT QoS 1 redelivers after reconnects.

## Options considered

| Workload | Options |
|---|---|
| CRUD | EF Core · Dapper |
| Analytics | EF Core LINQ · EF Core raw SQL · Dapper |
| Telemetry | EF Core `SaveChanges` · multi-row `INSERT … unnest(@arrays)` · binary `COPY` (direct or via staging) |

## Decision

- **EF Core** for CRUD and **all schema migrations**. Its change tracking, validation integration and
  migrations are worth it for small relational writes.
- **Dapper** for analytics. The SQL is TimescaleDB-specific (`time_bucket`, continuous aggregates,
  `first`/`last`), so it is clearer written as SQL than coaxed out of LINQ, and Dapper adds nothing
  per row beyond materialisation.
- **Npgsql binary `COPY`** for telemetry, **optimistically straight into the hypertable**. If the
  primary key `(meter_id, time)` rejects the batch because it contains a redelivered reading, the
  transaction rolls back and the batch is retried through a temporary staging table plus
  `INSERT … ON CONFLICT DO NOTHING`. The result is the same either way, and the expensive path
  only runs when a duplicate actually exists.

The choice was measured, not assumed (`docs/benchmarks.md`, section 2):

| Writer, 5,000-row batch | Rows/s | Allocated |
|---|---:|---:|
| EF Core | ~9,000 | 39.5 MB |
| Dapper `unnest` + `ON CONFLICT` | ~52,000 | 416 KB |
| COPY → staging → `ON CONFLICT` | ~55,000 | 474 KB |
| Raw COPY (not idempotent) | ~240,000 | 472 KB |
| **Optimistic COPY, staging merge only on a duplicate (chosen)** | **~193,000** | 472 KB |

## Consequences

- Three data-access styles in one codebase. Each is confined to one place:
  - EF Core: `GridPulseDbContext`, the API's CRUD endpoints and the alert dispatcher.
  - Dapper: `TelemetryQueries`.
  - COPY: `CopyReadingBatchWriter`.
- The `readings` table is still mapped in EF Core, so that migrations own its schema, but it is
  never written through EF Core.
- The first version always went through the staging merge, at about 55,000 rows/s. Benchmarks showed
  the conflict-checking insert, not the transport, was the cost. The optimistic path made the writer
  3.3× faster, and end to end it halved database CPU (`docs/benchmarks.md`, section 3).
- **The trade-off:** a batch that does contain a duplicate pays for the copy twice. That only
  happens around MQTT reconnects, so it is acceptable.
