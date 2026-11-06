using System.Diagnostics.Metrics;
using GridPulse.Core.Readings;

namespace GridPulse.Ingestion.Pipeline;

/// <summary>Custom ingestion metrics, exported through OpenTelemetry (meter <c>GridPulse.Ingestion</c>).</summary>
public sealed class IngestionMetrics
{
    public const string MeterName = "GridPulse.Ingestion";

    private readonly Counter<long> _received;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _late;
    private readonly Counter<long> _persisted;
    private readonly Histogram<double> _latency;
    private readonly Histogram<int> _batchSize;
    private readonly Histogram<double> _batchDuration;
    private readonly Counter<long> _windowsClosed;
    private readonly Counter<long> _alertsRaised;

    public IngestionMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _received = meter.CreateCounter<long>("gridpulse.ingestion.readings.received", "{reading}");
        _rejected = meter.CreateCounter<long>("gridpulse.ingestion.readings.rejected", "{reading}");
        _late = meter.CreateCounter<long>("gridpulse.ingestion.readings.late", "{reading}");
        _persisted = meter.CreateCounter<long>("gridpulse.ingestion.readings.persisted", "{reading}");
        _latency = meter.CreateHistogram<double>(
            "gridpulse.ingestion.latency", "s", "Time from measurement (event time) to database commit");
        _batchSize = meter.CreateHistogram<int>("gridpulse.ingestion.batch.size", "{reading}");
        _batchDuration = meter.CreateHistogram<double>("gridpulse.ingestion.batch.duration", "s");
        _windowsClosed = meter.CreateCounter<long>("gridpulse.ingestion.windows.closed", "{window}");
        _alertsRaised = meter.CreateCounter<long>("gridpulse.alerts.raised", "{alert}");
        Meter = meter;
    }

    public Meter Meter { get; }

    public void Received() => _received.Add(1);

    public void Rejected(RejectReason reason) =>
        _rejected.Add(1, new KeyValuePair<string, object?>("reason", reason.ToString()));

    public void Late() => _late.Add(1);

    public void WindowsClosed(int count) => _windowsClosed.Add(count);

    public void AlertRaised(GridPulse.Core.Contracts.AlertRuleType type) =>
        _alertsRaised.Add(1, new KeyValuePair<string, object?>("type", type.ToString()));

    public void BatchPersisted(IReadOnlyList<Reading> batch, TimeSpan duration, DateTimeOffset committedAt)
    {
        _persisted.Add(batch.Count);
        _batchSize.Record(batch.Count);
        _batchDuration.Record(duration.TotalSeconds);
        foreach (var reading in batch)
        {
            _latency.Record((committedAt - reading.Timestamp).TotalSeconds);
        }
    }

    /// <summary>Registers one gauge reporting the depth of each channel stage.</summary>
    public void ObserveQueueDepth(params (string Stage, Func<int> Depth)[] stages) =>
        Meter.CreateObservableGauge(
            "gridpulse.ingestion.queue.depth",
            () => stages.Select(s => new Measurement<int>(s.Depth(), new KeyValuePair<string, object?>("stage", s.Stage))),
            "{reading}");
}
