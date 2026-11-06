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

## 3. Optimisations (Phase 9)

_Pending._
