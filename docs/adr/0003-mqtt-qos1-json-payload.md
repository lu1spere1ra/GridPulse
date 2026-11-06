# ADR-0003: MQTT 5 with QoS 1 and a JSON payload for meter telemetry

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

Meters publish one small reading per second (5,000 meters ⇒ 5,000 messages/s). Devices in the
field have unreliable links and constrained stacks. We need a transport that devices actually
speak, that tolerates short disconnects, and that is easy to inspect while developing.

## Options considered

1. **MQTT (Mosquitto)** — the de-facto IoT protocol, tiny header, persistent sessions, per-message QoS.
   Mosquitto is a single small container. No partitioning by key, no replay after delivery.
2. **HTTP POST per reading** — simple, but a TCP/TLS + HTTP round trip per reading is wasteful for
   devices and pushes buffering and retries onto every meter.
3. **AMQP / Kafka directly from devices** — heavy client libraries, not what meters ship with.

QoS levels:

- **QoS 0** (at most once): fastest, but readings are silently lost on reconnects.
- **QoS 1** (at least once): one PUBACK per message; duplicates are possible after a reconnect.
- **QoS 2** (exactly once): four packets per message, and still not end-to-end exactly-once once the
  message leaves the broker.

Payload: JSON vs a binary format (Protobuf/MessagePack/CBOR).

## Decision

- MQTT 5 on Mosquitto, topic `gridpulse/meters/{serial}/readings`, **QoS 1**.
- Duplicates are handled downstream, cheaply: a per-meter "timestamp must increase" guard in the
  pipeline, and `PRIMARY KEY (meter_id, time)` + `ON CONFLICT DO NOTHING` in the database. Together with QoS 1
  that gives *effectively-once* storage without QoS 2's extra round trips.
- **JSON** payload (~100 bytes), parsed with System.Text.Json source generation. It is readable with
  any MQTT tool, and its parsing cost is measured in the benchmarks rather than assumed.
- The simulator multiplexes all simulated meters over **one** MQTT connection and publishes a slice of the
  fleet every 100 ms (concurrently, so QoS 1 acknowledgements overlap). Real meters would each hold a
  connection. Mosquitto handles thousands of connections, but the simulation does not need to prove that.

## Consequences

- At-least-once delivery means every consumer must be idempotent. This is enforced in the pipeline and the schema.
- MQTT has no consumer groups by key; this limits ingestion scale-out (see ADR-0002).
- If payload parsing ever dominates the CPU profile, switching to a binary format is a contained
  change behind the `ReadingMessage` contract.
