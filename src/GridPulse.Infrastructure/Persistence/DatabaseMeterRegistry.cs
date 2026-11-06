using System.Collections.Frozen;
using Dapper;
using GridPulse.Core.Readings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GridPulse.Infrastructure.Persistence;

/// <summary>
/// In-memory snapshot of registered meters, reloaded from the database periodically, or
/// within about a second after an unknown serial is seen (a meter that was just provisioned).
/// </summary>
/// <remarks>
/// Lookups run on the MQTT receive path, so they read an immutable <see cref="FrozenDictionary{TKey,TValue}"/>
/// that is swapped atomically on reload: no locks on the hot path.
/// </remarks>
public sealed class DatabaseMeterRegistry(
    NpgsqlDataSource dataSource,
    TimeProvider time,
    ILogger<DatabaseMeterRegistry> logger,
    TimeSpan refreshInterval) : BackgroundService, IMeterRegistry
{
    private static readonly TimeSpan UnknownMeterRefreshDelay = TimeSpan.FromSeconds(1);

    private volatile Snapshot _snapshot = Snapshot.Empty;
    private volatile bool _refreshRequested;

    public int Count => _snapshot.BySerial.Count;

    public IReadOnlyCollection<int> MeterIds => _snapshot.ById.Keys;

    public bool TryResolve(string serial, out int meterId)
    {
        if (_snapshot.BySerial.TryGetValue(serial, out meterId))
        {
            return true;
        }

        _refreshRequested = true;
        return false;
    }

    public string? GetSerial(int meterId) => _snapshot.ById.GetValueOrDefault(meterId);

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<(int Id, string Serial)>(
            new CommandDefinition("SELECT id, serial FROM meters", cancellationToken: cancellationToken))).AsList();

        _snapshot = new Snapshot(
            rows.ToFrozenDictionary(r => r.Serial, r => r.Id, StringComparer.Ordinal),
            rows.ToFrozenDictionary(r => r.Id, r => r.Serial));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastRefresh = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(UnknownMeterRefreshDelay, time);
        do
        {
            var now = time.GetUtcNow();
            if (!_refreshRequested && now - lastRefresh < refreshInterval)
            {
                continue;
            }

            _refreshRequested = false;
            try
            {
                var before = Count;
                await RefreshAsync(stoppingToken);
                lastRefresh = now;
                if (Count != before)
                {
                    logger.LogInformation("Meter registry loaded {Count} meters", Count);
                }
            }
            catch (NpgsqlException ex)
            {
                logger.LogWarning("Meter registry refresh failed: {Message}", ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private sealed record Snapshot(FrozenDictionary<string, int> BySerial, FrozenDictionary<int, string> ById)
    {
        public static Snapshot Empty { get; } = new(FrozenDictionary<string, int>.Empty, FrozenDictionary<int, string>.Empty);
    }
}
