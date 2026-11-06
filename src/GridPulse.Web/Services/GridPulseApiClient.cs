using System.Net.Http.Json;
using GridPulse.Core.Contracts;

namespace GridPulse.Web.Services;

/// <summary>Typed client for the GridPulse HTTP API (v1).</summary>
public sealed class GridPulseApiClient(HttpClient http)
{
    public Task<PagedResult<MeterDto>?> GetMetersAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default) =>
        http.GetFromJsonAsync<PagedResult<MeterDto>>(
            $"api/v1/meters?page={page}&pageSize={pageSize}&search={Uri.EscapeDataString(search ?? string.Empty)}", cancellationToken);

    public async Task<MeterDto?> GetMeterAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/v1/meters/{id}", cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<MeterDto>(cancellationToken) : null;
    }

    public Task<ReadingSeries?> GetSeriesAsync(int meterId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        http.GetFromJsonAsync<ReadingSeries>(
            $"api/v1/meters/{meterId}/readings?from={Iso(from)}&to={Iso(to)}", cancellationToken);

    public Task<FleetSummary?> GetFleetAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        http.GetFromJsonAsync<FleetSummary>($"api/v1/analytics/fleet?from={Iso(from)}&to={Iso(to)}", cancellationToken);

    public Task<List<TopConsumer>?> GetTopConsumersAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken = default) =>
        http.GetFromJsonAsync<List<TopConsumer>>($"api/v1/analytics/top-consumers?from={Iso(from)}&to={Iso(to)}&limit={limit}", cancellationToken);

    public Task<List<AlertDto>?> GetAlertsAsync(AlertState? state = null, int limit = 100, CancellationToken cancellationToken = default) =>
        http.GetFromJsonAsync<List<AlertDto>>($"api/v1/alerts?limit={limit}" + (state is { } s ? $"&state={s}" : string.Empty), cancellationToken);

    public async Task AcknowledgeAlertAsync(long id, CancellationToken cancellationToken = default) =>
        (await http.PostAsync($"api/v1/alerts/{id}/acknowledge", null, cancellationToken)).EnsureSuccessStatusCode();

    public Task<List<AlertRuleDto>?> GetAlertRulesAsync(CancellationToken cancellationToken = default) =>
        http.GetFromJsonAsync<List<AlertRuleDto>>("api/v1/alert-rules", cancellationToken);

    public Task<HttpResponseMessage> CreateAlertRuleAsync(AlertRuleRequest request, CancellationToken cancellationToken = default) =>
        http.PostAsJsonAsync("api/v1/alert-rules", request, cancellationToken);

    public Task<HttpResponseMessage> UpdateAlertRuleAsync(int id, AlertRuleRequest request, CancellationToken cancellationToken = default) =>
        http.PutAsJsonAsync($"api/v1/alert-rules/{id}", request, cancellationToken);

    public async Task DeleteAlertRuleAsync(int id, CancellationToken cancellationToken = default) =>
        (await http.DeleteAsync($"api/v1/alert-rules/{id}", cancellationToken)).EnsureSuccessStatusCode();

    private static string Iso(DateTimeOffset value) => Uri.EscapeDataString(value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
}
