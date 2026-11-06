# GridPulse — Implementation Plan

> Status: **DRAFT — awaiting approval.** No code is written until this plan is approved.
> Section 2 lists the decisions that need a yes/no before Phase 0 starts.

GridPulse is a real-time energy monitoring platform for (simulated) smart meters. It is a
portfolio project, so its goal is not only to work but to make every non-obvious decision
explicit, measured and defensible in a technical interview.

---

## 1. Goals, constraints and success criteria

### Goals

- Ingest meter readings over MQTT, validate them, aggregate them in 1-minute and 15-minute
  windows and persist them in batches to PostgreSQL + TimescaleDB.
- Show a live dashboard (Blazor + MudBlazor) fed by SignalR, with historical charts served by
  a versioned, rate-limited, output-cached Minimal API.
- Raise configurable alerts (threshold exceeded, meter offline > 5 min) via SignalR and email.
- Generate a daily CSV/PDF report with an Azure Function and store it in Blob Storage.
- Be deployable to Azure (Container Apps + PostgreSQL Flexible Server + Storage) with Bicep,
  with a Helm chart as the Kubernetes alternative.
- Be observable: OpenTelemetry metrics in Prometheus with a provisioned Grafana dashboard.

### Hard constraints (from the brief)

| Constraint | How it is enforced |
|---|---|
| .NET 10 (LTS) | `global.json` pins the SDK; all projects target `net10.0` |
| `docker compose up` starts everything | Single root `compose.yaml`; migrations run as a one-shot service |
| Host ports only in **5300–5399** | Port map in §6; CI check that greps `compose.yaml` for out-of-range host ports |
| No MediatR, AutoMapper, MassTransit, FluentAssertions | Explicit mapping/handlers; Shouldly for assertions; CI check on `Directory.Packages.props` |
| xUnit + Shouldly, FakeTimeProvider, Testcontainers, bUnit, BenchmarkDotNet | See §8 |
| English everywhere; Conventional Commits; small commits | Commit per logical step (`feat(ingestion): …`, `test(domain): …`, `docs(adr): …`) |
| Deploy workflow ready but **not executed** | `workflow_dispatch` only, OIDC, documented in `docs/deploy-azure.md` |
| Green build + tests at the end of every phase | Each phase ends with `dotnet build -warnaserror` + `dotnet test`, pushed only when green |

### Success criteria

1. `docker compose up` on a clean machine brings up the whole stack, and the dashboard shows
   live data within ~30 s.
2. A sustained-throughput figure (readings/s) and ingestion latency percentiles (p50/p95/p99)
   are measured, reproducible with a script, and published in the README, with the hardware stated.
3. Every optimisation in the ingestion pipeline has a BenchmarkDotNet before/after table.
4. Each major decision has an ADR that states the context, the options considered, the
   decision and its consequences, including the downsides.

### Non-goals (stated so they can be defended, not forgotten)

- **Authentication/authorisation.** Public demo endpoints; the ADR on Azure hosting describes the
  production path (Entra ID + Container Apps built-in auth, managed identities for services).
- **Exactly-once delivery.** At-least-once plus idempotent writes (see ADR-0003).
- **Real meter protocols** (DLMS/COSEM, M-Bus), billing and tariffs.
- **HA MQTT broker** and multi-region.
- **.NET Aspire AppHost.** The brief requires Docker Compose. A `ServiceDefaults` project
  borrows Aspire's conventions (OTel, health checks, resilience) without the orchestrator.

---

## 2. Decisions that need your approval

Each item has a recommendation. I will proceed with the recommendation unless you say otherwise.

### D1 — TimescaleDB on Azure PostgreSQL Flexible Server ⚠️ (the important one)

**Problem.** Azure Database for PostgreSQL Flexible Server ships only the **Apache-2 edition** of
TimescaleDB. Continuous aggregates, native compression and the background policy jobs are
**Timescale License (TSL)** features and are therefore **not available on Azure**. The brief
asks for continuous aggregates *and* for Flexible Server. As written, the two conflict.

**Options:**

- **A. Edition-aware schema (recommended).** The migrator detects the edition
  (`SHOW timescaledb.license`):
  - *TSL edition* (local Docker, Timescale Cloud): hierarchical continuous aggregates
    (`readings_1m` → `readings_15m` → `readings_1h`), compression and retention policies.
  - *Apache edition* (Azure Flexible Server): the same relation names exist as plain rollup tables,
    refreshed incrementally by `pg_cron` (which Flexible Server supports) with an idempotent
    `INSERT … ON CONFLICT DO UPDATE` over a sliding window. Retention runs through `drop_chunks`,
    which is an Apache feature.
  - Query code (Dapper) is identical for both, because it only reads `readings_1m/15m/1h`.
  - **Both modes are tested.** The integration-test matrix runs the TimescaleDB container a second
    time with `timescaledb.license=apache`, which emulates Azure locally.
  - *Cost:* one extra SQL migration branch and a test matrix. *Interview value:* high, because it
    shows that I read the provider's limitations before shipping.
