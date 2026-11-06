# Architecture Decision Records

Each ADR records one decision: the context that forced it, the options that were considered,
what was chosen and what it costs. They are short on purpose.

| ADR | Decision |
|---|---|
| [0001](0001-timescaledb.md) | PostgreSQL + TimescaleDB, with an edition-aware schema (Azure ships the Apache-2 edition) |
| [0002](0002-in-process-channels-pipeline.md) | In-process `System.Threading.Channels` pipeline instead of an external queue; delivery guarantees; single ingestion replica |
| [0003](0003-mqtt-qos1-json-payload.md) | MQTT 5, QoS 1 and a JSON payload |
| [0004](0004-write-paths.md) | EF Core for CRUD, Dapper for analytics, binary COPY (optimistic, with an idempotent fallback) for telemetry |
| [0005](0005-container-apps.md) | Azure Container Apps rather than AKS or App Service |
| [0006](0006-live-fan-out.md) | Live fan-out over MQTT live topics to SignalR, without a backplane |
| [0007](0007-blazor-interactive-server.md) | Blazor Interactive Server and a hand-written SVG chart |
| [0008](0008-daily-report-function.md) | Daily report as an Azure Function; MIT-licensed PDF library (and why not QuestPDF or PDFsharp 6.5) |
| [0009](0009-stream-and-database-aggregation.md) | Aggregate twice: in the stream for latency, in the database for truth |

New ADRs start from the [template](0000-template.md).
