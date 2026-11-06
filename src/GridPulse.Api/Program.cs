using GridPulse.Api.Endpoints;
using GridPulse.Api.Hosting;
using GridPulse.Api.Live;
using GridPulse.Core.Contracts;
using GridPulse.Infrastructure.Messaging;
using GridPulse.Infrastructure.Persistence;
using GridPulse.Infrastructure.Queries;
using GridPulse.ServiceDefaults;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddGridPulseDatabase();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TelemetryQueries>();
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddGridPulseVersioning();
builder.Services.AddGridPulseRateLimiting();
builder.Services.AddGridPulseOutputCache();

builder.Services.AddOptions<MqttOptions>().BindConfiguration(MqttOptions.SectionName);
builder.Services.AddSignalR();
builder.Services.AddSingleton<LiveState>();
builder.Services.AddSingleton<MeterSubscriptions>();
builder.Services.AddHostedService<LiveTelemetryBridge>();

builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    // Behind the Container Apps / Kubernetes ingress the client IP (used to partition rate
    // limits) arrives in X-Forwarded-For.
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseOutputCache();

app.MapOpenApi().WithDocumentPerVersion();
app.MapScalarApiReference();

var v1 = app.NewVersionedApi("GridPulse")
    .MapGroup("/api/v{version:apiVersion}")
    .HasApiVersion(1.0);

v1.MapMeterEndpoints();
v1.MapReadingEndpoints();
v1.MapAnalyticsEndpoints();
v1.MapAlertRuleEndpoints();
v1.MapAlertEndpoints();

app.MapHub<TelemetryHub>(TelemetryHubMethods.Path);
app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

app.Run();

/// <summary>Entry point marker for <c>WebApplicationFactory</c> in integration tests.</summary>
public partial class Program;