- **B. Timescale Cloud (Tiger Cloud) in Azure instead of Flexible Server.** All features, but it
  isn't provisionable from Bicep (Bicep would just receive a connection string), and it drops
  "PostgreSQL Flexible Server" from the brief.
- **C. TimescaleDB as a container in Container Apps.** Rejected: a stateful database on
  Azure Files volumes in ACA is a known anti-pattern (fsync/locking, no backups, no PITR).

### D2 — Hot-path writes: EF Core vs Npgsql binary COPY

The brief says "EF Core for writes/CRUD". I propose:

- **EF Core** for CRUD (meters, alert rules, alerts) and for **all migrations**.
- **Dapper** for heavy analytical reads.
- **Npgsql binary `COPY`** for the raw readings hot path.

The ingestion writer is built behind an `IReadingBatchWriter` interface. **EF Core is the first
implementation (the baseline)**, then it is replaced step by step, with each step benchmarked:

1. EF Core `AddRange` + `SaveChanges`
2. Dapper multi-row `INSERT … SELECT unnest(@arrays)`
3. Binary `COPY` into a temp table + `INSERT … ON CONFLICT DO NOTHING` (idempotent)
4. Raw binary `COPY` (fastest, but not idempotent; kept only as the reference point)

The README shows the numbers for all four, so the deviation from "EF Core for writes" is
evidence-based. ADR-0004 explains why.

### D3 — Blazor render mode: Interactive Server (recommended) vs WebAssembly

- **Interactive Server.** The dashboard is an operator tool with few concurrent users. The
  SignalR client to the API hub lives server-side, so the API needs no CORS and no tokens in the
  browser. bUnit testing is simpler.
- **WebAssembly** is the right call for many public viewers. ADR-0010 records when to switch.

### D4 — Kubernetes alternative: Helm chart (recommended) vs raw manifests

A Helm chart (`deploy/helm/gridpulse`) with values for replica counts and simulator size,
validated in CI with `helm lint` + `helm template | kubeconform`. Raw manifests would duplicate
the per-environment settings.

### D5 — Phase order: observability before IaC

The brief's order is "… alerts, Function, IaC, observability". I propose doing
**observability before IaC**, for two reasons:

- the performance phase needs Prometheus to measure sustained readings/s;
- the Bicep can then wire the Container Apps managed OpenTelemetry agent knowing exactly what the
  services emit.

The custom `Meter` instruments are created from Phase 2 onwards anyway (they cost nothing). Only
the Prometheus/Grafana stack moves.

### D6 — Ingestion runs as a single replica (by design, documented)

Window aggregation keeps per-meter state in memory. MQTT shared subscriptions distribute messages
round-robin, **not by key**, so two replicas would split one meter's readings and corrupt its
windows.

- The ingestion service therefore runs as exactly one replica, scaled vertically with internal
  partitioning (§4.2).
- ADR-0006 documents the scale-out path: key-affine partitioning, either with topic-level
  partitions (`gridpulse/p/{n}/…`) with static assignment, or by moving to Event Hubs/Kafka
  partitions.
- The README throughput number shows how much headroom one replica has.

---

## 3. Architecture

```mermaid
flowchart LR
    subgraph Devices
        SIM[Simulator<br/>Worker Service<br/>N ≤ 5,000 meters]
    end
    SIM -- "MQTT 5, QoS 1<br/>gridpulse/meters/{id}/readings" --> MQ[(Mosquitto)]
    MQ --> ING

    subgraph ING[Ingestion Worker]
        direction LR
        RX[MQTT receiver<br/>parse + validate] --> P[[Partition channels<br/>hash(meter) % P]]
        P --> AGG[Window aggregators<br/>1m / 15m, event time]
        P --> WCH[[Writer channel]] --> BW[Batch writer<br/>binary COPY]
        AGG --> AL[Alert evaluator<br/>threshold / offline]
    end

    BW --> DB[(PostgreSQL + TimescaleDB<br/>hypertable + continuous aggregates)]
    AL --> DB
    AL -- SMTP --> MP[Mailpit]
    AGG -- "live topics<br/>gridpulse/live/…" --> MQ
    AL -- "gridpulse/live/alerts" --> MQ

    MQ -- live topics --> API
    subgraph API[API — Minimal APIs]
        REST[/api/v1 …<br/>versioning · rate limiting · output cache/]
        HUB[SignalR hub<br/>/hubs/telemetry]
    end
    API -- "EF Core (CRUD)<br/>Dapper (analytics)" --> DB
    WEB[Blazor Web App<br/>MudBlazor] -- HTTP + SignalR --> API

    FN[Azure Function<br/>timer: daily report] -- Dapper --> DB
    FN -- CSV + PDF --> BLOB[(Blob Storage<br/>Azurite locally)]

    ING & API & SIM & WEB -. OTLP metrics .-> PROM[(Prometheus)] --> GRAF[Grafana]
```

### Solution layout

