using Azure.Identity;
using Azure.Storage.Blobs;
using GridPulse.Reports.Reporting;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(
    builder.Configuration.GetConnectionString("GridPulse")
        ?? throw new InvalidOperationException("Connection string 'GridPulse' is not configured.")).Build());

// Locally (Azurite) a connection string; in Azure the account URL + managed identity, no keys.
builder.Services.AddSingleton(_ => builder.Configuration["ReportStorage:ServiceUri"] is { Length: > 0 } serviceUri
    ? new BlobServiceClient(new Uri(serviceUri), new DefaultAzureCredential())
    : new BlobServiceClient(builder.Configuration["ReportStorage:ConnectionString"] ?? "UseDevelopmentStorage=true"));

builder.Services.AddSingleton<DailyReportQuery>();
builder.Services.AddSingleton<DailyReportService>();

builder.Build().Run();
