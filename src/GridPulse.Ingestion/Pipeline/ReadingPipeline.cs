using System.Data.Common;
using System.Diagnostics;
using System.Threading.Channels;
using GridPulse.Core.Contracts;
using GridPulse.Core.Readings;
using Microsoft.Extensions.Options;

namespace GridPulse.Ingestion.Pipeline;

/// <summary>
/// The ingestion pipeline: <c>SubmitAsync → [input channel] → processor → [writer channel] → batch writer</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both channels are bounded with <see cref="BoundedChannelFullMode.Wait"/>. When the database
/// slows down, the writer channel fills, the processor blocks, the input channel fills and
/// <see cref="SubmitAsync"/> stops completing. The MQTT client then stops reading its socket and
/// the broker queues messages for us. Memory stays bounded by the channel capacities (ADR-0002).
/// </para>
/// <para>
/// Shutdown drains rather than cancels: the input channel is completed, every stage finishes
/// what it holds, and the last partial batch is written.
/// </para>
/// </remarks>
public sealed class ReadingPipeline : IHostedService, IDisposable
{
    private readonly IngestionOptions _options;
    private readonly IMeterRegistry _registry;
    private readonly IReadingBatchWriter _writer;
    private readonly ILivePublisher _live;
    private readonly IngestionMetrics _metrics;
    private readonly TimeProvider _time;
    private readonly ILogger<ReadingPipeline> _logger;
    private readonly ReadingValidator _validator;
    private readonly ReadingProcessor _processor;
    private readonly Channel<PipelineItem> _input;
    private readonly Channel<Reading> _toWriter;
    private readonly Channel<LiveUpdate> _liveUpdates;
    private readonly CancellationTokenSource _stopTicker = new();
    private Task _completion = Task.CompletedTask;

    public ReadingPipeline(
        IOptions<IngestionOptions> options,
        IOptions<ReadingValidationOptions> validationOptions,
        IMeterRegistry registry,
        IReadingBatchWriter writer,
        ILivePublisher live,
        IngestionMetrics metrics,
        TimeProvider time,
        ILogger<ReadingPipeline> logger,
        IEnumerable<IWindowListener> listeners)
    {
        _options = options.Value;
        _registry = registry;
        _writer = writer;
        _live = live;
        _metrics = metrics;
        _time = time;
        _logger = logger;
        _validator = new ReadingValidator(validationOptions.Value);
        _processor = new ReadingProcessor(_options, registry, metrics, listeners);

        _input = Channel.CreateBounded<PipelineItem>(new BoundedChannelOptions(_options.InputCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        _toWriter = Channel.CreateBounded<Reading>(new BoundedChannelOptions(_options.WriterCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

        // Live data is a snapshot: if the publisher falls behind, older snapshots are worthless.
        _liveUpdates = Channel.CreateBounded<LiveUpdate>(new BoundedChannelOptions(4)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });

        metrics.ObserveQueueDepth(("input", () => _input.Reader.Count), ("writer", () => _toWriter.Reader.Count));
    }

    /// <summary><c>false</c> while the database rejects writes; drives <c>/health/ready</c>.</summary>
    public bool IsWriterHealthy { get; private set; } = true;

    /// <summary>
    /// Validates a message and enqueues it. Completes only when there is room in the input
    /// channel: this is where backpressure reaches the MQTT client.
    /// </summary>
    public async ValueTask<RejectReason> SubmitAsync(ReadingMessage message, CancellationToken cancellationToken)
    {
        _metrics.Received();

        var reason = _validator.Validate(message, _time.GetUtcNow());
        if (reason == RejectReason.None && !_registry.TryResolve(message.MeterId, out _))
        {
            reason = RejectReason.UnknownMeter;
        }

        if (reason != RejectReason.None)
        {
            _metrics.Rejected(reason);
            return reason;
        }

        _registry.TryResolve(message.MeterId, out var meterId);
        var reading = new Reading(meterId, message.Ts, (float)message.PowerW, message.EnergyWh, (float)message.VoltageV);
        await _input.Writer.WriteAsync(PipelineItem.For(reading), cancellationToken);
        return RejectReason.None;
    }

    /// <summary>Counts a payload that could not even be parsed.</summary>
    public void RejectMalformed()
    {
        _metrics.Received();
        _metrics.Rejected(RejectReason.Malformed);
    }

    /// <summary>
    /// Starts every stage. This is a plain <see cref="IHostedService"/> rather than a
    /// <c>BackgroundService</c> on purpose: a <c>BackgroundService</c> whose stop is requested
    /// before <c>ExecuteAsync</c> gets scheduled never runs it, and then nothing would be drained.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _completion = Task.WhenAll(
            Task.Run(RunProcessorAsync, CancellationToken.None),
            Task.Run(RunWriterAsync, CancellationToken.None),
            Task.Run(RunLivePublisherAsync, CancellationToken.None),
            RunTickerAsync(_stopTicker.Token));
        return Task.CompletedTask;
    }

    /// <summary>Completes the input and waits for every stage to drain (bounded by the host's shutdown timeout).</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopTicker.CancelAsync();
        _input.Writer.TryComplete();
        await _completion.WaitAsync(cancellationToken);
        _logger.LogInformation("Ingestion pipeline drained");
    }