```
GridPulse.slnx
global.json · Directory.Build.props · Directory.Packages.props (CPM) · .editorconfig · compose.yaml
src/
  GridPulse.Domain/            Pure logic: readings, validation, window aggregation, alert rules. No I/O.
  GridPulse.Contracts/         MQTT payloads + System.Text.Json source-gen context, SignalR DTOs + typed hub client interface
  GridPulse.Infrastructure/    EF Core DbContext + migrations, Dapper queries, COPY writer, MQTT client factory
  GridPulse.ServiceDefaults/   OpenTelemetry, health checks, resilience, shared hosting conventions
  GridPulse.Migrator/          One-shot: applies migrations (edition-aware), seeds default alert rules
  GridPulse.Simulator/         Worker Service
  GridPulse.Ingestion/         Worker Service (Channels pipeline, alerting, email)
  GridPulse.Api/               Minimal APIs + SignalR hub
  GridPulse.Web/               Blazor Web App (Interactive Server) + MudBlazor
  GridPulse.Reports/           Azure Functions isolated worker (timer + manual HTTP trigger)
tests/
  GridPulse.Domain.Tests/      xUnit + Shouldly + FakeTimeProvider
  GridPulse.Ingestion.Tests/   Pipeline tests with in-memory fakes (backpressure, shutdown drain)
  GridPulse.IntegrationTests/  Testcontainers: TimescaleDB (TSL + Apache modes), Mosquitto, API via WebApplicationFactory
  GridPulse.Web.Tests/         bUnit
  GridPulse.Reports.Tests/     CSV/PDF generation, blob naming, idempotency
  GridPulse.ArchitectureTests/ Dependency rules (Domain references nothing; no banned packages)
benchmarks/
  GridPulse.Benchmarks/        BenchmarkDotNet: parsing, aggregation, channels, batch writers
deploy/
  mosquitto/ prometheus/ grafana/ (provisioning + dashboard JSON)
  bicep/                       main.bicep + modules
  helm/gridpulse/
tools/
  loadtest/                    Script: runs the stack at a given load, samples Prometheus, prints the summary table
docs/
  PLAN.md  architecture.md  benchmarks.md  deploy-azure.md  adr/  images/
```

Dependency rule, enforced by the architecture tests:

- `Domain` depends on nothing.
- `Contracts` depends on nothing.
- `Infrastructure` → `Domain`, `Contracts`.
- The hosts (`Simulator`, `Ingestion`, `Api`, `Web`, `Reports`, `Migrator`) → `Infrastructure`
  and/or `Contracts` + `ServiceDefaults`.
- `Web` talks to the API only over HTTP/SignalR. It does **not** reference `Infrastructure`.

---

## 4. Component design

### 4.1 Simulator (Worker Service)

- **Configuration:** `MeterCount` (1–5,000), `PublishInterval` (default 1 s), `Seed`,
  `AnomalyRate`, `OfflineRate`, `ConnectionCount`.
- **Load model:** a residential base load plus a daily curve with morning and evening peaks, noise,
  and appliance spikes. The cumulative energy register (Wh) is monotonic. Generation is
  deterministic from the seed, so runs are reproducible.
- **Injected faults (to exercise alerts):**
  - a configurable share of meters spike above the threshold;
  - a share go silent for 6–10 minutes;
  - a small rate of malformed payloads, to exercise validation.
- **Efficient scheduling:**
  - One `PeriodicTimer` built on `TimeProvider`, not 5,000 timers.
  - Meters are spread evenly over the interval in slices (no thundering herd).
  - Publishing goes through a small pool of MQTT connections (default 8) instead of 5,000 TCP
    connections. ADR-0003 explains this as a simulation shortcut.
- **Provisioning:** on startup it registers its meters through the API
  (`POST /api/v1/meters/batch`, idempotent). Provisioning stays separate from telemetry, and the
  ingestion hot path stays strict (unknown meters are rejected and counted).
- **Payload:** JSON, UTF-8, MQTT 5, QoS 1:

  ```json
  {"meterId":"MTR-000123","ts":"2026-11-06T10:15:00.000Z","powerW":1234.5,"energyWh":5532100,"voltageV":231.2}
  ```

### 4.2 Ingestion (Worker Service): pipeline built on System.Threading.Channels

```
MQTT handler ──▶ parse (Utf8JsonReader, zero-copy) ──▶ stateless validation
  ──▶ partition channel[hash(meterId) % P]   (bounded, SingleReader, FullMode=Wait)
        └─ partition worker (single-threaded, owns its meters' state, no locks)
             ├─ stateful validation: duplicates / out-of-order (ts ≤ last ts), energy register regression
             ├─ WindowAggregator 1m + 15m (event time, allowed lateness)
             ├─ OfflineDetector (last-seen per meter)
             └─▶ writer channel (bounded) ──▶ BatchWriter(s): flush on 5,000 rows OR 250 ms
                  (closed windows / alert transitions ──▶ alert + live channels)
```

**Key properties, each of which becomes an ADR or a test:**

- **Backpressure, end to end.**
  - Bounded channels with `FullMode.Wait`: the MQTT handler awaits `WriteAsync`.
  - MQTTnet stops reading the socket, TCP backpressure reaches Mosquitto, and Mosquitto queues
    up to `max_queued_messages`.
  - Queue depth per stage is exported as a gauge.
