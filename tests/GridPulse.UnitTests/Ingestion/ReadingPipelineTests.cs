using GridPulse.Core.Contracts;
using GridPulse.Core.Readings;
using GridPulse.Ingestion;
using GridPulse.Ingestion.Pipeline;
using GridPulse.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GridPulse.UnitTests.Ingestion;

public sealed class ReadingPipelineTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly RecordingBatchWriter _writer = new();
    private readonly RecordingLivePublisher _live = new();
    private ReadingPipeline _sut = null!;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _sut.StopAsync(CancellationToken.None);

    private async Task StartAsync(IngestionOptions options)
    {
        _sut = new ReadingPipeline(
            Options.Create(options),
            Options.Create(new ReadingValidationOptions()),
            new InMemoryMeterRegistry(10),
            _writer,
            _live,
            new IngestionMetrics(new TestMeterFactory()),
            _time,
            NullLogger<ReadingPipeline>.Instance,
            []);
        await _sut.StartAsync(Ct);
    }

    private static ReadingMessage Message(int meter, int second) =>
        new($"MTR-{meter:D6}", T0.AddSeconds(second - 200), 100, second, 230);

    private static async Task Eventually(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task Flushes_full_batches_and_drains_the_remainder_on_stop()
    {
        await StartAsync(new IngestionOptions { BatchSize = 10, FlushInterval = TimeSpan.FromMinutes(10) });

        for (var i = 0; i < 25; i++)
        {
            await _sut.SubmitAsync(Message(1, i), Ct);
        }

        await _sut.StopAsync(Ct);

        _writer.Batches.Select(b => b.Length).ShouldBe([10, 10, 5]);
    }

    [Fact]
    public async Task Flushes_a_partial_batch_when_the_flush_interval_elapses()
    {
        await StartAsync(new IngestionOptions { BatchSize = 1_000, FlushInterval = TimeSpan.FromMilliseconds(250) });

        for (var i = 0; i < 3; i++)
        {
            await _sut.SubmitAsync(Message(1, i), Ct);
        }

        await Task.Delay(50, Ct);
        _writer.Total.ShouldBe(0);

        // Advance the fake clock in small steps: the writer may not have armed its timer yet
        // when the first step happens, and a single big step would then be missed.
        await Eventually(() =>
        {
            _time.Advance(TimeSpan.FromMilliseconds(50));
            return _writer.Total == 3;
        });
        _writer.Batches.ShouldHaveSingleItem().Length.ShouldBe(3);
    }

    [Fact]
    public async Task A_blocked_writer_eventually_blocks_submitters()
    {
        _writer.Block();
        await StartAsync(new IngestionOptions { InputCapacity = 5, WriterCapacity = 5, BatchSize = 1 });

        ValueTask<RejectReason> pending = default;
        var submitted = 0;
        for (var i = 0; i < 100; i++)
        {
            pending = _sut.SubmitAsync(Message(1, i), Ct);
            if (!pending.IsCompleted)
            {
                break;
            }

            submitted++;
        }

        pending.IsCompleted.ShouldBeFalse("bounded channels must apply backpressure");
        submitted.ShouldBeLessThan(100);

        _writer.Release();
        (await pending).ShouldBe(RejectReason.None);
    }

    [Fact]
    public async Task Stop_drains_every_accepted_reading()
    {
        await StartAsync(new IngestionOptions { BatchSize = 64 });

        for (var i = 0; i < 1_000; i++)
        {
            await _sut.SubmitAsync(Message((i % 10) + 1, i / 10), Ct);
        }

        await _sut.StopAsync(Ct);

        _writer.Total.ShouldBe(1_000);
    }

    [Fact]
    public async Task Unknown_meters_and_invalid_readings_are_rejected_before_the_channel()
    {
        await StartAsync(new IngestionOptions());

        (await _sut.SubmitAsync(Message(1, 0) with { MeterId = "MTR-999999" }, Ct)).ShouldBe(RejectReason.UnknownMeter);
        (await _sut.SubmitAsync(Message(1, 0) with { PowerW = -5 }, Ct)).ShouldBe(RejectReason.PowerOutOfRange);

        await _sut.StopAsync(Ct);
        _writer.Total.ShouldBe(0);
    }

    [Fact]
    public async Task Ticks_publish_a_fleet_snapshot()
    {
        await StartAsync(new IngestionOptions { TickInterval = TimeSpan.FromSeconds(1) });
        await _sut.SubmitAsync(Message(1, 0), Ct);

        await Eventually(() =>
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
            return !_live.Fleet.IsEmpty;
        });
        _live.Fleet.First().OnlineMeters.ShouldBe(1);
    }
}
