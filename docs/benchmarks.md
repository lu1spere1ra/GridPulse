# Benchmarks

All numbers come from BenchmarkDotNet (`benchmarks/GridPulse.Benchmarks`) unless stated otherwise.

**Machine:** 4 vCPU Intel Xeon @ 2.10 GHz (cloud VM), 16 GB RAM, Ubuntu 24.04, .NET 10.0.14, x64 RyuJIT.
Shared cloud VMs are noisy. Treat differences under ~10 % as noise, and compare ratios rather
than absolute values.

Reproduce:

```bash
cd benchmarks/GridPulse.Benchmarks
dotnet run -c Release -- --filter '*'              # full run
dotnet run -c Release -- --filter '*' --job short  # quick check
```

## 1. In-memory pipeline (Phase 2)

| Benchmark | Mean per reading | Allocated per reading | Implied capacity |
|---|---:|---:|---:|
| `PayloadParsing.Reflection` (System.Text.Json, reflection metadata) | 547 ns | 272 B | — |
| `PayloadParsing.SourceGenerated` | 546 ns | 272 B | — |
| `Processor.ProcessOneSecondOfFleet` (ordering guard + 1m/15m windows + live state, steady state) | 29 ns | 0 B | ~34 M readings/s on one thread |
| `PipelineThroughput.SubmitAndDrain` (validate → channel → processor → channel → batching, no-op writer) | 755 ns | 197 B | ~1.3 M readings/s |

What this tells us:

- **The processor is not the bottleneck.** At 5,000 meters × 1 Hz it is busy ~0.015 % of the
  time. This is why the design keeps a single processor thread with no partitioning (ADR-0002).
- **Source generation did not make deserialization faster.** Once System.Text.Json has cached its
  reflection metadata, both paths use the same converters. Source generation pays off at startup
  and under Native AOT, not in steady-state parsing. Parsing is the most expensive in-memory
  step, which makes it the first optimisation target (section 3).
- **The first processor benchmark was wrong.** It used `[IterationSetup]` and measured dictionary
  growth from empty: 308 ns and 799 B per reading. Warming up once in `[GlobalSetup]` gives the
  steady-state number above. The lesson: benchmark the state the system actually runs in.

## 2. Database writers (Phase 3)

`WriterBenchmarks` writes batches into the `readings` hypertable of a dedicated `gridpulse_bench`
database. The TimescaleDB container runs on the **same 4-vCPU VM** as the benchmark, so client
and server compete for CPU.

| Writer | Idempotent | 1,000 rows | 5,000 rows | Rows/s (5,000-row batches) | Allocated (5,000 rows) |
|---|:-:|---:|---:|---:|---:|
| EF Core `AddRange` + `SaveChanges` (baseline) | ❌ | 118.4 ms | 555.8 ms | ~9,000 | 39.5 MB |
| Dapper `INSERT … SELECT unnest(@arrays) ON CONFLICT DO NOTHING` | ✅ | 20.2 ms | 95.8 ms | ~52,000 | 416 KB |
| Binary `COPY` → temp staging table → `INSERT … ON CONFLICT DO NOTHING` | ✅ | 22.4 ms | 90.7 ms | ~55,000 | 474 KB |
| Raw binary `COPY` straight into the hypertable | ❌ | 5.2 ms | 20.8 ms | ~240,000 | 472 KB |

What this tells us:

- **EF Core is the wrong tool for bulk telemetry.** It is about 6× slower than the alternatives and
  allocates about 100× more: it materialises an entity per row, tracks it and generates parameterised
  multi-row INSERTs. It is still the right tool for CRUD and migrations (ADR-0004).
- **The cost of idempotency is the `ON CONFLICT` insert path, not the network.** The `unnest` and
  staging variants take almost the same time, even though one sends parameters and the other
  streams binary COPY data. Raw COPY into the hypertable is about 4× faster because it uses
  TimescaleDB's multi-row COPY path and does no per-row conflict handling.
- **Next step:** make the common case (no duplicates) take the raw-COPY path, and fall back to the
  staging merge only when a duplicate actually shows up. Measured in section 3.

## 3. End-to-end load and optimisation rounds (Phase 9)

`tools/loadtest/run.sh` restarts the simulator at a given rate against the running compose stack,
waits for a warm-up, and reads the numbers from Prometheus:

- **Throughput:** readings/s published and persisted.
- **Latency:** p50/p95/p99 from the reading's own timestamp to the database commit.
- **Peak queued readings** in the pipeline channels.
- **Average batch size.**
- **CPU** of each service.

Everything (broker, database, five .NET services, Prometheus, Grafana) runs on the same 4-vCPU VM.

```bash
GRIDPULSE_METERS=5000 docker compose up -d
tools/loadtest/run.sh 5000 00:00:01                      # 5,000 readings/s
CONNECTIONS=4 tools/loadtest/run.sh 5000 00:00:00.2      # 25,000 readings/s
```