- **Delivery guarantee.**
  - Messages are acknowledged once they are enqueued. A crash can lose what is in the in-memory
    buffers.
  - The loss window is bounded (channel capacity) and is documented in ADR-0002 as the price of
    not running Kafka/Event Hubs.
  - Graceful shutdown drains every channel within `HostOptions.ShutdownTimeout`.
- **Idempotency.** `PRIMARY KEY (meter_id, time)` plus `ON CONFLICT DO NOTHING` absorb QoS 1
  redeliveries across reconnects.
- **Time.**
  - Windows are aligned to UTC epoch boundaries, exactly like `time_bucket`, so the stream results
    match the database results.
  - Windows close when `TimeProvider` time passes `windowEnd + allowedLateness` (default 5 s).
    Readings that arrive later are **still persisted** (the database is the source of truth)
    and are counted as `late`, but they are excluded from the live view.
- **Lock-free state.**
  - Each partition owns a `Dictionary<int, MeterState>` of `struct` state, updated in place through
    `CollectionsMarshal.GetValueRefOrAddDefault`.
  - Ticks (window close, offline check) travel through the same channel as readings, so all state
    is touched by a single thread.
- **Writer resilience.**
  - Transient Npgsql errors (`NpgsqlException.IsTransient`) are retried with exponential backoff
    (Microsoft.Extensions.Resilience).
  - While the database is down, the channels fill and backpressure kicks in, and
    `/health/ready` turns unhealthy.
  - Non-transient batch errors are logged and counted, and the batch is dropped.
- **Live fan-out.**
  - Ingestion publishes the fleet snapshot (1 Hz), per-meter closed windows and alert transitions
    to `gridpulse/live/…`.
  - For per-meter raw points, the API subscribes **dynamically** to
    `gridpulse/meters/{id}/readings` only while some browser is watching that meter.
  - This avoids re-publishing 5k msg/s. See ADR-0007.

### 4.3 Database (PostgreSQL 17 + TimescaleDB, image tag pinned)

| Relation | Kind | Notes |
|---|---|---|
| `meters` | table (EF Core) | `id int identity`, `serial text unique`, name, zone, created_at |
| `readings` | **hypertable** on `time` | `(time timestamptz, meter_id int, power_w real, energy_wh bigint, voltage_v real)`, PK `(meter_id, time)`, 1-hour chunks |
| `readings_1m` → `readings_15m` → `readings_1h` | **hierarchical continuous aggregates** (TSL) / rollup tables + pg_cron (Apache), see D1 | avg/min/max power, energy delta (`last(energy) - first(energy)`), sample count |
| `alert_rules`, `alerts` | tables (EF Core) | alert state machine `Firing → Resolved`, `acknowledged_at` |

- **Compression** (TSL only): `segmentby = meter_id`, `orderby = time DESC`, after 1 day.
- **Retention:** 7 days of raw data locally (configurable). Rollups are kept longer.
- **Migrations:**
  - EF Core migrations; the TimescaleDB DDL goes in `migrationBuilder.Sql(…)`.
  - Continuous aggregates use `suppressTransaction: true`, because `CREATE MATERIALIZED VIEW …
    WITH (timescaledb.continuous)` cannot run inside a transaction. This is a well-known pitfall,
    and a test covers it.
- **Real-time aggregation:** enabled explicitly (`materialized_only = false`) on `readings_1m` so
  the newest bucket is visible. The trade-off is documented.

### 4.4 API (Minimal APIs)

- **Endpoint groups** per feature (`MeterEndpoints`, `ReadingEndpoints`, `AlertEndpoints`,
  `AlertRuleEndpoints`), each in its own static class with `MapXxx(this IEndpointRouteBuilder)`.
  Handlers are static methods that use `TypedResults`, so they are unit-testable and give precise
  OpenAPI metadata.
- **Versioning:** `Asp.Versioning.Http` (10.x), URL segment `/api/v{version}`, with `v1` reported
  and deprecation headers wired up.
- **Validation:** the built-in Minimal API validation in .NET 10 (`AddValidation()`), with RFC 9457
  Problem Details for every error.
- **Rate limiting:**
  - built-in, partitioned by client IP (`ForwardedHeaders` configured for the ACA ingress);
  - a token bucket for writes and a fixed window for reads;
  - `429` responses carry `Retry-After`.
- **Output caching:**
  - Historical range queries are cached, with the cache key varying by the query parameters.
  - Queries whose `to` is within the last minute are **not** cached (custom policy).
  - Cache entries are tagged per meter (`meter:{id}`) and evicted on update.
  - The cache is in-memory per replica. ADR-0011 discusses the Redis output-cache store for scale-out.
- **Resolution selection:** `GET /api/v1/meters/{id}/readings?from&to&resolution=auto|raw|1m|15m|1h`
  picks the coarsest resolution that keeps the result at ≤ 1,000 points.
- **Analytics (Dapper):** fleet consumption summary, top-N consumers in a range, and a load
  profile by hour of day.
- **SignalR hub `/hubs/telemetry`:**
  - strongly typed (`Hub<ITelemetryClient>`);
  - groups per meter, a fleet group and an alerts group;
  - fed by a hosted service that subscribes to the `gridpulse/live/#` MQTT topics.
  - Every API replica receives every live message, so broadcast works **without a backplane**;
    only sticky sessions are needed (ADR-0007).
