#nullable enable
using System.Globalization;
using System.Text.Json.Nodes;
using Eniris.Configuration;
using Eniris.Data;
using Eniris.Models;

namespace Eniris.Telemetry;

/// <summary>
/// Parses Eniris telemetry responses into strongly associated device values.
/// </summary>
public static class TelemetryResponseParser
{
    /// <summary>
    /// Parses latest-value telemetry responses keyed by request statement id.
    /// </summary>
    public static IEnumerable<SensorValue> Parse(
        IReadOnlyList<TelemetryRequest> requests,
        IEnumerable<JsonObject> responses)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(responses);

        var values = new Dictionary<SensorKey, SensorValue>();
        foreach (var value in ParseCore(requests, responses, latestOnly: true, restrictToKnownFields: true))
        {
            values[value.Key] = value;
        }

        return values.Values;
    }

    /// <summary>
    /// Parses latest-value telemetry responses keyed by request statement id.
    /// </summary>
    public static IReadOnlyDictionary<SensorKey, SensorValue> ParseResponses(
        IReadOnlyList<TelemetryRequest> requests,
        IEnumerable<JsonObject> responses)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(responses);

        var values = new Dictionary<SensorKey, SensorValue>();
        foreach (var value in Parse(requests, responses))
        {
            values[value.Key] = value;
        }

        return values;
    }

    /// <summary>
    /// Parses telemetry responses into historical database records.
    /// </summary>
    public static IEnumerable<TelemetryRecord> ParseHistorical(
        IReadOnlyList<TelemetryRequest> requests,
        IEnumerable<JsonObject> responses)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(responses);

        foreach (var value in ParseCore(requests, responses, latestOnly: false, restrictToKnownFields: false))
        {
            if (value.Timestamp is null)
            {
                continue;
            }

            yield return TelemetryRecordMapper.MapSensorValue(value, value.Device, value.Source);
        }
    }

    private static IEnumerable<SensorValue> ParseCore(
        IReadOnlyList<TelemetryRequest> requests,
        IEnumerable<JsonObject> responses,
        bool latestOnly,
        bool restrictToKnownFields)
    {
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

                IEnumerable<JsonArray> dataRows = latestOnly
                    ? SelectLatestRow(columns, rows) is JsonArray latestRow ? [latestRow] : []
                    : rows.OfType<JsonArray>();

                foreach (var row in dataRows)
                {
                    var timestamp = ExtractTimestamp(columns, row);
                    for (var index = 0; index < columns.Count && index < row.Count; index++)
                    {
                        var column = columns[index];
                        if (column == "time" ||
                            (restrictToKnownFields && !EnirisConstants.TelemetryFields.ContainsKey(column)))
                        {
                            continue;
                        }

                        var rawValue = row[index];
                        if (rawValue is null)
                        {
                            continue;
                        }

                        var normalizedValue = NormalizeValue(column, rawValue);
                        var key = new SensorKey(request.Device.Id, request.Source.Key, column);
                        yield return new SensorValue(key, request.Device, request.Source, normalizedValue, timestamp);
                    }
                }
            }
        }
    }

    private static IReadOnlyList<string> ReadColumns(JsonArray? columns) =>
        columns is null
            ? []
            : columns.Select(ReadString).Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value!).ToArray();

    private static JsonArray? SelectLatestRow(IReadOnlyList<string> columns, JsonArray rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var timeIndex = columns.ToList().IndexOf("time");
        if (timeIndex < 0)
        {
            return rows[^1] as JsonArray;
        }

        JsonArray? latestRow = null;
        DateTimeOffset? latestTimestamp = null;
        foreach (var candidate in rows.OfType<JsonArray>())
        {
            var timestamp = ExtractTimestampValue(timeIndex, candidate);
            if (timestamp is null)
            {
                latestRow ??= candidate;
                continue;
            }

            if (latestTimestamp is null || timestamp > latestTimestamp)
            {
                latestTimestamp = timestamp;
                latestRow = candidate;
            }
        }

        return latestRow ?? rows[^1] as JsonArray;
    }

    private static DateTimeOffset? ExtractTimestamp(IReadOnlyList<string> columns, JsonArray row)
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
                if (DateTimeOffset.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    return parsed.ToUniversalTime();
                }
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(longValue);
            }

            if (value.TryGetValue<double>(out var doubleValue))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(doubleValue, CultureInfo.InvariantCulture));
            }
        }

        return null;
    }

    private static DateTimeOffset? ExtractTimestampValue(int timeIndex, JsonArray row)
    {
        if (timeIndex < 0 || timeIndex >= row.Count)
        {
            return null;
        }

        var node = row[timeIndex];
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var stringValue) &&
                DateTimeOffset.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed.ToUniversalTime();
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(longValue);
            }

            if (value.TryGetValue<double>(out var doubleValue))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(doubleValue, CultureInfo.InvariantCulture));
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
