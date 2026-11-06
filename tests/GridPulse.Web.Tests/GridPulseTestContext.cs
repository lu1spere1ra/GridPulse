using GridPulse.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;

namespace GridPulse.Web.Tests;

/// <summary>bUnit context with MudBlazor, a fake feed, a fake API and a fixed clock.</summary>
public abstract class GridPulseTestContext : BunitContext
{
    protected static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    protected GridPulseTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<TimeProvider>(Clock);
        Services.AddSingleton<ITelemetryFeed>(Feed);
        Services.AddSingleton(new GridPulseApiClient(new HttpClient(Api) { BaseAddress = new Uri("http://api/") }));
    }

    protected FakeTimeProvider Clock { get; } = new(Now);

    protected FakeTelemetryFeed Feed { get; } = new();

    protected FakeApiHandler Api { get; } = new();
}