- **OpenAPI:** the built-in `Microsoft.AspNetCore.OpenApi` document, with Scalar UI in Development.
- **Health:** `/health/live` and `/health/ready` (database, MQTT).

### 4.5 Web (Blazor Web App, Interactive Server, MudBlazor)

| Page | Content |
|---|---|
| **Dashboard** | KPI cards (fleet kW, readings/s, online/offline meters, active alerts), a live fleet chart over the last 15 min, and the live alert list |
| **Meters** | `MudDataGrid` with server-side paging and filtering |
| **Meter detail** | Live chart (raw points), historical chart with a resolution picker, and the meter's alerts |
| **Alerts** | Active alerts and history, with acknowledge |
| **Alert rules** | CRUD form with validation |

- The live data comes through an `ITelemetryFeed` abstraction, a SignalR client with automatic
  reconnect. bUnit tests swap it for a fake.
- Each chart keeps a fixed-size ring buffer, so memory stays bounded however long the page is open.
- If MudBlazor's built-in time-series chart is not good enough at 1 Hz, the fallback is
  `Blazor-ApexCharts` (MIT). That choice would get an ADR.

### 4.6 Alerts

- **Rules** (stored in the database, CRUD through the API, reloaded by ingestion every 30 s):
  - **Threshold:** `avg | max` power of the closed 1-min window `>` limit, scoped to one meter or
    the whole fleet (with per-meter override).
  - **Offline:** no reading for `> 5 min` (configurable). Meters that never report after an
    ingestion restart are seeded with `lastSeen = startup time`, so they are detected too.
- **State machine:**
  - at most one open alert per `(rule, meter)`;
  - `Firing → Resolved` when the condition clears (the next window under the limit, or the next
    reading arrives);
  - hysteresis is configurable.
- **Pure evaluation logic in `Domain`:**
  - `AlertEvaluator.Evaluate(window, rules)` and `OfflineDetector.Check(now)`;
  - unit-tested with `FakeTimeProvider` (advance the clock 4:59 → no alert, 5:01 → alert, reading
    arrives → resolved).
- **Dispatch:** a channel feeds the dispatcher, which:
  - persists the alert (EF Core);
  - publishes it to `gridpulse/live/alerts`, which reaches SignalR;
  - sends email via **MailKit** to Mailpit, with a per-`(rule, meter)` cooldown to avoid mail storms.

### 4.7 Reports (Azure Functions, isolated worker, .NET 10)

- **Triggers:**
  - a timer trigger, `0 15 0 * * *` (00:15 UTC), that reports on the previous UTC day;
  - an HTTP trigger, `POST /api/reports/daily?date=yyyy-MM-dd` (function key), for manual runs
    and the demo.
- **Data:** Dapper over `readings_1h`, plus the alerts of the day.
- **CSV:** written by hand with `ReadOnlySpan`/`IBufferWriter`, invariant culture, RFC 4180
  quoting. No dependency is needed.
- **PDF:** **PDFsharp-MigraDoc** (MIT), with an embedded OFL font. **QuestPDF is not used**:
  its licence is commercial above a revenue threshold, which has the same issue as FluentAssertions.
- **Storage:**
  - `reports/daily/yyyy/MM/dd/gridpulse-daily-yyyy-MM-dd.{csv,pdf}` in Blob Storage (Azurite locally);
  - overwritten on re-run, so the function is idempotent;
  - in Azure, access is via managed identity with RBAC (no account keys).

### 4.8 Observability

- **OpenTelemetry SDK** in every service, via `ServiceDefaults`. The OTLP exporter sends to
  **Prometheus 3's native OTLP receiver** (no collector container needed locally), and Grafana is
  provisioned with Prometheus as its data source.
- **Custom metrics** (`Meter "GridPulse.Ingestion"`, `"GridPulse.Simulator"`):
  - `gridpulse.ingestion.readings.received` / `.persisted` / `.rejected{reason}` / `.late`
  - `gridpulse.ingestion.latency` (histogram, event time → DB commit, in seconds)
  - `gridpulse.ingestion.queue.depth{stage}` (observable gauge from `ChannelReader.Count`)
  - `gridpulse.ingestion.batch.size`, `gridpulse.ingestion.batch.duration`
  - `gridpulse.alerts.raised{type}`, `gridpulse.simulator.published`
- **Built-in instrumentation:** ASP.NET Core, HttpClient, .NET runtime (GC, thread pool), and
  Npgsql metrics.
- **Grafana dashboard JSON**, provisioned from `deploy/grafana/`:
  - throughput, latency percentiles, queue depth, batch size, rejections by reason;
  - alerts, API RPS/latency/429s, GC and thread pool.
- **Azure:** the Container Apps managed OpenTelemetry agent sends to Application Insights.
- **Stretch goal:** W3C trace context propagated through MQTT 5 user properties, giving one trace
  from simulator to DB commit.

---

## 5. Infrastructure as code

### Bicep (`deploy/bicep`)

