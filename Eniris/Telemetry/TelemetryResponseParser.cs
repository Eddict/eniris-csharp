#nullable enable
using System.Globalization;
using System.Text.Json.Nodes;
using Eniris.Configuration;
using Eniris.Models;

namespace Eniris.Telemetry;

/// <summary>
/// Parses Eniris telemetry responses into strongly associated device values.
/// </summary>
public static class TelemetryResponseParser
{
    /// <summary>
    /// Parses telemetry responses keyed by request statement id.
    /// </summary>
    public static IReadOnlyDictionary<TelemetrySensorKey, TelemetrySensorValue> ParseResponses(
        IReadOnlyList<TelemetryRequest> requests,
        IEnumerable<JsonObject> responses)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(responses);

        var values = new Dictionary<TelemetrySensorKey, TelemetrySensorValue>();
        foreach (var response in responses)
        {
            if (!TryReadInt(response["statement_id"], out var statementId) ||
                statementId < 0 ||
                statementId >= requests.Count)
            {
                continue;
            }

            if (response["error"] is not null)
            {
                continue;
            }

            var request = requests[statementId];
            if (response["series"] is not JsonArray seriesArray)
            {
                continue;
            }

            foreach (var series in seriesArray.OfType<JsonObject>())
            {
                var columns = ReadColumns(series["columns"] as JsonArray);
                var rows = series["values"] as JsonArray;
                if (columns.Count == 0 || rows?.Count is not > 0)
                {
                    continue;
                }

                if (rows[0] is not JsonArray row)
                {
                    continue;
                }

                var timestamp = ExtractTimestamp(columns, row);
                for (var index = 0; index < columns.Count && index < row.Count; index++)
                {
                    var column = columns[index];
                    if (column == "time" || !EnirisConstants.TelemetryFields.ContainsKey(column))
                    {
                        continue;
                    }

                    var rawValue = row[index];
                    if (rawValue is null)
                    {
                        continue;
                    }

                    var normalizedValue = NormalizeValue(column, rawValue);
                    var key = new TelemetrySensorKey(request.Device.Id, request.Source.Key, column);
                    values[key] = new TelemetrySensorValue(key, request.Device, request.Source, normalizedValue, timestamp);
                }
            }
        }

        return values;
    }

    private static IReadOnlyList<string> ReadColumns(JsonArray? columns) =>
        columns is null
            ? []
            : columns.Select(ReadString).Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value!).ToArray();

    private static string? ExtractTimestamp(IReadOnlyList<string> columns, JsonArray row)
    {
        var timeIndex = columns.ToList().IndexOf("time");
        if (timeIndex < 0 || timeIndex >= row.Count)
        {
            return null;
        }

        var node = row[timeIndex];
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(longValue).UtcDateTime.ToString("O");
            }

            if (value.TryGetValue<double>(out var doubleValue))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(doubleValue, CultureInfo.InvariantCulture)).UtcDateTime.ToString("O");
            }
        }

        return null;
    }

    private static object? NormalizeValue(string field, JsonNode rawValue)
    {
        if (field.EndsWith("_frac", StringComparison.Ordinal) && TryReadDouble(rawValue, out var fraction))
        {
            return fraction * 100d;
        }

        if (rawValue is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var boolValue))
            {
                return boolValue;
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return longValue;
            }

            if (value.TryGetValue<double>(out var doubleValue))
            {
                return doubleValue;
            }

            if (value.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }
        }

        return rawValue.ToJsonString();
    }

    private static bool TryReadDouble(JsonNode node, out double value)
    {
        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<double>(out value))
            {
                return true;
            }

            if (jsonValue.TryGetValue<long>(out var longValue))
            {
                value = longValue;
                return true;
            }
        }

        return double.TryParse(ReadString(node), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadInt(JsonNode? node, out int value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<int>(out value))
        {
            return true;
        }

        return int.TryParse(ReadString(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return longValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<double>(out var doubleValue))
            {
                return doubleValue.ToString(CultureInfo.InvariantCulture);
            }
        }

        return node?.ToJsonString().Trim('"');
    }
}

/// <summary>
/// Unique key for a telemetry sensor value.
/// </summary>
public readonly record struct TelemetrySensorKey(int DeviceId, string SourceKey, string Field)
{
    /// <summary>
    /// Gets a stable unique-id suffix.
    /// </summary>
    public string UniqueSuffix =>
        $"{DeviceId}_{SourceKey.Replace(':', '_').Replace(',', '_').Replace('=', '_')}_{Field}";
}

/// <summary>
/// Latest value and metadata for one telemetry field.
/// </summary>
public sealed record TelemetrySensorValue(
    TelemetrySensorKey Key,
    EnirisDevice Device,
    TelemetrySource Source,
    object? Value,
    string? Timestamp);
