using System.Net;
using System.Net.Http.Json;
using GridPulse.Core.Contracts;
using GridPulse.IntegrationTests.Database;
using GridPulse.IntegrationTests.Messaging;

namespace GridPulse.IntegrationTests.Api;

public sealed class MeterApiTests(CommunityDatabase database, MosquittoBroker broker) : IAsyncLifetime
{
    private readonly ApiFactory _factory = new(database, broker);
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static string NewSerial() => $"T-{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}";

    [Fact]
    public async Task Create_get_update_and_delete_a_meter()
    {
        var serial = NewSerial();
        var created = await _client.PostAsJsonAsync("/api/v1/meters", new CreateMeterRequest(serial, "Kitchen", "Lisbon"), Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var meter = (await created.Content.ReadFromJsonAsync<MeterDto>(Ct))!;
        created.Headers.Location!.ToString().ShouldEndWith($"/api/v1/meters/{meter.Id}");

        (await _client.PutAsJsonAsync($"/api/v1/meters/{meter.Id}", new UpdateMeterRequest("Office", "Porto"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var fetched = await _client.GetFromJsonAsync<MeterDto>($"/api/v1/meters/{meter.Id}", Ct);
        (fetched!.Name, fetched.Zone).ShouldBe(("Office", "Porto"));

        (await _client.DeleteAsync($"/api/v1/meters/{meter.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.GetAsync($"/api/v1/meters/{meter.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Duplicate_serial_returns_conflict_problem_details()
    {
        var serial = NewSerial();
        await _client.PostAsJsonAsync("/api/v1/meters", new CreateMeterRequest(serial, null, null), Ct);

        var response = await _client.PostAsJsonAsync("/api/v1/meters", new CreateMeterRequest(serial, null, null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Invalid_request_returns_validation_problem()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/meters", new CreateMeterRequest("not a serial", null, null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Serial");
    }

    [Fact]
    public async Task Batch_registration_is_idempotent()
    {
        var serials = Enumerable.Range(0, 3).Select(_ => NewSerial()).ToList();

        var first = await (await _client.PostAsJsonAsync("/api/v1/meters/batch", new RegisterMetersRequest(serials), Ct)).Content.ReadFromJsonAsync<RegisterMetersResponse>(Ct);
        var second = await (await _client.PostAsJsonAsync("/api/v1/meters/batch", new RegisterMetersRequest(serials), Ct)).Content.ReadFromJsonAsync<RegisterMetersResponse>(Ct);

        first.ShouldBe(new RegisterMetersResponse(3, 3));
        second.ShouldBe(new RegisterMetersResponse(3, 0));
    }

    [Fact]
    public async Task Responses_report_supported_versions_and_unknown_versions_are_rejected()
    {
        var response = await _client.GetAsync("/api/v1/meters", Ct);
        response.Headers.GetValues("api-supported-versions").ShouldContain("1.0");

        (await _client.GetAsync("/api/v2/meters", Ct)).IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Openapi_document_is_published_per_version()
    {
        var document = await _client.GetStringAsync("/openapi/v1.json", Ct);

        document.ShouldContain("/api/v1/meters/{id}/readings");
    }
}
