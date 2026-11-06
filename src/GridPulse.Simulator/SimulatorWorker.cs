using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;

namespace GridPulse.Simulator;

public sealed class SimulatorWorker : BackgroundService
{
    private readonly SimulatorOptions _options;
    private readonly IReadingPublisher _publisher;
    private readonly MeterProvisioner? _provisioner;
    private readonly TimeProvider _time;
    private readonly ILogger<SimulatorWorker> _logger;
    private readonly Counter<long> _published;
    private readonly Counter<long> _failed;

    public SimulatorWorker(
        IOptions<SimulatorOptions> options,
        IReadingPublisher publisher,
        TimeProvider time,
        IMeterFactory meterFactory,
        ILogger<SimulatorWorker> logger,
        MeterProvisioner? provisioner = null)
    {
        _options = options.Value;
        _publisher = publisher;
        _provisioner = provisioner;
        _time = time;
        _logger = logger;
        Fleet = new MeterFleet(_options.MeterCount, _options.SlicesPerInterval, _options.Seed);

        var meter = meterFactory.Create("GridPulse.Simulator");
        _published = meter.CreateCounter<long>("gridpulse.simulator.readings.published", "{reading}");
        _failed = meter.CreateCounter<long>("gridpulse.simulator.readings.failed", "{reading}");
    }

    public MeterFleet Fleet { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_provisioner is not null)
        {
            await _provisioner.RegisterAsync(Fleet.Meters.Select(m => m.Serial).ToArray(), stoppingToken);
        }

        await _publisher.ConnectAsync(stoppingToken);
        _logger.LogInformation(
            "Simulating {Meters} meters, one reading every {Interval}", _options.MeterCount, _options.PublishInterval);

        using var timer = new PeriodicTimer(_options.PublishInterval / Fleet.SliceCount, _time);
        var tasks = new List<Task>(Fleet.Meters.Count / Fleet.SliceCount + 1);
        long tick = 0;

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PublishSliceAsync(tick++, tasks, stoppingToken);
        }
    }

    /// <summary>Publishes one slice concurrently: QoS 1 waits for a PUBACK per message, so awaiting them one by one would cap throughput at one round trip per reading.</summary>
    internal async Task PublishSliceAsync(long tick, List<Task> tasks, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        tasks.Clear();
        foreach (var meter in Fleet.Slice(tick))
        {
            if (meter.Next(now, _options.SpikeProbability, _options.OfflineProbability) is { } reading)
            {
                tasks.Add(_publisher.PublishAsync(reading, cancellationToken));
            }
        }

        try
        {
            await Task.WhenAll(tasks);
            _published.Add(tasks.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failed = tasks.Count(t => t.IsFaulted);
            _failed.Add(failed);
            _published.Add(tasks.Count - failed);
            _logger.LogWarning("Failed to publish {Failed} readings: {Message}", failed, ex.Message);
        }
    }
}
