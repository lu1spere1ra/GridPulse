using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GridPulse.Infrastructure.Persistence;

/// <summary>Brings a database to the current schema: EF Core migrations, TimescaleDB objects, seed data.</summary>
public static class DatabaseInitializer
{
    public static async Task<TimescaleEdition> InitializeAsync(
        GridPulseDbContext db, NpgsqlDataSource dataSource, ILogger logger, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Applying EF Core migrations");
        await db.Database.MigrateAsync(cancellationToken);
        var edition = await TimescaleSetup.ApplyAsync(dataSource, logger, cancellationToken);
        await DefaultData.SeedAsync(db, logger, cancellationToken);
        return edition;
    }
}
