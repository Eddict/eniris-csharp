#nullable enable
using System.Globalization;
using System.Text.Json.Nodes;
using Eniris.Helpers;
using Eniris.Models;
using Eniris.Telemetry;
using Microsoft.Extensions.Logging;

namespace Eniris.Examples;

public sealed class TelemetryQueryBuilderExample
{
    private readonly ILogger<TelemetryQueryBuilderExample> _logger;

    public TelemetryQueryBuilderExample(ILogger<TelemetryQueryBuilderExample> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<(string Name, JsonObject Query)> BuildExamples(TelemetrySource? source = null)
    {
        var now = DateTimeOffset.UtcNow;
        var start = now.AddDays(-7);
        var sampleSource = source ?? new TelemetrySource(
            measurement: "solarInverterMetrics",
            retentionPolicy: "rp_one_m",
            tags: new Dictionary<string, string>(StringComparer.Ordinal) { ["nodeId"] = "inverter-1" },
            database: "site_telemetry");

        var latest = TelemetryQueryBuilder.BuildQuery(sampleSource, ["actualPowerTot_W", "voltageL1N_V"])
            ?? throw new InvalidOperationException("Failed to build a latest-value example query.");

        var historical = TelemetryQueryBuilder.BuildHistoricalQuery(sampleSource, ["actualPowerTot_W", "voltageL1N_V"], start.UtcDateTime, now.UtcDateTime)
            ?? throw new InvalidOperationException("Failed to build a historical example query.");

        var exactTimeWhere = historical["where"] as JsonObject ?? new JsonObject();
        exactTimeWhere["time"] = new JsonArray(
            new JsonObject { ["operator"] = ">=", ["value"] = start.ToString("O", CultureInfo.InvariantCulture) },
            new JsonObject { ["operator"] = "<=", ["value"] = now.ToString("O", CultureInfo.InvariantCulture) });
        historical["where"] = exactTimeWhere;
        historical["limit"] = 10000;

        var namespaceSource = new TelemetrySource(
            measurement: sampleSource.Measurement,
            retentionPolicy: sampleSource.RetentionPolicy,
            tags: sampleSource.Tags,
            @namespace: new JsonObject
            {
                ["version"] = "2",
                ["organization"] = "smartgridone",
                ["bucket"] = "telemetry",
            });

        var aggregated = new JsonObject
        {
            ["select"] = new JsonArray(
                new JsonObject { ["field"] = "actualPowerTot_W", ["function"] = "mean" },
                new JsonObject { ["field"] = "actualPowerTot_W", ["function"] = "max" },
                JsonValue.Create("voltageL1N_V")),
            ["from"] = new JsonObject
            {
                ["measurement"] = namespaceSource.Measurement,
                ["namespace"] = namespaceSource.Namespace!.DeepClone(),
            },
            ["where"] = new JsonObject
            {
                ["time"] = new JsonArray(
                    new JsonObject { ["operator"] = ">=", ["value"] = start.ToString("O", CultureInfo.InvariantCulture) },
                    new JsonObject { ["operator"] = "<=", ["value"] = now.ToString("O", CultureInfo.InvariantCulture) }),
                ["tags"] = new JsonObject(namespaceSource.Tags.Select(static pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))),
            },
            ["orderBy"] = "ASC",
            ["limit"] = 1440,
        };

        _logger.LogInformation("Built sample latest, historical, and aggregated telemetry queries");
        return
        [
            ("Latest telemetry query", latest),
            ("Historical telemetry query with where.time", historical),
            ("Namespace + aggregated telemetry query", aggregated),
        ];
    }
}
