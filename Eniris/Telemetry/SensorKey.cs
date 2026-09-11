#nullable enable

namespace Eniris.Telemetry;

/// <summary>
/// Unique key for a telemetry field on a specific device/source pair.
/// </summary>
public readonly record struct SensorKey(int DeviceId, string SourceKey, string Field)
{
    /// <summary>
    /// Gets a stable unique-id suffix.
    /// </summary>
    public string UniqueSuffix =>
        $"{DeviceId}_{SourceKey.Replace(':', '_').Replace(',', '_').Replace('=', '_')}_{Field}";
}
