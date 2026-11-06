using Asp.Versioning;

namespace GridPulse.Api.Hosting;

internal static class VersioningExtensions
{
    /// <summary>
    /// URL-segment versioning (<c>/api/v1/…</c>): visible, cache-friendly and easy to route at the
    /// ingress. Supported and deprecated versions are reported in response headers, and one
    /// OpenAPI document is generated per version (<c>/openapi/v1.json</c>).
    /// </summary>
    public static IServiceCollection AddGridPulseVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = false;
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'V";
                options.SubstituteApiVersionInUrl = true;
            })
            .AddOpenApi();
        return services;
    }
}
