# ADR-0001: PostgreSQL + TimescaleDB for telemetry, with an edition-aware schema

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

- **Volume.** At 5,000 meters × 1 Hz the system stores 432 M rows/day.
- **Queries.** Almost every query is "one meter or the whole fleet, over a time range, at some
  resolution".
- **Relational data alongside.** Meters, alert rules and alerts are ordinary relational data that
  we want in the same database, with foreign keys and EF Core.
- **Azure.** The target is Azure Database for PostgreSQL Flexible Server, which ships only the
  **Apache-2 edition** of TimescaleDB. Continuous aggregates, compression and background policies
  are Timescale License (TSL) features and are not available there.

## Options considered

1. **Plain PostgreSQL with native partitioning.** Works everywhere, but we would hand-roll
   partition creation and rollups.
2. **PostgreSQL + TimescaleDB.**
   - Hypertables give automatic time partitioning, and `time_bucket` handles bucketing.
   - In the TSL edition, incremental continuous aggregates, columnar compression and retention
     policies come with it.
   - It is still PostgreSQL: EF Core, Npgsql, Dapper and joins with relational tables all work.
3. **A dedicated time-series database** (InfluxDB, Azure Data Explorer). Excellent ingest and
   query performance, but a second database for the relational data, a second query language and
   another service to run.

## Decision

PostgreSQL + TimescaleDB, with a schema that adapts to the edition at deploy time:

| Object | Community (TSL) edition — local Docker, Timescale Cloud | Apache-2 edition — Azure Flexible Server |
|---|---|---|
| `readings` | hypertable, 1-hour chunks, PK `(meter_id, time)` | same |
| `readings_1m` | continuous aggregate (real-time) | view: `time_bucket` over `readings` |
| `readings_15m` | hierarchical continuous aggregate on `readings_1m` | view over `readings_1m` |
| `readings_1h` | hierarchical continuous aggregate on `readings_15m` | view over `readings_15m` |
| Compression | after 1 day, `segmentby meter_id` | — |
| Retention | raw readings 7 days | — (manual `drop_chunks`, or `pg_cron`) |

- **How the split is applied.** EF Core migrations own the tables and the hypertable (Apache
  features). `TimescaleSetup` runs after the migrations, detects the edition
  (`SHOW timescaledb.license`) and creates the rollups idempotently.
- **Why outside the migration.** It cannot live inside an EF migration for two reasons: the edition
  is only known at run time, and continuous aggregates cannot be created inside a transaction.
- **Stored columns.** Rollups store `sum` and `count` rather than `avg`, and `first`/`last` of the
  energy register. This lets coarser buckets be computed exactly from finer ones, and energy is a
  register difference rather than an integral of sampled power.
- **Same query surface.** Both editions expose the same relation names and columns, so the Dapper
  queries are identical. Integration tests run the whole suite against both editions (the Apache
  container is started with `-c timescaledb.license=apache`).

## Consequences

- On Azure, rollups are computed at query time, so long ranges are slower and there is no
  automatic compression or retention.
  - Mitigations, in order of effort: a `pg_cron` job calling `drop_chunks`; app-maintained rollup
    tables; or Timescale Cloud on Azure if analytical load justifies it.
- Continuous aggregates lag by their refresh policy, so real-time aggregation is enabled
  (`materialized_only = false`), and the newest buckets are computed on read.
- The primary key `(meter_id, time)` serves per-meter range scans and makes inserts idempotent
  (ADR-0004). TimescaleDB's default `time DESC` index serves fleet-wide range scans.
