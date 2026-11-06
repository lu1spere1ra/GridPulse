using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace GridPulse.IntegrationTests.Messaging;

/// <summary>A throwaway Mosquitto broker for tests that need real MQTT.</summary>
public sealed class MosquittoBroker : IAsyncLifetime
{
    private const int MqttPort = 1883;

    private readonly IContainer _container = new ContainerBuilder("eclipse-mosquitto:2.0.22")
        .WithPortBinding(MqttPort, assignRandomHostPort: true)
        // The image ships a config that listens on all interfaces without authentication.
        .WithCommand("mosquitto", "-c", "/mosquitto-no-auth.conf")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(MqttPort))
        .Build();

    public string Host => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(MqttPort);

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
