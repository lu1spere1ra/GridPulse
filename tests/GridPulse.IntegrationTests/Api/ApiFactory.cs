using GridPulse.IntegrationTests.Database;
using GridPulse.IntegrationTests.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GridPulse.IntegrationTests.Api;

/// <summary>The real API host wired to the test TimescaleDB and Mosquitto containers.</summary>
public sealed class ApiFactory(CommunityDatabase database, MosquittoBroker broker, IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:GridPulse", database.ConnectionString);
        builder.UseSetting("Mqtt:Host", broker.Host);
        builder.UseSetting("Mqtt:Port", broker.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }
    }
}
