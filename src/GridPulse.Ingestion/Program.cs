using GridPulse.Core.Readings;
using GridPulse.Infrastructure.Messaging;
using GridPulse.Ingestion;
using GridPulse.Ingestion.Mqtt;
using GridPulse.Ingestion.Pipeline;
using GridPulse.ServiceDefaults;

var builder = WebApplication.CreateSlimBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddOptions<IngestionOptions>()
    .BindConfiguration(IngestionOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<ReadingValidationOptions>().BindConfiguration("Validation");
builder.Services.AddOptions<MqttOptions>().BindConfiguration(MqttOptions.SectionName);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IngestionMetrics>();
builder.Services.AddSingleton<MqttLivePublisher>();
builder.Services.AddSingleton<ILivePublisher>(sp => sp.GetRequiredService<MqttLivePublisher>());

builder.AddPersistence();

// Order matters: hosted services stop in reverse order, so MQTT stops before the pipeline drains.
builder.Services.AddSingleton<ReadingPipeline>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReadingPipeline>());
builder.Services.AddHostedService<MqttIngestionService>();

builder.Services.AddHealthChecks()
    .AddCheck<PipelineHealthCheck>("pipeline", tags: [ServiceDefaultsExtensions.ReadyTag]);

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