    public void Dispose() => _stopTicker.Dispose();

    private async Task RunTickerAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.TickInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await _input.Writer.WriteAsync(PipelineItem.Tick, stoppingToken);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
        {
            // Shutting down.
        }
    }

    private async Task RunProcessorAsync()
    {
        try
        {
            await foreach (var item in _input.Reader.ReadAllAsync())
            {
                if (item.IsTick)
                {
                    _liveUpdates.Writer.TryWrite(_processor.Tick(_time.GetUtcNow()));
                }
                else if (_processor.Process(item.Reading))
                {
                    await _toWriter.Writer.WriteAsync(item.Reading);
                }
            }

            _processor.Flush();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Reading processor failed");
            throw;
        }
        finally
        {
            _toWriter.Writer.TryComplete();
            _liveUpdates.Writer.TryComplete();
        }
    }

    private async Task RunWriterAsync()
    {
        var reader = _toWriter.Reader;
        var batch = new List<Reading>(_options.BatchSize);

        while (await reader.WaitToReadAsync())
        {
            // The first reading starts the clock: flush when the batch is full or FlushInterval has passed.
            var deadline = _time.GetUtcNow() + _options.FlushInterval;
            while (batch.Count < _options.BatchSize)
            {
                while (batch.Count < _options.BatchSize && reader.TryRead(out var reading))
                {
                    batch.Add(reading);
                }

                var remaining = deadline - _time.GetUtcNow();
                if (batch.Count >= _options.BatchSize || remaining <= TimeSpan.Zero)
                {
                    break;
                }

                using var wait = new CancellationTokenSource(remaining, _time);
                try
                {
                    if (!await reader.WaitToReadAsync(wait.Token))
                    {
                        break; // Completed: flush what we have.
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // Flush interval elapsed.
                }
            }

            await WriteWithRetryAsync(batch);
            batch.Clear();
        }
    }

    /// <summary>
    /// Transient database errors (connection loss, failover) are retried indefinitely with
    /// capped backoff: the channels fill up and backpressure protects memory meanwhile.
    /// Anything else is retried a few times and then the batch is dropped and logged.
    /// </summary>
    private async Task WriteWithRetryAsync(List<Reading> batch)
    {
        var delay = TimeSpan.FromMilliseconds(250);
        for (var attempt = 1; ; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                await _writer.WriteAsync(batch, CancellationToken.None);
                _metrics.BatchPersisted(batch, Stopwatch.GetElapsedTime(started), _time.GetUtcNow());
                IsWriterHealthy = true;
                return;
            }
            catch (Exception ex) when (ex is DbException { IsTransient: true } || attempt < 3)
            {
                IsWriterHealthy = false;
                _logger.LogWarning(ex, "Writing a batch of {Count} readings failed (attempt {Attempt}); retrying in {Delay}", batch.Count, attempt, delay);
                await Task.Delay(delay, _time);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromSeconds(30).Ticks));
            }
            catch (Exception ex)
            {
                IsWriterHealthy = false;
                _logger.LogError(ex, "Dropping a batch of {Count} readings after {Attempt} attempts", batch.Count, attempt);
                return;
            }
        }
    }

    private async Task RunLivePublisherAsync()
    {
        await foreach (var update in _liveUpdates.Reader.ReadAllAsync())
        {
            try
            {
                await _live.PublishFleetAsync(update.Fleet, CancellationToken.None);
                if (update.Latest.Meters.Count > 0)
                {
                    await _live.PublishLatestAsync(update.Latest, CancellationToken.None);
                }

                if (update.Windows.Count > 0)
                {
                    await _live.PublishWindowsAsync(update.Windows, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                // Live data is best effort; the next tick carries a fresh snapshot.
                _logger.LogDebug(ex, "Publishing live data failed");
            }
        }
    }

    private readonly record struct PipelineItem(Reading Reading, bool IsTick)
    {
        public static PipelineItem Tick { get; } = new(default, true);

        public static PipelineItem For(in Reading reading) => new(reading, false);
    }
}
