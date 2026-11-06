using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace GridPulse.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public const string ConnectionStringName = "GridPulse";

    /// <summary>
    /// Registers one pooled <see cref="NpgsqlDataSource"/> (used directly by COPY and Dapper) and an
    /// EF Core context factory on top of the same data source.
    /// </summary>
    public static IHostApplicationBuilder AddGridPulseDatabase(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        builder.Services.AddPooledDbContextFactory<GridPulseDbContext>((sp, options) => options.UseGridPulse(sp.GetRequiredService<NpgsqlDataSource>()));
        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<GridPulseDbContext>>().CreateDbContext());
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
        return builder;
    }

    public static DbContextOptionsBuilder UseGridPulse(this DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options.UseNpgsql(dataSource).UseSnakeCaseNamingConvention();
}
