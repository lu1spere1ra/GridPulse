using GridPulse.Core.Readings;
using GridPulse.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace GridPulse.Ingestion;

internal static class PersistenceRegistration
{
    public static void AddPersistence(this WebApplicationBuilder builder)
    {
        builder.AddGridPulseDatabase();

        builder.Services.AddSingleton(sp => new DatabaseMeterRegistry(
            sp.GetRequiredService<Npgsql.NpgsqlDataSource>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<DatabaseMeterRegistry>>(),
            sp.GetRequiredService<IOptions<IngestionOptions>>().Value.MeterRefreshInterval));
        builder.Services.AddSingleton<IMeterRegistry>(sp => sp.GetRequiredService<DatabaseMeterRegistry>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<DatabaseMeterRegistry>());

        builder.Services.AddSingleton<IReadingBatchWriter, CopyReadingBatchWriter>();
    }
}