### Results after the optimisations below

| Offered load | Published/s | Persisted/s | p50 | p95 | p99 | Peak queued | Ingestion CPU |
|---|---:|---:|---:|---:|---:|---:|---:|
| 5,000 meters × 1 Hz (**the requirement**) | 4,990 | 4,986 | 180 ms | 378 ms | 396 ms | 0 | 0.22 cores |
| 5,000 meters × 5 Hz | 24,602 | **24,565** | 165 ms | 313 ms | 383 ms | 898 | 1.03 cores |
| 5,000 meters × 10 Hz (overload) | 39,332 | 27,765 | 7.5 s | 9.8 s | 9.9 s | 2,323 | 1.10 cores |

- **Sustained capacity on one 4-vCPU VM:** about **25,000 readings/s** (5× the requirement) with
  p99 under 400 ms.
- **At 5,000 readings/s, the median latency of ~180 ms is mostly the 250 ms flush interval.**
  Batches fill to ~1,500 rows and are flushed by time, not by size. A shorter interval lowers
  latency at the cost of more, smaller transactions.
- **Overload behaves as designed.** Persisted throughput stays flat, the backlog waits in the
  broker (bounded by MQTT Receive Maximum) rather than in ingestion's memory, and latency grows
  instead of the process crashing.

### Round 0 — a data bug found by load testing

The first 5,000/s run persisted only 4,504 of 4,991 readings/s.

- **Cause.** The metrics showed `rejected{reason="RegisterRegression"}` at 471/s. Restarting the
  simulator re-created meters with their initial energy register, which was lower than what
  ingestion had already stored. Ingestion was right to reject a register that goes backwards.
- **Fix, in the simulator.** Registers are now anchored to wall-clock time at each meter's maximum
  possible power, so a restart continues the register as a real meter would. A unit test covers it.
- **After the fix:** persisted = published.

### Round 1 — "one MQTT connection is the limit" (wrong hypothesis)

Offering 25k and 50k readings/s, the simulator never published more than ~12,000/s, while every
container had idle CPU.

- **First guess:** the single MQTT connection. Spreading meters over 8 connections made it
  *worse* (9,600/s). Hypothesis rejected.
- **Instrumentation.** A new `gridpulse.simulator.slice.duration` histogram showed every
  500-message slice took ~60 ms to be acknowledged, against a 10 ms budget.
- **Isolation.** Stopping the only subscriber (ingestion) changed nothing, so the limit was
  between simulator and broker.

### Round 2 — Nagle's algorithm on the broker

~60 ms per round trip with idle CPUs is the signature of Nagle's algorithm interacting with
delayed ACKs. Mosquitto's `set_tcp_nodelay` defaults to `false`, so its tiny PUBACK packets were
being held back.

| Mosquitto | Published/s (1 connection) | Slice p50 |
|---|---:|---:|
| default (`set_tcp_nodelay false`) | 12,394 | 60 ms |
| `set_tcp_nodelay true` | **28,054** | **18 ms** |

One configuration line gave 2.3× throughput.

### Round 3 — the writer becomes the bottleneck → optimistic COPY

At ~39,000 offered readings/s, ingestion persisted ~29,600/s.

- Every batch was full (4,998 rows), the database was at 45 % CPU, and up to 9,400 readings waited
  in the channels: the writer was the bottleneck.
- **Change:** the writer now does a raw `COPY` straight into the hypertable. Only if the primary key
  rejects the batch (SQLSTATE 23505, a QoS 1 redelivery) does it roll back and retry the same batch
  through the staging merge, so the result is identical.

| Writer, 5,000-row batch (BenchmarkDotNet) | Mean | Rows/s |
|---|---:|---:|
| COPY → staging → `ON CONFLICT` (before) | 86.0 ms | ~58,000 |
| **Optimistic COPY, staging only on duplicate (after)** | **25.9 ms** | **~193,000** |
| Raw COPY (reference, not idempotent) | 23.7 ms | ~211,000 |

End to end at the same offered load:

- database CPU fell from 45 % to 20 %;
- peak queued readings fell from 9,412 to 2,323;
- **persisted throughput did not rise.**

The bottleneck moved upstream: Mosquitto was at ~97 % of its single core, relaying about 67,000
messages/s (in + out).

### What was deliberately not optimised

- **Payload parsing** (546 ns/reading, section 1). At 25,000 readings/s it costs about 1.4 % of a
  core. A zero-allocation `Utf8JsonReader` parser would be faster, but it would not move any number
  that matters here.
- **The broker ceiling.** The next step is not code. It would be:
  - a multi-threaded or clustered broker (EMQX, HiveMQ) or Azure Event Grid's MQTT broker, with
    shared subscriptions;
  - key-affine partitioning across several ingestion replicas (ADR-0002).


