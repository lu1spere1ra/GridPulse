using System.Text.Json;
using System.Text.Json.Serialization;

namespace GridPulse.Core.Contracts;

/// <summary>
/// Source-generated serializers for every message on the wire: no reflection, no runtime
/// code generation, and fewer allocations on the ingestion hot path.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ReadingMessage))]
[JsonSerializable(typeof(FleetSnapshot))]
[JsonSerializable(typeof(MeterLatestBatch))]
[JsonSerializable(typeof(WindowClosed))]
[JsonSerializable(typeof(IReadOnlyList<WindowClosed>))]
[JsonSerializable(typeof(AlertNotification))]
public sealed partial class GridPulseJsonContext : JsonSerializerContext;
