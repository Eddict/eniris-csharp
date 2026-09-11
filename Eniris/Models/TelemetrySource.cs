#nullable enable
using System.Text.Json.Nodes;

namespace Eniris.Models;

/// <summary>
/// A concrete Eniris telemetry source for a device.
/// </summary>
public sealed class TelemetrySource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetrySource"/> class.
    /// </summary>
    public TelemetrySource(
        string measurement,
        string retentionPolicy,
        IReadOnlyDictionary<string, string>? tags = null,
        string? database = null,
        JsonObject? @namespace = null,
        IReadOnlyList<string>? fields = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(measurement);
        ArgumentException.ThrowIfNullOrWhiteSpace(retentionPolicy);

        Measurement = measurement;
        RetentionPolicy = retentionPolicy;
        Tags = tags ?? new Dictionary<string, string>(StringComparer.Ordinal);
        Database = database;
        Namespace = @namespace;
        Fields = fields;
    }

    /// <summary>
    /// Gets the telemetry measurement name.
    /// </summary>
    public string Measurement { get; }

    /// <summary>
    /// Gets the retention policy used by the telemetry source.
    /// </summary>
    public string RetentionPolicy { get; }

    /// <summary>
    /// Gets the telemetry filter tags.
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; }

    /// <summary>
    /// Gets the database name for classic Influx telemetry sources.
    /// </summary>
    public string? Database { get; }

    /// <summary>
    /// Gets the namespace definition for InfluxDB v1/v2 or IOx sources.
    /// </summary>
    public JsonObject? Namespace { get; }

    /// <summary>
    /// Gets the optional whitelist of telemetry fields that are valid for this source.
    /// </summary>
    public IReadOnlyList<string>? Fields { get; }

    /// <summary>
    /// Gets a stable key for this telemetry source.
    /// </summary>
    public string Key
    {
        get
        {
            var scope = Database
                ?? ReadString(Namespace?["value"])
                ?? ReadString(Namespace?["bucket"])
                ?? "default";
            var tags = string.Join(",", Tags.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
            return $"{Measurement}:{RetentionPolicy}:{scope}:{tags}";
        }
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        return node?.ToJsonString().Trim('"');
    }
}
