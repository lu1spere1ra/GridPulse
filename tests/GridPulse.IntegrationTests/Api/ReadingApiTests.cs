using System.Net;
using System.Net.Http.Json;
using GridPulse.Core.Contracts;
using GridPulse.Core.Readings;
using GridPulse.Infrastructure.Persistence;
using GridPulse.IntegrationTests.Database;
using GridPulse.IntegrationTests.Messaging;

namespace GridPulse.IntegrationTests.Api;

public sealed class ReadingApiTests(CommunityDatabase database, MosquittoBroker broker) : IAsyncLifetime
{
    private readonly ApiFactory _factory = new(database, broker);
    private HttpClient _client = null!;
    private int _meterId;
    private DateTimeOffset _start;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        var created = await _client.PostAsJsonAsync("/api/v1/meters", new CreateMeterRequest($"R-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}", null, null), Ct);
        _meterId = (await created.Content.ReadFromJsonAsync<MeterDto>(Ct))!.Id;

        // Two hours of 10-second readings that ended an hour ago (a settled, cacheable range).
        _start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddHours(DateTime.UtcNow.Hour - 3);
        var readings = Enumerable.Range(0, 720).Select(i => new Reading(_meterId, _start.AddSeconds(i * 10), 1_000 + (i % 60), i, 230)).ToList();
        await new CopyReadingBatchWriter(database.DataSource).WriteAsync(readings, Ct);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private string Url(DateTimeOffset from, DateTimeOffset to, string? resolution = null) =>
        $"/api/v1/meters/{_meterId}/readings?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}"
        + (resolution is null ? string.Empty : $"&resolution={resolution}");

    [Fact]
    public async Task Short_ranges_return_raw_points()
    {
        var series = await _client.GetFromJsonAsync<ReadingSeries>(Url(_start, _start.AddMinutes(10)), Ct);

        series!.Resolution.ShouldBe(Resolution.Raw);
        series.Points.Count.ShouldBe(60);
    }

    [Fact]
    public async Task Longer_ranges_return_minute_buckets_from_the_rollups()
    {
        var series = await _client.GetFromJsonAsync<ReadingSeries>(Url(_start, _start.AddHours(2)), Ct);

        series!.Resolution.ShouldBe(Resolution.OneMinute);
        series.Points.Count.ShouldBe(120);
        series.Points.ShouldAllBe(p => p.Samples == 6);
    }

    [Fact]
    public async Task Historical_ranges_are_cached_until_the_meter_changes()
    {
        var url = Url(_start, _start.AddHours(1));

        (await _client.GetAsync(url, Ct)).Headers.Age.ShouldBeNull();
        (await _client.GetAsync(url, Ct)).Headers.Age.ShouldNotBeNull("second request should be served from the output cache");

        await _client.PutAsJsonAsync($"/api/v1/meters/{_meterId}", new UpdateMeterRequest("renamed", null), Ct);
        (await _client.GetAsync(url, Ct)).Headers.Age.ShouldBeNull("updating the meter evicts its cache entries");
    }

    [Fact]
    public async Task Ranges_reaching_into_the_present_are_not_cached()
    {
        var url = Url(DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(1));

        await _client.GetAsync(url, Ct);
        (await _client.GetAsync(url, Ct)).Headers.Age.ShouldBeNull();
    }

    [Fact]
    public async Task Invalid_ranges_are_rejected()
    {
        (await _client.GetAsync(Url(_start, _start.AddHours(-1)), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _client.GetAsync(Url(_start, _start.AddDays(1), "Raw"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_meter_returns_not_found()
    {
        var url = $"/api/v1/meters/{int.MaxValue}/readings?from={Uri.EscapeDataString(_start.ToString("O"))}&to={Uri.EscapeDataString(_start.AddHours(1).ToString("O"))}";

        (await _client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
