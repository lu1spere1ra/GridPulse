#!/usr/bin/env bash
# Sustained-throughput test against the running compose stack.
#
#   tools/loadtest/run.sh <meters> <publish-interval> [measure-seconds]
#   tools/loadtest/run.sh 5000 00:00:01        # 5,000 readings/s (the requirement)
#   CONNECTIONS=8 tools/loadtest/run.sh 5000 00:00:00.1   # 50,000 readings/s (needs several MQTT connections)
#
# Restarts only the simulator with the requested load, waits for a warm-up, then reads the
# numbers from Prometheus over the measurement window and prints a Markdown table row.
set -euo pipefail

METERS=${1:?meters}
INTERVAL=${2:?publish interval, e.g. 00:00:01}
MEASURE=${3:-120}
WARMUP=${WARMUP:-45}
PROM=${PROM:-http://localhost:5330}

cd "$(dirname "$0")/../.."
GRIDPULSE_METERS=$METERS GRIDPULSE_INTERVAL=$INTERVAL GRIDPULSE_CONNECTIONS=${CONNECTIONS:-1} docker compose up -d --no-build --force-recreate simulator >/dev/null 2>&1
echo "Load: $METERS meters every $INTERVAL over ${CONNECTIONS:-1} connection(s) — warming up ${WARMUP}s, measuring ${MEASURE}s" >&2
sleep "$WARMUP"
sleep "$MEASURE"

q() {
  curl -s --data-urlencode "query=$1" "$PROM/api/v1/query" |
    python3 -c "import sys,json; r=json.load(sys.stdin)['data']['result']; print(round(float(r[0]['value'][1]), ${2:-1}) if r else 'n/a')"
}

W="${MEASURE}s"
published=$(q "sum(rate(gridpulse_simulator_readings_published_total[$W]))")
persisted=$(q "sum(rate(gridpulse_ingestion_readings_persisted_total[$W]))")
p50=$(q "histogram_quantile(0.50, sum by (le) (rate(gridpulse_ingestion_latency_seconds_bucket[$W]))) * 1000" 0)
p95=$(q "histogram_quantile(0.95, sum by (le) (rate(gridpulse_ingestion_latency_seconds_bucket[$W]))) * 1000" 0)
p99=$(q "histogram_quantile(0.99, sum by (le) (rate(gridpulse_ingestion_latency_seconds_bucket[$W]))) * 1000" 0)
queue=$(q "max_over_time(sum(gridpulse_ingestion_queue_depth)[$W:5s])" 0)
batch=$(q "sum(rate(gridpulse_ingestion_batch_size_sum[$W])) / sum(rate(gridpulse_ingestion_batch_size_count[$W]))" 0)
cpu_ing=$(q "sum(rate(dotnet_process_cpu_time_seconds_total{job=\"GridPulse.Ingestion\"}[$W]))" 2)
cpu_sim=$(q "sum(rate(dotnet_process_cpu_time_seconds_total{job=\"GridPulse.Simulator\"}[$W]))" 2)

echo "| Meters | Interval | Published/s | Persisted/s | p50 ms | p95 ms | p99 ms | Max queued | Avg batch | Ingestion CPU (cores) | Simulator CPU (cores) |"
echo "|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"
echo "| $METERS | $INTERVAL | $published | $persisted | $p50 | $p95 | $p99 | $queue | $batch | $cpu_ing | $cpu_sim |"
