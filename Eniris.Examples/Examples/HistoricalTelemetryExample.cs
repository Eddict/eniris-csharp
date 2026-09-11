#nullable enable
using System.Globalization;
using System.Text.Json.Nodes;
using Eniris.Data;
using Eniris.Models;
using Eniris.Telemetry;
using Microsoft.Extensions.Logging;

namespace Eniris.Examples.Examples;

public sealed class HistoricalTelemetryExample
{
    private readonly IEnirisClient _client;
    private readonly ILogger<HistoricalTelemetryExample> _logger;

    public HistoricalTelemetryExample(IEnirisClient client, ILogger<HistoricalTelemetryExample> logger)
    {
        _client = client;
        _logger = logger;
    }

    public JsonObject BuildTimeRangeQuery(
        TelemetrySource source,
        IReadOnlyList<string> fields,
        DateTimeOffset start,
        DateTimeOffset end,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fields);

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "The telemetry limit must be greater than zero.");
        }

        var query = TelemetryQueryBuilder.BuildHistoricalQuery(source, fields, start.UtcDateTime, end.UtcDateTime)
            ?? throw new InvalidOperationException($"Unable to build a historical query for source '{source.Key}'.");

        var where = query["where"] as JsonObject ?? new JsonObject();
        where["time"] = new JsonArray(
            new JsonObject
            {
                ["operator"] = ">=",
                ["value"] = start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            },
            new JsonObject
            {
                ["operator"] = "<=",
                ["value"] = end.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            });
        query["where"] = where;
        query["limit"] = limit;
        query["orderBy"] = "ASC";
        return query;
    }

    public async Task<IReadOnlyList<TelemetryRecord>> RunDirectRangeQueryAsync(
        EnirisDevice device,
        TelemetrySource source,
        IReadOnlyList<string> fields,
        DateTimeOffset start,
        DateTimeOffset end,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var query = BuildTimeRangeQuery(source, fields, start, end, limit);
        var request = new TelemetryRequest(device, source, query);

        _logger.LogInformation("Fetching historical telemetry directly for {DeviceName} from {Start} to {End}", device.Name, start, end);
        var responses = await _client.TelemetryAsync([query], cancellationToken).ConfigureAwait(false);
        return TelemetryResponseParser.ParseHistorical([request], responses)
            .OrderBy(static record => record.Timestamp)
            .ToArray();
    }

    public async Task<IReadOnlyList<TelemetryRecord>> RunChunkedRangeQueryAsync(
        EnirisDevice device,
        TelemetrySource source,
        string[] fields,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fields);

        var fetcher = new HistoricalDataFetcher(_client);
        _logger.LogInformation("Fetching chunked historical telemetry for {DeviceName} from {Start} to {End}", device.Name, start, end);
        var records = await fetcher.FetchAsync(device, source, fields, start.UtcDateTime, end.UtcDateTime, cancellationToken: cancellationToken).ConfigureAwait(false);
        return records.OrderBy(static record => record.Timestamp).ToArray();
    }
}
