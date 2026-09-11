#nullable enable
using System.Globalization;
using System.Text.Json.Nodes;
using Eniris.Models;

namespace Eniris.Telemetry;

/// <summary>
/// Builds telemetry queries that match the Eniris SmartgridOne API shape.
/// </summary>
public static class TelemetryQueryBuilder
{
    /// <summary>
    /// Builds a single latest-value telemetry query for a source.
    /// </summary>
    public static JsonObject? BuildQuery(TelemetrySource source, IEnumerable<string> fields) =>
        BuildCoreQuery(source, fields, orderBy: "DESC", limit: 1, where: null);

    /// <summary>
    /// Builds a telemetry query for a historical time range.
    /// </summary>
    public static JsonObject? BuildHistoricalQuery(
        TelemetrySource source,
        IEnumerable<string> fields,
        DateTime from,
        DateTime to,
        JsonObject? where = null)
    {
        if (to <= from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), "The end of the historical range must be after the start.");
        }

        var mergedWhere = MergeWhere(
            BuildTimeRangeWhere(from, to),
            where);

        return BuildCoreQuery(source, fields, orderBy: "ASC", limit: null, mergedWhere);
    }

    /// <summary>
    /// Builds request descriptors for all telemetry sources exposed by a device set.
    /// </summary>
    public static IReadOnlyList<TelemetryRequest> BuildRequests(IEnumerable<EnirisDevice> devices, IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(fields);

        var requests = new List<TelemetryRequest>();
        foreach (var device in devices)
        {
            foreach (var source in device.TelemetrySources)
            {
                var query = BuildQuery(source, fields);
                if (query is not null)
                {
                    requests.Add(new TelemetryRequest(device, source, query));
                }
            }
        }

        return requests;
    }

    private static JsonObject? BuildCoreQuery(
        TelemetrySource source,
        IEnumerable<string> fields,
        string orderBy,
        int? limit,
        JsonObject? where)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderBy);

        var selectedFields = fields
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Where(field => source.Fields is null || source.Fields.Contains(field, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (selectedFields.Length == 0)
        {
            return null;
        }

        var fromClause = new JsonObject
        {
            ["measurement"] = source.Measurement,
        };

        if (!string.IsNullOrWhiteSpace(source.Database))
        {
            fromClause["database"] = source.Database;
            fromClause["retentionPolicy"] = source.RetentionPolicy;
        }
        else if (source.Namespace is not null)
        {
            var @namespace = (JsonObject)source.Namespace.DeepClone();
            if (string.Equals(ReadString(@namespace["version"]), "1", StringComparison.Ordinal))
            {
                @namespace["retentionPolicy"] = source.RetentionPolicy;
            }

            fromClause["namespace"] = @namespace;
        }
        else
        {
            return null;
        }

        var query = new JsonObject
        {
            ["select"] = new JsonArray(selectedFields.Select(static field => (JsonNode?)JsonValue.Create(field)).ToArray()),
            ["from"] = fromClause,
            ["orderBy"] = orderBy,
        };

        if (limit is { } rowLimit)
        {
            query["limit"] = rowLimit;
        }

        var effectiveWhere = MergeWhere(BuildTagsWhere(source), where);
        if (effectiveWhere?.Count > 0)
        {
            query["where"] = effectiveWhere;
        }

        return query;
    }

    private static JsonObject? BuildTagsWhere(TelemetrySource source) =>
        source.Tags.Count == 0
            ? null
            : new JsonObject
            {
                ["tags"] = new JsonObject(source.Tags.Select(static pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))),
            };

    private static JsonObject BuildTimeRangeWhere(DateTime from, DateTime to) =>
        new()
        {
            ["time"] = new JsonObject
            {
                ["from"] = NormalizeTimestamp(from),
                ["to"] = NormalizeTimestamp(to),
            },
        };

    private static JsonObject? MergeWhere(JsonObject? first, JsonObject? second)
        => MergeNode(first, second) as JsonObject;

    private static JsonNode? MergeNode(JsonNode? first, JsonNode? second)
    {
        if (first is null)
        {
            return second?.DeepClone();
        }

        if (second is null)
        {
            return first.DeepClone();
        }

        if (first is JsonObject firstObject && second is JsonObject secondObject)
        {
            var merged = (JsonObject)firstObject.DeepClone();
            foreach (var pair in secondObject)
            {
                merged[pair.Key] = MergeNode(merged[pair.Key], pair.Value);
            }

            return merged;
        }

        return second.DeepClone();
    }

    private static string NormalizeTimestamp(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value.ToString("O", CultureInfo.InvariantCulture)
            : value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? ReadString(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }

        return node?.ToJsonString().Trim('"');
    }
}

/// <summary>
/// Couples a device and source to the JSON query sent to the telemetry endpoint.
/// </summary>
public sealed record TelemetryRequest(EnirisDevice Device, TelemetrySource Source, JsonObject Query);
