using GridPulse.Infrastructure.Messaging;
using GridPulse.ServiceDefaults;
using GridPulse.Simulator;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddOptions<SimulatorOptions>()
    .BindConfiguration(SimulatorOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<MqttOptions>().BindConfiguration(MqttOptions.SectionName);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IReadingPublisher, MqttReadingPublisher>();

var apiBaseUrl = builder.Configuration.GetValue<Uri?>($"{SimulatorOptions.SectionName}:{nameof(SimulatorOptions.ApiBaseUrl)}");
if (apiBaseUrl is not null)
{
    builder.Services.AddHttpClient<MeterProvisioner>(http => http.BaseAddress = apiBaseUrl);
}

builder.Services.AddHostedService<SimulatorWorker>();

builder.Build().Run();
