# ADR-0006: Live fan-out over MQTT live topics to SignalR, without a backplane

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

Dashboards need second-by-second data: fleet totals, the latest value of the meter being viewed,
closed windows and alerts. That data is produced inside the ingestion service, while browsers
connect to the API, which hosts the SignalR hub. The API may run more than one replica.

## Options considered

1. **Ingestion hosts the hub.** No hop, but it couples a CPU-bound single-replica worker to
   browser connections, and the ingestion replica count is fixed at 1 (ADR-0002).
2. **The API polls the database.** Simple, but it adds query load every second and latency equal
   to the poll interval.
3. **Ingestion → SignalR backplane (Redis / Azure SignalR Service) → API.** This is the standard
   scale-out answer, but it adds a new service.
4. **Ingestion publishes compact live messages to the MQTT broker we already run; each API replica
   subscribes and pushes to its own clients.**

## Decision

Option 4.

- **What ingestion publishes.** Once per second, ingestion publishes to `gridpulse/live/*`:
  - a fleet snapshot;
  - one batch with the latest value of every meter that reported;
  - the windows that closed.

  Alerts are published as they happen. These are snapshots, so they use QoS 0; alerts use QoS 1.
- **How the API consumes them.** Every API replica holds its own *non-shared* subscription to
  `gridpulse/live/#`, so every replica receives every live message.
- **Who receives what.**
  - Fleet snapshots and alerts go to all clients.
  - Per-meter updates go only to the SignalR group `meter:{id}`, and only for meters that some
    client is watching (`MeterSubscriptions`). The per-meter firehose (5,000 values per second)
    never reaches browsers that are not looking at it.
- **The volume is small** (a few KB/s at 5,000 meters), so re-publishing through the broker is cheap.

## Consequences

- **No backplane is needed.** Each replica already has all the data its own clients need. Scaling
  the API out only needs sticky sessions for SignalR's negotiate → connect handshake, which
  Container Apps and Kubernetes ingresses support.
- **Live data is best effort by design.** A lost snapshot is replaced by the next one a second
  later. Alerts are durable anyway (they are stored, and the dashboard reloads them over HTTP).
- **One more hop** (ingestion → broker → API) adds a few milliseconds, which is negligible next to
  the 1-second tick.
