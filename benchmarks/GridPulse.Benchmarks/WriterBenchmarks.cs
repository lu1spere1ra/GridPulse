using BenchmarkDotNet.Attributes;
using Dapper;
using GridPulse.Core.Readings;
using GridPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace GridPulse.Benchmarks;

/// <summary>
/// Four ways to write a batch of readings into the <c>readings</c> hypertable. Results are per row.
/// </summary>
/// <remarks>
/// Needs a running TimescaleDB. Uses (and recreates) a separate database, <c>gridpulse_bench</c>, so it
/// never touches development data. Default server: the compose database on localhost:5310;
/// override with <c>GRIDPULSE_BENCH_SERVER</c> (a connection string without a database name).
/// </remarks>
[MemoryDiagnoser]
public class WriterBenchmarks
{
    private const int Meters = 5_000;

    private NpgsqlDataSource _dataSource = null!;
    private CopyReadingBatchWriter _copyWithStaging = null!;
    private DbContextOptions<GridPulseDbContext> _efOptions = null!;
    private DateTimeOffset _next;

    [Params(1_000, 5_000)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var server = Environment.GetEnvironmentVariable("GRIDPULSE_BENCH_SERVER")
            ?? "Host=localhost;Port=5310;Username=gridpulse;Password=gridpulse";

        using (var admin = new NpgsqlConnection($"{server};Database=postgres"))
        {
            admin.Execute("DROP DATABASE IF EXISTS gridpulse_bench WITH (FORCE)");
            admin.Execute("CREATE DATABASE gridpulse_bench");
        }

        _dataSource = new NpgsqlDataSourceBuilder($"{server};Database=gridpulse_bench").Build();
        var options = new DbContextOptionsBuilder<GridPulseDbContext>();
        options.UseGridPulse(_dataSource);
        _efOptions = options.Options;
        using (var db = new GridPulseDbContext(_efOptions))
        {
            DatabaseInitializer.InitializeAsync(db, _dataSource, NullLogger.Instance).GetAwaiter().GetResult();
        }

        _copyWithStaging = new CopyReadingBatchWriter(_dataSource);
        _next = DateTimeOffset.UtcNow.AddHours(-6);
    }

    [GlobalCleanup]
    public void Cleanup() => _dataSource.Dispose();

    /// <summary>Unique timestamps per call, so the non-idempotent variants never hit a conflict.</summary>
    private List<Reading> NextBatch()
    {
        var batch = new List<Reading>(BatchSize);
        for (var i = 0; i < BatchSize; i++)
        {
            if (i % Meters == 0)
            {
                _next = _next.AddSeconds(1);
            }

            batch.Add(new Reading((i % Meters) + 1, _next, 1_234.5f, 1_000_000 + i, 230.1f));
        }

        _next = _next.AddSeconds(1);
        return batch;
    }

    [Benchmark(Baseline = true)]
    public async Task EfCoreAddRange()
    {
        await using var db = new GridPulseDbContext(_efOptions);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.Readings.AddRange(NextBatch().Select(r => new ReadingRow
        {
            Time = r.Timestamp, MeterId = r.MeterId, PowerW = r.PowerW, EnergyWh = r.EnergyWh, VoltageV = r.VoltageV,
        }));
        await db.SaveChangesAsync();
    }

    [Benchmark]
    public async Task DapperUnnestOnConflict()
    {
        var batch = NextBatch();
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO readings (time, meter_id, power_w, energy_wh, voltage_v)
            SELECT * FROM unnest(@times, @meters, @powers, @energies, @voltages)
            ON CONFLICT DO NOTHING
            """,
            new
            {
                times = batch.Select(r => r.Timestamp).ToArray(),
                meters = batch.Select(r => r.MeterId).ToArray(),
                powers = batch.Select(r => r.PowerW).ToArray(),
                energies = batch.Select(r => r.EnergyWh).ToArray(),
                voltages = batch.Select(r => r.VoltageV).ToArray(),
            });
    }

    [Benchmark]
    public Task CopyViaStagingOnConflict() => _copyWithStaging.WriteAsync(NextBatch(), CancellationToken.None);

    /// <summary>Reference point only: fastest, but a single duplicate fails the whole batch.</summary>
    [Benchmark]
    public async Task RawCopy()
    {
        var batch = NextBatch();
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var importer = await connection.BeginBinaryImportAsync(
            "COPY readings (time, meter_id, power_w, energy_wh, voltage_v) FROM STDIN (FORMAT BINARY)");
        foreach (var r in batch)
        {
            await importer.StartRowAsync();
            await importer.WriteAsync(r.Timestamp, NpgsqlDbType.TimestampTz);
            await importer.WriteAsync(r.MeterId, NpgsqlDbType.Integer);
            await importer.WriteAsync(r.PowerW, NpgsqlDbType.Real);
            await importer.WriteAsync(r.EnergyWh, NpgsqlDbType.Bigint);
            await importer.WriteAsync(r.VoltageV, NpgsqlDbType.Real);
        }

        await importer.CompleteAsync();
    }
}
