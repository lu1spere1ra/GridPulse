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

_Pending._

## 3. Optimisations (Phase 9)

_Pending._
