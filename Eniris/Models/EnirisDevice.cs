#nullable enable
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Eniris.Configuration;

namespace Eniris.Models;

/// <summary>
/// A device returned by the Eniris metadata API.
/// </summary>
public sealed class EnirisDevice
{
    private static readonly Regex SiteSuffixRegex = new("_site_\\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Initializes a new instance of the <see cref="EnirisDevice"/> class.
    /// </summary>
    public EnirisDevice(int id, string? lastUpdate, JsonObject? properties = null, JsonObject? userRights = null)
    {
        Id = id;
        LastUpdate = lastUpdate;
        Properties = properties ?? [];
        UserRights = userRights ?? [];
    }

    /// <summary>
    /// Gets the Eniris device identifier.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Gets the last update timestamp returned by the metadata API.
    /// </summary>
    public string? LastUpdate { get; }

    /// <summary>
    /// Gets the raw metadata properties for the device.
    /// </summary>
    public JsonObject Properties { get; }

    /// <summary>
    /// Gets the raw user-rights payload for the device.
    /// </summary>
    public JsonObject UserRights { get; }

    /// <summary>
    /// Gets the best available node identifier.
    /// </summary>
    public string NodeId => FirstString(Properties, "nodeId", "node_id", "id") ?? Id.ToString();

    /// <summary>
    /// Gets the Eniris node type.
    /// </summary>
    public string NodeType => FirstString(Properties, "nodeType", "node_type", "type") ?? string.Empty;

    /// <summary>
    /// Gets the user-facing device name.
    /// </summary>
    public string Name =>
        FirstString(Properties, "name", "displayName", "display_name", "label", "description")
        ?? NestedString(Properties, "info", "name")
        ?? NestedString(Properties, "location", "name")
        ?? NodeId;

    /// <summary>
    /// Gets the device manufacturer if one is available.
    /// </summary>
    public string? Manufacturer =>
        FirstString(Properties, "manufacturer", "brand", "vendor")
        ?? NestedString(Properties, "info", "manufacturer");

    /// <summary>
    /// Gets the device model if one is available.
    /// </summary>
    public string? Model =>
        FirstString(Properties, "model", "modelName", "deviceModel")
        ?? NestedString(Properties, "info", "model");

    /// <summary>
    /// Gets the best available serial number.
    /// </summary>
    public string SerialNumber =>
        CleanControllerSerial(
            FirstString(
                Properties,
                "serialNumber",
                "serial_number",
                "serial",
                "serialNo",
                "controllerSerial",
                "controller_serial",
                "hardwareSerial",
                "hardware_serial")
            ?? NestedString(Properties, "info", "serialNumber")
            ?? NodeId);

    /// <summary>
    /// Gets a value indicating whether the device looks like a SmartgridOne controller.
    /// </summary>
    public bool IsController
    {
        get
        {
            var product = FirstString(Properties, "product", "productName", "deviceClass");
            var haystack = string.Join(
                ' ',
                new[] { NodeType, Name, Model, product }
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value!.ToLowerInvariant()));

            return haystack.Contains("smartgridone", StringComparison.Ordinal) ||
                   haystack.Contains("smartgrid one", StringComparison.Ordinal) ||
                   haystack.Contains("smartgrid-one", StringComparison.Ordinal) ||
                   haystack.Contains("controller", StringComparison.Ordinal) ||
                   haystack.Contains("rp_one", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Gets the controller node id for this device, if one can be inferred.
    /// </summary>
    public string? ControllerNodeId
    {
        get
        {
            var direct = FirstString(
                Properties,
                "controllerNodeId",
                "controller_node_id",
                "parentNodeId",
                "parent_node_id",
                "gatewayNodeId",
                "gateway_node_id",
                "edgeNodeId",
                "edge_node_id",
                "smartgridOneNodeId",
                "smartgridoneNodeId");

            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            direct = NestedString(Properties, "controller", "nodeId");
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            if (Properties["nodeParentsIds"] is JsonArray parentIds)
            {
                return parentIds.Select(ReadString).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
            }

            return null;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the device should be exposed as a user-facing device.
    /// </summary>
    public bool ShouldExposeAsDevice =>
        !EnirisConstants.ExcludedDeviceNodeTypes.Contains(NodeType) && !IsController;

    /// <summary>
    /// Gets telemetry tags found on the device metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags
    {
        get
        {
            foreach (var key in new[] { "telemetryTags", "telemetry_tags", "tags", "monitoringTags" })
            {
                if (Properties[key] is JsonObject tagObject)
                {
                    return tagObject
                        .Where(static pair => pair.Value is not null)
                        .ToDictionary(static pair => pair.Key, static pair => ReadString(pair.Value) ?? string.Empty, StringComparer.Ordinal);
                }
            }

            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nodeId"] = NodeId,
                ["deviceId"] = Id.ToString(),
            };
        }
    }

    /// <summary>
    /// Gets all telemetry sources that can be derived from the device metadata.
    /// </summary>
    public IReadOnlyList<TelemetrySource> TelemetrySources
    {
        get
        {
            var explicitSeries = NodeInfluxSeriesSources(Properties);
            if (explicitSeries.Count > 0)
            {
                return explicitSeries;
            }

            var explicitSources = ExplicitSources(Properties);
            if (explicitSources.Count > 0)
            {
                return explicitSources;
            }

            var database = FirstString(Properties, "database", "telemetryDatabase", "db");
            var @namespace = NamespaceFromProperties(Properties);
            if (database is null && @namespace is null)
            {
                return [];
            }

            var measurements = MeasurementsFromProperties(Properties);
            var retentionPolicies = RetentionPoliciesFromProperties(Properties);
            return
            [
                ..from measurement in measurements
                from retentionPolicy in retentionPolicies
                select new TelemetrySource(
                    measurement,
                    retentionPolicy,
                    Tags,
                    database,
                    @namespace is null ? null : (JsonObject)@namespace.DeepClone())
            ];
        }
    }

    /// <summary>
    /// Parses a <c>/v1/device/query</c> response into device models.
    /// </summary>
    public static IReadOnlyList<EnirisDevice> ParseMany(JsonObject? payload)
    {
        if (payload?["device"] is not JsonArray devices)
        {
            return [];
        }

        var result = new List<EnirisDevice>(devices.Count);
        foreach (var item in devices.OfType<JsonObject>())
        {
            if (!TryReadInt(item["id"], out var id))
            {
                continue;
            }

            result.Add(
                new EnirisDevice(
                    id,
                    ReadString(item["lastUpdate"]),
                    item["properties"] as JsonObject,
                    item["userRights"] as JsonObject));
        }

        return result;
    }

    /// <summary>
    /// Removes Eniris site suffixes from a controller serial number.
    /// </summary>
    public static string CleanControllerSerial(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SiteSuffixRegex.Replace(value, string.Empty);
    }

    private static IReadOnlyList<TelemetrySource> ExplicitSources(JsonObject properties)
    {
        if (FirstNode(properties, "telemetrySources", "telemetry_sources", "sources") is not JsonArray rawSources)
        {
            return [];
        }

        var sources = new List<TelemetrySource>(rawSources.Count);
        foreach (var raw in rawSources.OfType<JsonObject>())
        {
            var measurement = ReadString(raw["measurement"]);
            var retentionPolicy = ReadString(raw["retentionPolicy"]) ?? ReadString(raw["retention_policy"]);
            if (string.IsNullOrWhiteSpace(measurement) ||
                string.IsNullOrWhiteSpace(retentionPolicy) ||
                !EnirisConstants.RetentionPolicies.Contains(retentionPolicy, StringComparer.Ordinal))
            {
                continue;
            }

            sources.Add(
                new TelemetrySource(
                    measurement,
                    retentionPolicy,
                    ReadTags(raw["tags"] as JsonObject),
                    ReadString(raw["database"]),
                    raw["namespace"] as JsonObject,
                    TelemetryFields(raw["fields"] as JsonArray)));
        }

        return sources;
    }

    private static IReadOnlyList<TelemetrySource> NodeInfluxSeriesSources(JsonObject properties)
    {
        if (properties["nodeInfluxSeries"] is not JsonArray rawSources)
        {
            return [];
        }

        var sources = new List<TelemetrySource>(rawSources.Count);
        foreach (var raw in rawSources.OfType<JsonObject>())
        {
            var retentionPolicy = ReadString(raw["retentionPolicy"]);
            var measurement = ReadString(raw["measurement"]);
            var database = ReadString(raw["database"]);
            var fields = TelemetryFields(raw["fields"] as JsonArray);
            if (string.IsNullOrWhiteSpace(retentionPolicy) ||
                string.IsNullOrWhiteSpace(measurement) ||
                string.IsNullOrWhiteSpace(database) ||
                fields is null ||
                !EnirisConstants.RetentionPolicies.Contains(retentionPolicy, StringComparer.Ordinal))
            {
                continue;
            }

            sources.Add(
                new TelemetrySource(
                    measurement,
                    retentionPolicy,
                    ReadTags(raw["tags"] as JsonObject),
                    database,
                    null,
                    fields));
        }

        return sources;
    }

    private static IReadOnlyList<string>? TelemetryFields(JsonArray? rawFields)
    {
        if (rawFields is null)
        {
            return null;
        }

        var fields = rawFields
            .Select(ReadString)
            .Where(static field => !string.IsNullOrWhiteSpace(field) && EnirisConstants.TelemetryFields.ContainsKey(field))
            .Select(static field => field!)
            .ToArray();

        return fields.Length == 0 ? null : fields;
    }

    private static IReadOnlyList<string> MeasurementsFromProperties(JsonObject properties)
    {
        var rawMeasurement = FirstNode(properties, "measurements", "measurement", "telemetryMeasurements");
        if (rawMeasurement is JsonArray arrayMeasurement)
        {
            var values = arrayMeasurement.Select(ReadString).Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item!).ToArray();
            if (values.Length > 0)
            {
                return values;
            }
        }

        if (ReadString(rawMeasurement) is { } singleMeasurement)
        {
            return [singleMeasurement];
        }

        var nodeType = (FirstString(properties, "nodeType", "type") ?? string.Empty).ToLowerInvariant();
        if (nodeType.Contains("solar", StringComparison.Ordinal) && nodeType.Contains("string", StringComparison.Ordinal))
        {
            return ["solarStringMetrics"];
        }

        if (nodeType.Contains("solar", StringComparison.Ordinal))
        {
            return ["solarInverterMetrics"];
        }

        if (nodeType.Contains("battery", StringComparison.Ordinal))
        {
            return ["batteryMetrics"];
        }

        if (nodeType.Contains("ev", StringComparison.Ordinal) || nodeType.Contains("charger", StringComparison.Ordinal))
        {
            return ["evChargerMetrics"];
        }

        if (nodeType.Contains("hybrid", StringComparison.Ordinal))
        {
            return ["hybridInverterMetrics"];
        }

        if (nodeType.Contains("grid", StringComparison.Ordinal) || nodeType.Contains("submeter", StringComparison.Ordinal))
        {
            return ["submeteringMetrics"];
        }

        return EnirisConstants.DefaultMeasurements;
    }

    private static IReadOnlyList<string> RetentionPoliciesFromProperties(JsonObject properties)
    {
        var rawPolicy = FirstNode(properties, "retentionPolicy", "retention_policy", "retentionPolicies");
        if (rawPolicy is JsonArray arrayPolicy)
        {
            var values = arrayPolicy
                .Select(ReadString)
                .Where(static item => !string.IsNullOrWhiteSpace(item) && EnirisConstants.RetentionPolicies.Contains(item, StringComparer.Ordinal))
                .Select(static item => item!)
                .ToArray();
            if (values.Length > 0)
            {
                return values;
            }
        }

        if (ReadString(rawPolicy) is { } singlePolicy &&
            EnirisConstants.RetentionPolicies.Contains(singlePolicy, StringComparer.Ordinal))
        {
            return [singlePolicy];
        }

        return EnirisConstants.RetentionPolicies;
    }

    private static JsonObject? NamespaceFromProperties(JsonObject properties)
    {
        var raw = FirstNode(properties, "namespace", "telemetryNamespace") as JsonObject;
        if (raw is not null)
        {
            return (JsonObject)raw.DeepClone();
        }

        var organization = FirstString(properties, "organization", "telemetryOrganization");
        var bucket = FirstString(properties, "bucket", "telemetryBucket");
        if (!string.IsNullOrWhiteSpace(organization) && !string.IsNullOrWhiteSpace(bucket))
        {
            return new JsonObject
            {
                ["version"] = "2",
                ["organization"] = organization,
                ["bucket"] = bucket,
            };
        }

        var ioxNamespace = FirstString(properties, "ioxNamespace", "telemetryIoxNamespace");
        if (!string.IsNullOrWhiteSpace(ioxNamespace))
        {
            return new JsonObject
            {
                ["version"] = "IOx",
                ["value"] = ioxNamespace,
            };
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> ReadTags(JsonObject? tagObject) =>
        tagObject is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : tagObject
                .Where(static pair => pair.Value is not null)
                .ToDictionary(static pair => pair.Key, static pair => ReadString(pair.Value) ?? string.Empty, StringComparer.Ordinal);

    private static JsonNode? FirstNode(JsonObject data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (data[key] is { } node &&
                (node is not JsonValue value || !value.TryGetValue<string>(out var textValue) || !string.IsNullOrWhiteSpace(textValue)))
            {
                return node;
            }
        }

        return null;
    }

    private static string? FirstString(JsonObject data, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = ReadString(data[key]);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? NestedString(JsonObject data, params string[] path)
    {
        JsonNode? current = data;
        foreach (var part in path)
        {
            current = current?[part];
        }

        return ReadString(current);
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue.ToString();
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return longValue.ToString();
            }

            if (value.TryGetValue<double>(out var doubleValue))
            {
                return doubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<bool>(out var boolValue))
            {
                return boolValue.ToString();
            }
        }

        return node.ToJsonString().Trim('"');
    }

    private static bool TryReadInt(JsonNode? node, out int value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<int>(out value))
        {
            return true;
        }

        return int.TryParse(ReadString(node), out value);
    }
}
