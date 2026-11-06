using System.Collections.Concurrent;
using GridPulse.Core.Contracts;
using GridPulse.Simulator;
using GridPulse.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GridPulse.UnitTests.Simulator;

public sealed class SimulatorWorkerTests
{
    private sealed class RecordingPublisher : IReadingPublisher
    {
        public ConcurrentQueue<ReadingMessage> Published { get; } = new();

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishAsync(ReadingMessage reading, CancellationToken cancellationToken)
        {
            Published.Enqueue(reading);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Fleet_slices_cover_every_meter_exactly_once()
    {
        var fleet = new MeterFleet(meterCount: 103, sliceCount: 10, seed: 1);

        var serials = Enumerable.Range(0, 10).SelectMany(i => fleet.Slice(i)).Select(m => m.Serial).ToList();

        serials.Count.ShouldBe(103);
        serials.Distinct().Count().ShouldBe(103);
    }

    [Fact]
    public async Task One_interval_of_ticks_publishes_one_reading_per_meter()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        var publisher = new RecordingPublisher();
        var options = Options.Create(new SimulatorOptions
        {
            MeterCount = 50,
            SlicesPerInterval = 5,
            SpikeProbability = 0,
            OfflineProbability = 0,
        });
        var worker = new SimulatorWorker(options, publisher, time, new TestMeterFactory(), NullLogger<SimulatorWorker>.Instance);
        var tasks = new List<Task>();

        for (var tick = 0; tick < 5; tick++)
        {
            await worker.PublishSliceAsync(tick, tasks, TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromMilliseconds(200));
        }

        publisher.Published.Select(r => r.MeterId).Distinct().Count().ShouldBe(50);
        publisher.Published.Count.ShouldBe(50);
    }
}
