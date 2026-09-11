#nullable enable
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
    public static JsonObject? BuildQuery(TelemetrySource source, IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fields);

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
            @namespace["retentionPolicy"] = source.RetentionPolicy;

            fromClause["retentionPolicy"] = source.RetentionPolicy;
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
            ["orderBy"] = "DESC",
            ["limit"] = 1,
        };

        if (source.Tags.Count > 0)
        {
            query["where"] = new JsonObject
            {
                ["tags"] = new JsonObject(source.Tags.Select(static pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))),
            };
        }

        return query;
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
