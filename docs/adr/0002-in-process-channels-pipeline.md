# ADR-0002: In-process System.Threading.Channels pipeline instead of an external queue

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

Between the MQTT broker and the database, readings must be validated, aggregated into 1- and
15-minute windows, checked against alert rules and written in batches. The target is 5,000
readings/s with headroom, from a single ingestion service. We need to decouple the stages
(the database is slower and burstier than the network) without losing control of memory.

## Options considered

1. **External queue** (Azure Event Hubs, Kafka, Service Bus) between the broker and the processors.
   - ✅ Durable: a crash loses nothing; consumers can replay.
   - ✅ Partitions by key → horizontal scale-out with per-meter ordering.
   - ❌ Another managed service to provision, pay for, secure and operate; another network hop.
   - ❌ An MQTT→queue bridge is still needed in front of it.
2. **In-process `System.Threading.Channels`** between stages inside one service.
   - ✅ Zero infrastructure, microsecond hand-offs, trivial to test with fakes.
   - ✅ Bounded channels give backpressure for free.
   - ❌ Not durable: whatever is buffered in memory is lost if the process crashes.
   - ❌ Scales vertically only (see below).
3. **Dataflow (TPL Dataflow) / Rx** — more abstraction than three stages need.

## Decision

Three stages connected by **bounded** channels with `FullMode = Wait`:

```
MQTT handler → [input, 50k] → processor (single reader) → [writer, 50k] → batch writer
```

- **Backpressure chain.** The writer slows down, the writer channel fills, the processor waits,
  the input channel fills, and the MQTT handler's `await SubmitAsync` doesn't return.
  - MQTTnet only sends the PUBACK when the handler returns. The client advertises MQTT 5
    **Receive Maximum = 1,000**, so the broker stops sending after 1,000 unacknowledged messages
    and queues the rest itself.
  - Memory is bounded by the channel capacities, not by the load.
- **Single processor thread owns all state.** It holds the per-meter windows, the ordering guard
  and the live state, so there are no locks and no concurrent dictionaries.
  - Ticks (closing windows, live snapshots) travel through the same channel as readings, so state
    is only touched by that thread.
  - The processor costs well under a microsecond per reading (see `docs/benchmarks.md`), so one
    thread covers the target many times over.
- **Batching** flushes when a batch is full (5,000 rows) or 250 ms after its first row arrived,
  whichever comes first. This bounds latency at low load and maximises throughput at high load.
- **Graceful shutdown drains** instead of cancelling. The MQTT service stops first (hosted services
  stop in reverse order), the input channel is completed, and each stage finishes what it holds.

## Consequences

- **Delivery guarantee: at-least-once from devices to broker, at-most-once inside the service.**
  A reading is acknowledged once it is in the input channel. On a *crash* (not a graceful stop),
  up to `InputCapacity + WriterCapacity + BatchSize` readings can be lost.
  - This is the price of option 2, and it is acceptable for telemetry: the cumulative energy
    register means billing-relevant totals are still correct.
  - If that loss window became unacceptable, the fix is to put a durable log (option 1) in front
    of the pipeline, not to make the pipeline itself durable.
- **One ingestion replica.** MQTT shared subscriptions distribute messages round-robin, not by
  meter, so two replicas would split one meter's readings and corrupt its windows and ordering
  checks. The service therefore runs as exactly one replica and scales vertically.
  - The scale-out path is key-affine partitioning: either topic partitions
    (`gridpulse/p/{hash % N}/…` with static assignment) or Event Hubs/Kafka partitions keyed by meter.
- Redelivered duplicates (QoS 1) are dropped by the per-meter ordering guard and, across restarts,
  by the database primary key.
