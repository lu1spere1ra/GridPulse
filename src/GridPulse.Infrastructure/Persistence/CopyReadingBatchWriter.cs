using GridPulse.Core.Readings;
using Npgsql;
using NpgsqlTypes;

namespace GridPulse.Infrastructure.Persistence;

/// <summary>
/// Writes readings with PostgreSQL binary <c>COPY</c> into a temporary staging table, then moves
/// them into the hypertable with <c>INSERT … ON CONFLICT DO NOTHING</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>COPY</c> is the fastest way to load rows into PostgreSQL: one round trip, binary encoding,
/// no per-row statement parsing. But <c>COPY</c> cannot skip duplicates, and a single QoS 1
/// redelivery would fail the whole batch. Copying into a staging table first keeps COPY's speed
/// and gets idempotency from the primary key (ADR-0004; numbers in docs/benchmarks.md).
/// </para>
/// <para>
/// Readings are sorted by (meter, time) before the copy so they arrive in primary-key order.
/// </para>
/// </remarks>
public sealed class CopyReadingBatchWriter(NpgsqlDataSource dataSource) : IReadingBatchWriter
{
    private const string CreateStaging = """
        CREATE TEMP TABLE IF NOT EXISTS readings_staging
            (time timestamptz NOT NULL, meter_id int NOT NULL, power_w real NOT NULL, energy_wh bigint NOT NULL, voltage_v real NOT NULL)
            ON COMMIT DELETE ROWS
        """;

    private const string Copy = "COPY readings_staging (time, meter_id, power_w, energy_wh, voltage_v) FROM STDIN (FORMAT BINARY)";

    private const string Merge = """
        INSERT INTO readings (time, meter_id, power_w, energy_wh, voltage_v)
        SELECT time, meter_id, power_w, energy_wh, voltage_v FROM readings_staging
        ON CONFLICT DO NOTHING
        """;

    /// <summary>Returns how many readings were actually inserted (duplicates excluded).</summary>
    public async Task<int> WriteAndCountAsync(IReadOnlyList<Reading> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // A temp table lives as long as the (pooled) physical connection, so it is created once per
        // connection and emptied at every commit, instead of being created and dropped per batch.
        await using (var create = new NpgsqlCommand(CreateStaging, connection, transaction))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var importer = await connection.BeginBinaryImportAsync(Copy, cancellationToken))
        {
            foreach (var reading in batch)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(reading.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(reading.MeterId, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(reading.PowerW, NpgsqlDbType.Real, cancellationToken);
                await importer.WriteAsync(reading.EnergyWh, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(reading.VoltageV, NpgsqlDbType.Real, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }

        int inserted;
        await using (var merge = new NpgsqlCommand(Merge, connection, transaction))
        {
            inserted = await merge.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    public Task WriteAsync(IReadOnlyList<Reading> batch, CancellationToken cancellationToken) =>
        WriteAndCountAsync(batch, cancellationToken);
}
