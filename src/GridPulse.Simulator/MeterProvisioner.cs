using System.Net.Http.Json;
using GridPulse.Core.Contracts;

namespace GridPulse.Simulator;

/// <summary>
/// Registers the simulated meters through the API before telemetry starts. Provisioning is a
/// separate concern from telemetry: ingestion rejects readings from unknown meters.
/// </summary>
public sealed class MeterProvisioner(HttpClient http, ILogger<MeterProvisioner> logger)
{
    public async Task RegisterAsync(IReadOnlyList<string> serials, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (true)
        {
            try
            {
                foreach (var chunk in serials.Chunk(1_000))
                {
                    using var response = await http.PostAsJsonAsync(
                        "api/v1/meters/batch", new RegisterMetersRequest(chunk), cancellationToken);
                    response.EnsureSuccessStatusCode();
                }

                logger.LogInformation("Registered {Count} meters", serials.Count);
                return;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Meter registration failed ({Message}); retrying in {Delay}", ex.Message, delay);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }
}
