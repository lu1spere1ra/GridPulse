using GridPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

// One-shot job: EF Core migrations, then edition-specific TimescaleDB objects, then seed data.
// Runs as a compose service before the apps start, and as a Container Apps Job in Azure.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.AddGridPulseDatabase();
using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Migrator");
var dataSource = host.Services.GetRequiredService<NpgsqlDataSource>();

await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GridPulseDbContext>();
    await WaitForDatabaseAsync(db, logger);
    await DatabaseInitializer.InitializeAsync(db, dataSource, logger);
}

logger.LogInformation("Database is up to date");

static async Task WaitForDatabaseAsync(GridPulseDbContext db, ILogger logger)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.CloseConnectionAsync();
            return;
        }
        catch (NpgsqlException) when (attempt < 30)
        {
            logger.LogInformation("Waiting for the database (attempt {Attempt})", attempt);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