| Module | Resources |
|---|---|
| `network.bicep` | VNet; a subnet delegated to `Microsoft.App/environments`; a subnet delegated to PostgreSQL; private DNS zone |
| `monitoring.bicep` | Log Analytics + Application Insights |
| `postgres.bicep` | Flexible Server (Burstable B1ms), private access, `azure.extensions` / `shared_preload_libraries` = `timescaledb,pg_cron` |
| `storage.bicep` | Storage Account + `reports` container, shared-key access disabled |
| `registry.bicep` | Azure Container Registry (Basic) |
| `identity.bicep` | User-assigned managed identity with role assignments: AcrPull, Storage Blob Data Contributor, Key Vault Secrets User |
| `keyvault.bicep` | DB password secret, referenced by Container Apps secrets |
| `containerapps.bicep` | Environment (VNet-integrated, managed OTel agent → App Insights), see the app list below |

Container apps:

- `mosquitto`: internal TCP ingress on 1883
- `ingestion`: no ingress, min = max = 1 (see D6)
- `api`: external HTTPS, sticky sessions
- `web`: external HTTPS
- `simulator`: small N, to keep cost low
- `reports`: Azure Functions on Container Apps
- `migrator`: Container Apps **Job**, triggered manually by the deploy workflow

**ADR-0008: Container Apps vs AKS vs App Service.**

- Container Apps gives scale-to-zero, built-in Dapr/KEDA/OTel, TCP ingress and Functions
  hosting, with no cluster operations.
- AKS is justified only with a platform team, custom operators or service-mesh needs.

### Helm (`deploy/helm/gridpulse`)

- One chart with Deployments/Services for each component and values for image tags, replicas and
  simulator size.
- TimescaleDB runs as a StatefulSet (dev clusters only; `values-prod.yaml` points to an external
  database).
- Validated in CI with `helm lint` + `helm template | kubeconform`.

### GitHub Actions

| Workflow | Trigger | Steps |
|---|---|---|
| `ci.yml` | push / PR | restore (locked) → build `-warnaserror` → unit + bUnit tests → integration tests (Testcontainers, Docker on the runner) → coverage summary → `bicep build` + `helm lint`/`kubeconform` → port-range and banned-package checks |
| `benchmarks.yml` | `workflow_dispatch` | in-memory benchmarks (`--job short`), results uploaded as an artifact (not on every push: shared runners are too noisy to compare) |
| `deploy-azure.yml` | **`workflow_dispatch` only** | `azure/login` with **OIDC** (federated credential, no secrets) → build + push images to ACR → `az deployment group what-if` → deploy → start the migrator job → smoke test `/health/ready`. Protected by a GitHub `production` environment. **Not executed.** `docs/deploy-azure.md` documents the one-time setup (app registration, federated credential, role assignments, repository variables). |

---

## 6. Local environment and port map (host ports 5300–5399 only)

| Host port | Service | Container port |
|---|---|---|
| 5300 | Web (Blazor) | 8080 |
| 5301 | API (+ Scalar UI at `/scalar`, SignalR hub) | 8080 |
| 5310 | PostgreSQL / TimescaleDB | 5432 |
| 5320 | Mosquitto (MQTT) | 1883 |
| 5330 | Prometheus | 9090 |
| 5331 | Grafana (anonymous viewer, dashboard pre-provisioned) | 3000 |
| 5340 | Mailpit web UI | 8025 |
| 5350 / 5351 / 5352 | Azurite blob / queue / table | 10000 / 10001 / 10002 |
| 5360 | Reports Function host | 80 |

- Ingestion, the simulator and the migrator expose **no** host ports. Mailpit's SMTP port stays
  internal.
- `launchSettings.json` profiles use the same ports, so `dotnet run` and Compose stay consistent.
- **Testcontainers maps random ephemeral host ports during tests.** These are outside 5300–5399 by
  design: the containers are short-lived and test-only, so they cannot collide with your other
  projects. Tell me if you want them pinned instead.
- Compose profiles:
  - default: the full stack with 500 meters at 1 Hz;
  - `GRIDPULSE_METERS=5000 docker compose up`: the 5,000-meter scenario;
  - `tools/loadtest`: the saturation runs.

---

## 7. Phases

Every phase ends with `dotnet build -warnaserror`, `dotnet test` (all green), and, where relevant,
`docker compose up` with healthy services. Then the phase's commits are pushed. ADRs are written in
the phase where the decision is made, not at the end.

