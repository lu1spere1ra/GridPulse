using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace GridPulse.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef migrations add</c> only; no database connection is opened.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GridPulseDbContext>
{
    public GridPulseDbContext CreateDbContext(string[] args)
    {
        var dataSource = new NpgsqlDataSourceBuilder("Host=localhost;Database=gridpulse").Build();
        var options = new DbContextOptionsBuilder<GridPulseDbContext>();
        options.UseGridPulse(dataSource);
        return new GridPulseDbContext(options.Options);
    }
}
