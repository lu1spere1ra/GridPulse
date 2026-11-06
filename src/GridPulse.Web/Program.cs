using GridPulse.ServiceDefaults;
using GridPulse.Web.Components;
using GridPulse.Web.Services;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddSingleton(TimeProvider.System);

var apiBaseUrl = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5301/");
builder.Services.AddHttpClient<GridPulseApiClient>(http => http.BaseAddress = apiBaseUrl);

// Scoped = one per Blazor circuit (one per browser tab).
builder.Services.AddScoped<ITelemetryFeed, SignalRTelemetryFeed>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();
