using GridPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GridPulse.IntegrationTests.Database;

/// <summary>
/// A real TimescaleDB in a container, migrated exactly like production (EF Core migrations +
/// <see cref="TimescaleSetup"/> + seed). One container per edition per test run.
/// </summary>
public abstract class TimescaleDatabase(TimescaleEdition edition) : IAsyncLifetime
{
    public const string Image = "timescale/timescaledb:2.31.0-pg17";

    private PostgreSqlContainer _container = null!;

    public TimescaleEdition Edition { get; } = edition;

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        var builder = new PostgreSqlBuilder(Image)
            .WithDatabase("gridpulse")
            .WithUsername("gridpulse")
            .WithPassword("gridpulse");

        // The Apache-2 edition is what Azure Database for PostgreSQL ships. Forcing the license
        // setting locally reproduces Azure's feature set (no continuous aggregates, no policies).
        if (Edition == TimescaleEdition.Apache)
        {
            builder = builder.WithCommand("-c", "timescaledb.license=apache");
        }

        _container = builder.Build();
        await _container.StartAsync();

        DataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        await using var db = CreateDbContext();
        var detected = await DatabaseInitializer.InitializeAsync(db, DataSource, NullLogger.Instance);
        if (detected != Edition)
        {
            throw new InvalidOperationException($"Expected the {Edition} edition but the server reports {detected}.");
        }
    }

    public GridPulseDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<GridPulseDbContext>();
        options.UseGridPulse(DataSource);
        return new GridPulseDbContext(options.Options);
    }

    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

public sealed class CommunityDatabase() : TimescaleDatabase(TimescaleEdition.Community);

public sealed class ApacheDatabase() : TimescaleDatabase(TimescaleEdition.Apache);
