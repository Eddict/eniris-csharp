#nullable enable
using System.Text.Json.Nodes;
using Eniris.Examples.Helpers;
using Eniris.Examples.Models;
using Eniris.Models;
using Microsoft.Extensions.Logging;

namespace Eniris.Examples.Examples;

public sealed class DeviceDiscoveryExample
{
    private readonly IEnirisClient _client;
    private readonly ExampleConfig _config;
    private readonly ILogger<DeviceDiscoveryExample> _logger;

    public DeviceDiscoveryExample(IEnirisClient client, ExampleConfig config, ILogger<DeviceDiscoveryExample> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    public async Task<DeviceDiscoverySummary> RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching companies, roles, monitors, and devices");

        var companies = await _client.CompaniesAsync(cancellationToken).ConfigureAwait(false);
        var roles = await _client.RolesAsync(cancellationToken).ConfigureAwait(false);

        var monitorsByRole = new Dictionary<string, IReadOnlyList<JsonObject>>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            var roleId = ReadString(role["id"]) ?? ReadString(role["roleId"]);
            if (string.IsNullOrWhiteSpace(roleId))
            {
                continue;
            }

            monitorsByRole[roleId] = await _client.MonitorsAsync(roleId, cancellationToken).ConfigureAwait(false);
        }

        var devicesPayload = await _client.DevicesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var devices = EnirisDevice.ParseMany(devicesPayload).OrderBy(static device => device.Name, StringComparer.Ordinal).ToArray();
        var controllers = EnirisController.GroupControllers(devices).OrderBy(static controller => controller.Name, StringComparer.Ordinal).ToArray();
        LogDiscoveredDevices(devices);

        return new DeviceDiscoverySummary(companies, roles, monitorsByRole, devices, controllers);
    }

    private void LogDiscoveredDevices(IReadOnlyList<EnirisDevice> devices)
    {
        foreach (var device in devices)
        {
            _logger.LogDebug(
                "Discovered device {DeviceName} ({DeviceId}) type={NodeType} telemetrySources={TelemetrySourceCount}",
                device.Name,
                device.Id,
                string.IsNullOrWhiteSpace(device.NodeType) ? "<unknown>" : device.NodeType,
                device.TelemetrySources.Count);

            if (device.TelemetrySources.Count == 0)
            {
                _logger.LogDebug("Device {DeviceName} ({DeviceId}) has no telemetry sources.", device.Name, device.Id);
                continue;
            }

            foreach (var source in device.TelemetrySources)
            {
                var queryableFields = TelemetryFieldSelection.SelectFields(source, _config.PreferredTelemetryFields);
                var directFields = queryableFields.Take(1).ToArray();
                var sqlFields = queryableFields.Take(2).ToArray();

                _logger.LogDebug(
                    "Device {DeviceName} ({DeviceId}) source {SourceKey} measurement={Measurement} retentionPolicy={RetentionPolicy} advertisedFields=[{AdvertisedFields}] queryableFields=[{QueryableFields}] directHistoricalFields=[{DirectFields}] sqlStoreFields=[{SqlFields}]",
                    device.Name,
                    device.Id,
                    source.Key,
                    source.Measurement,
                    source.RetentionPolicy,
                    FormatFields(source.Fields, "<unspecified>"),
                    FormatFields(queryableFields, "<none>"),
                    FormatFields(directFields, "<none>"),
                    FormatFields(sqlFields, "<none>"));
            }
        }
    }

    private static string FormatFields(IEnumerable<string>? fields, string fallback)
    {
        var materialized = fields?
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return materialized is { Length: > 0 }
            ? string.Join(", ", materialized)
            : fallback;
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

public sealed record DeviceDiscoverySummary(
    IReadOnlyList<JsonObject> Companies,
    IReadOnlyList<JsonObject> Roles,
    IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> MonitorsByRole,
    IReadOnlyList<EnirisDevice> Devices,
    IReadOnlyList<EnirisController> Controllers);
