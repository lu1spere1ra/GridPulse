using System.Net;
using System.Text;
using System.Text.Json;
using GridPulse.Core.Contracts;
using GridPulse.Web.Services;

namespace GridPulse.Web.Tests;

/// <summary>A telemetry feed the test drives by hand.</summary>
public sealed class FakeTelemetryFeed : ITelemetryFeed
{
    public FeedStatus Status { get; private set; } = FeedStatus.Connected;

    public FleetSnapshot? LastFleet { get; set; }

    public List<int> Subscribed { get; } = [];

    public event Action<FleetSnapshot>? FleetUpdated;

    public event Action<MeterLatest>? MeterUpdated;

    public event Action<AlertNotification>? AlertChanged;

    public event Action<FeedStatus>? StatusChanged;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SubscribeMeterAsync(int meterId)
    {
        Subscribed.Add(meterId);
        return Task.CompletedTask;
    }

    public Task UnsubscribeMeterAsync(int meterId)
    {
        Subscribed.Remove(meterId);
        return Task.CompletedTask;
    }

    public void Publish(FleetSnapshot snapshot) => FleetUpdated?.Invoke(snapshot);

    public void Publish(MeterLatest latest) => MeterUpdated?.Invoke(latest);

    public void Publish(AlertNotification alert) => AlertChanged?.Invoke(alert);

    public void SetStatus(FeedStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }
}

/// <summary>Answers API calls from a table of canned JSON responses (by path prefix).</summary>
public sealed class FakeApiHandler : HttpMessageHandler
{
    /// <summary>Runs before each response; lets a test simulate live events during a slow API call.</summary>
    public Action<string>? OnRequest { get; set; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, object> _responses = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requests { get; } = [];

    public FakeApiHandler Respond(string pathPrefix, object body)
    {
        _responses[pathPrefix] = body;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add($"{request.Method} {path}");
        OnRequest?.Invoke(path);
        var match = _responses.Where(r => path.StartsWith(r.Key, StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.Key.Length).FirstOrDefault();
        if (match.Key is null)
        {
            return Task.FromResult(new HttpResponseMessage(request.Method == HttpMethod.Get ? HttpStatusCode.NotFound : HttpStatusCode.NoContent));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(match.Value, Json), Encoding.UTF8, "application/json"),
        });
    }
}
