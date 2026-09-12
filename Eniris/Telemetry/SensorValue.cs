#nullable enable
using Eniris.Models;

namespace Eniris.Telemetry;

/// <summary>
/// Telemetry value and metadata for one field on one device.
/// </summary>
public sealed record SensorValue(
    SensorKey Key,
    EnirisDevice Device,
    TelemetrySource Source,
    object Value,
    DateTimeOffset Timestamp);