| # | Phase | Main deliverables | Done when |
|---|---|---|---|
| **0** | **Foundation** | `global.json`, `GridPulse.slnx`, `Directory.Build.props` (nullable, analyzers, warnings as errors, deterministic builds), Central Package Management, `.editorconfig`, empty projects with dependency rules, `ci.yml`, `compose.yaml` skeleton (TimescaleDB, Mosquitto), ADR template + ADR-0001 (record architecture decisions) + ADR-0013 (library and licence policy) | CI green; architecture tests pass |
| **1** | **Simulator → MQTT** | Load model, fault injection, `TimeProvider`-driven scheduler, connection pool, meter provisioning client; unit tests (deterministic profiles, slice scheduling with `FakeTimeProvider`); ADR-0003 (MQTT, QoS 1, payload format) | Simulator publishes to Mosquitto in Compose; tests green |
| **2** | **Ingestion pipeline (in-memory)** | Zero-copy parser, validation, partitioned channels, `WindowAggregator` (1m/15m, event time, lateness), writer abstraction with an in-memory sink, graceful drain, `Meter` instruments; tests for aggregation edge cases (window boundaries, late, duplicate, out-of-order readings, register regression) and for backpressure; **first benchmarks** (parsing: reflection vs source-gen vs `Utf8JsonReader`; aggregation: string vs int keys, class vs struct state); ADR-0002 (Channels vs external queue), ADR-0005 (stream path vs database path), ADR-0006 (partitioning, single replica) | Pipeline sustains its target in in-memory benchmarks; tests green |
| **3** | **Persistence** | EF Core model + migrations, hypertable, continuous aggregates / rollups (**D1**), compression and retention, Migrator, the four batch writers (**D2**); Testcontainers tests: migrations in both editions, writer idempotency, and a **stream-vs-database consistency test** (in-memory aggregator output == `readings_1m` for the same data); **writer benchmarks** before/after; ADR-0015 (why TimescaleDB), ADR-0004 (write paths), ADR-0009 (TimescaleDB edition on Azure) | Data flows simulator → DB in Compose; integration tests green in both editions |
| **4** | **API + SignalR** | Endpoint groups, versioning, validation, Problem Details, rate limiting, output caching, Dapper analytics, OpenAPI + Scalar, health checks, SignalR hub + MQTT live bridge with dynamic subscriptions; integration tests (WebApplicationFactory + Testcontainers): versioning, 429s, cache hit/evict, hub messages; ADR-0007 (live fan-out), ADR-0011 (caching strategy) | `curl` and the Scalar UI work in Compose; tests green |
| **5** | **Web dashboard** | Layout, Dashboard, Meters, Meter detail, live and historical charts, `ITelemetryFeed`; bUnit tests (KPI cards, ring buffer, chart updates, reconnect banner); ADR-0010 (render mode, charts). **Milestone: end-to-end vertical slice working from `docker compose up`.** | Live chart moves in the browser (checked with Playwright); tests green |
| **6** | **Alerts** | Rule model + CRUD, evaluator + offline detector (`FakeTimeProvider` tests), state machine, dispatcher, MailKit → Mailpit with cooldown, SignalR alerts, Alerts and Alert rules pages, bUnit tests for the alert list and rule form | An injected spike and an injected offline meter raise and resolve alerts, visible in the UI and in Mailpit |
| **7** | **Reports Function** | Timer + HTTP triggers, CSV writer, PDF (MigraDoc), Blob upload, Azurite in Compose; tests (CSV format and culture, PDF smoke test, blob path, idempotency); ADR-0012 (Function vs ACA Job vs hosted service; PDF library choice) | A manual trigger writes a CSV and a PDF to Azurite; tests green |
| **8** | **Observability** (D5) | ServiceDefaults OTel wiring, Prometheus (OTLP receiver), provisioned Grafana dashboard, optional trace propagation through MQTT; ADR-0014 (observability stack) | Grafana shows live throughput and latency in Compose |
| **9** | **Performance** | `tools/loadtest` saturation runs, profiling (`dotnet-counters`, `dotnet-trace`), optimisation rounds, each with before/after BenchmarkDotNet tables and end-to-end readings/s + p99; `docs/benchmarks.md`, numbers in the README | Sustained-throughput figure published with method and hardware |
| **10** | **IaC + deployment** | Bicep modules (`bicep build` + linter clean), Helm chart (lint + kubeconform), `deploy-azure.yml` (OIDC, what-if, migrator job, smoke test), `docs/deploy-azure.md`; ADR-0008 (Container Apps vs AKS) | CI validates Bicep and Helm; the deploy workflow is reviewed but **not run** |
| **11** | **Polish** | README (pitch, Mermaid architecture, GIF/screenshots captured with Playwright, performance table, how to run, project tour), `docs/architecture.md`, ADR index review, final cleanup | A fresh clone + `docker compose up` works by following only the README |

**Performance targets** (hypotheses that Phase 9 will confirm or correct; the README will report
whatever is actually measured):

- **Requirement:** 5,000 meters × 1 Hz = 5,000 readings/s with p99 ingestion latency under 1 s.
- **Target headroom:** at least 10× that (≥ 50,000 readings/s) on one ingestion replica with 4 vCPUs.

### ADR index (written in the phase where each decision is made)

