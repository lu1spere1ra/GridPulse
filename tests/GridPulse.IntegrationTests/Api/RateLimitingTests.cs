using System.Net;
using GridPulse.IntegrationTests.Database;
using GridPulse.IntegrationTests.Messaging;

namespace GridPulse.IntegrationTests.Api;

public sealed class RateLimitingTests(CommunityDatabase database, MosquittoBroker broker)
{
    [Fact]
    public async Task Requests_over_the_read_limit_get_429_with_retry_after()
    {
        await using var factory = new ApiFactory(database, broker, new Dictionary<string, string?>
        {
            ["RateLimiting:ReadsPerWindow"] = "3",
            ["RateLimiting:Window"] = "00:01:00",
        });
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            (await client.GetAsync("/api/v1/alert-rules", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var limited = await client.GetAsync("/api/v1/alert-rules", TestContext.Current.CancellationToken);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
    }
}
