using GridPulse.Core.Readings;
using Npgsql;
using NpgsqlTypes;

namespace GridPulse.Infrastructure.Persistence;

/// <summary>
/// Writes readings with PostgreSQL binary <c>COPY</c>, optimistically straight into the hypertable,
/// and falls back to an idempotent merge only when the batch contains a duplicate.
/// </summary>
/// <remarks>
/// <para>
/// <c>COPY</c> is the fastest way to load rows into PostgreSQL: one round trip, binary encoding, no
/// per-row statement parsing. But it cannot skip duplicates, and MQTT QoS 1 can redeliver a reading
/// after a reconnect. The first version always copied into a staging table and merged with
/// <c>INSERT … ON CONFLICT DO NOTHING</c>; the benchmarks showed that merge cost about 4× the copy itself.
/// </para>
/// <para>
/// Duplicates are rare (only around reconnects), so the common case now takes the fast path:
/// raw <c>COPY</c> into <c>readings</c>. If the primary key rejects the batch (SQLSTATE 23505), the
/// transaction rolls back and the same batch goes through the staging merge. The result is identical
/// either way. See ADR-0004 and docs/benchmarks.md.
/// </para>
/// </remarks>
public sealed class CopyReadingBatchWriter(NpgsqlDataSource dataSource) : IReadingBatchWriter
{
    private const string CopyIntoReadings = "COPY readings (time, meter_id, power_w, energy_wh, voltage_v) FROM STDIN (FORMAT BINARY)";

    private const string CreateStaging = """
        CREATE TEMP TABLE IF NOT EXISTS readings_staging
            (time timestamptz NOT NULL, meter_id int NOT NULL, power_w real NOT NULL, energy_wh bigint NOT NULL, voltage_v real NOT NULL)
            ON COMMIT DELETE ROWS
        """;

    private const string CopyIntoStaging = "COPY readings_staging (time, meter_id, power_w, energy_wh, voltage_v) FROM STDIN (FORMAT BINARY)";

    private const string Merge = """
        INSERT INTO readings (time, meter_id, power_w, energy_wh, voltage_v)
        SELECT time, meter_id, power_w, energy_wh, voltage_v FROM readings_staging
        ON CONFLICT DO NOTHING
        """;

    /// <summary>How many batches needed the duplicate-safe fallback (exposed for tests and diagnostics).</summary>
    public long FallbackCount => Interlocked.Read(ref _fallbacks);

    private long _fallbacks;

    /// <summary>Returns how many readings were actually inserted (duplicates excluded).</summary>
    public async Task<int> WriteAndCountAsync(IReadOnlyList<Reading> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var copied = await CopyAsync(connection, CopyIntoReadings, batch, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (int)copied;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // A redelivered reading is already stored; the transaction was rolled back, so nothing
            // from this attempt was kept. Retry the same batch through the duplicate-safe path.
            Interlocked.Increment(ref _fallbacks);
            return await MergeAsync(connection, batch, cancellationToken);
        }
    }

    public Task WriteAsync(IReadOnlyList<Reading> batch, CancellationToken cancellationToken) =>
        WriteAndCountAsync(batch, cancellationToken);

    private static async Task<int> MergeAsync(NpgsqlConnection connection, IReadOnlyList<Reading> batch, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The temp table lives as long as the pooled physical connection: created once, emptied at commit.
        await using (var create = new NpgsqlCommand(CreateStaging, connection, transaction))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await CopyAsync(connection, CopyIntoStaging, batch, cancellationToken);

        int inserted;
        await using (var merge = new NpgsqlCommand(Merge, connection, transaction))
        {
            inserted = await merge.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    private static async Task<ulong> CopyAsync(NpgsqlConnection connection, string copyCommand, IReadOnlyList<Reading> batch, CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(copyCommand, cancellationToken);
        foreach (var reading in batch)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(reading.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
            await importer.WriteAsync(reading.MeterId, NpgsqlDbType.Integer, cancellationToken);
            await importer.WriteAsync(reading.PowerW, NpgsqlDbType.Real, cancellationToken);
            await importer.WriteAsync(reading.EnergyWh, NpgsqlDbType.Bigint, cancellationToken);
            await importer.WriteAsync(reading.VoltageV, NpgsqlDbType.Real, cancellationToken);
        }

        return await importer.CompleteAsync(cancellationToken);
    }
}