| ADR | Decision | Phase |
|---|---|---|
| 0001 | Record architecture decisions (template, MADR-style) | 0 |
| 0002 | In-process `System.Threading.Channels` pipeline vs an external queue (Event Hubs / Kafka / Service Bus) | 2 |
| 0003 | MQTT 5 + Mosquitto, QoS 1, JSON payload, simulator connection pooling | 1 |
| 0004 | Write paths: EF Core for CRUD, Dapper for analytics, binary COPY for the hot path | 3 |
| 0005 | Stream aggregation (latency) vs database aggregation (truth), event time, watermarks, `TimeProvider` | 2 |
| 0006 | Partitioned single-writer state; single ingestion replica and the scale-out path | 2 |
| 0007 | Live fan-out over MQTT live topics → SignalR without a backplane | 4 |
| 0008 | Azure Container Apps vs AKS vs App Service | 10 |
| 0009 | TimescaleDB edition strategy (TSL locally, Apache-2 on Azure) | 3 |
| 0010 | Blazor render mode and charting library | 5 |
| 0011 | Output caching and rate limiting strategy | 4 |
| 0012 | Daily report: Azure Function vs ACA Job vs hosted service; PDF library | 7 |
| 0013 | Third-party library and licence policy | 0 |
| 0014 | Observability stack (OTLP → Prometheus locally, Azure Monitor in the cloud) | 8 |
| 0015 | Why TimescaleDB (vs plain PostgreSQL partitioning, InfluxDB, Azure Data Explorer) | 3 |

---

## 8. Testing strategy

| Layer | Tooling | Focus |
|---|---|---|
| Unit | xUnit v3 + Shouldly + `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) | Window boundaries and lateness, aggregation maths, validation, alert state machine, offline timing, simulator load model, resolution selection, CSV formatting |
| Pipeline | xUnit + in-memory fakes | Backpressure (full channel blocks the producer), shutdown drain loses nothing, partition routing is stable |
| Integration | Testcontainers (TimescaleDB pinned, run in **both** TSL and Apache modes; Mosquitto as a generic container) + `WebApplicationFactory` | Migrations, hypertable/aggregates, idempotent writes, stream-vs-database consistency, Dapper queries, API behaviour (versioning, 429, caching), SignalR delivery, MQTT end to end |
| Component | bUnit (+ MudBlazor services, loose JSInterop) | Dashboard cards, alert list (severity, acknowledge), rule form validation, chart ring buffer |
| Architecture | NetArchTest.Rules (MIT) | Layer dependencies; no references to banned packages |
| Performance | BenchmarkDotNet (`[MemoryDiagnoser]`) + load-test script | Before/after per optimisation; end-to-end sustained throughput |

- **Test doubles:** hand-written fakes are preferred. Where a mock really helps, NSubstitute
  (BSD-3). Moq is avoided because of the SponsorLink incident (noted in ADR-0013).
- **Test containers:** shared through xUnit collection fixtures, so a container starts once per
  test assembly.

---

## 9. Package policy

| Purpose | Package | Licence |
|---|---|---|
| MQTT | MQTTnet 5.x | MIT |
| PostgreSQL | Npgsql, Npgsql.EntityFrameworkCore.PostgreSQL 10.x | PostgreSQL |
| Analytics | Dapper | Apache-2.0 |
| API versioning | Asp.Versioning.Http 10.x | MIT |
| OpenAPI UI | Scalar.AspNetCore | MIT |
| UI | MudBlazor 9.x | MIT |
| Email | MailKit | MIT |
| PDF | PDFsharp-MigraDoc 6.x | MIT |
| Blob | Azure.Storage.Blobs, Azure.Identity | MIT |
| Resilience | Microsoft.Extensions.Resilience (Polly v8) | MIT / BSD-3 |
| Telemetry | OpenTelemetry.* | Apache-2.0 |
| Tests | xunit.v3, Shouldly, bUnit, Testcontainers, NetArchTest.Rules, NSubstitute (if needed) | Apache-2.0 / BSD / MIT |
| Benchmarks | BenchmarkDotNet | MIT |

Excluded: **MediatR, AutoMapper, MassTransit, FluentAssertions** (brief); **QuestPDF**
(revenue-based commercial licence); **Moq** (see above). Exact versions are pinned in
`Directory.Packages.props`, and `packages.lock.json` files are committed (locked restore in CI).

---

## 10. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Azure ships TimescaleDB Apache-2 only (no continuous aggregates or compression) | D1 option A: edition-aware migrations, tested in both modes |
| The Functions .NET 10 isolated container image or tooling behaves differently from the docs | Verified at the start of Phase 7. Fallback: run the Functions host locally as a plain container built from the SDK image |
| MudBlazor's charts are too limited for 1 Hz time series | Fallback to Blazor-ApexCharts, decided in Phase 5 with an ADR |
| Benchmarks on shared CI runners are noisy | README numbers come from local runs with the hardware stated; CI only runs a smoke benchmark job on demand |
| The deploy can't be exercised end to end | `what-if` + `bicep build` + linter in CI; the setup guide is written step by step so a reviewer can run it |
| Build environment (this session) | .NET 10 SDK from Ubuntu `noble-updates`, Docker available. Azure CLI login is not available, so Bicep is validated offline and in CI |

---

## 11. Working agreement

- Branch: `claude/gridpulse-energy-monitoring-o9ml9j`. Small commits in Conventional Commits
  format. Pushed at the end of each green phase.
- I stop and report at the end of each phase with:
  - what was built;
  - the test results;
  - the numbers measured, where relevant;
  - anything that deviated from this plan.
- Any deviation from this plan or the brief gets an ADR or an explicit note in the phase report.
  There are no silent changes.
