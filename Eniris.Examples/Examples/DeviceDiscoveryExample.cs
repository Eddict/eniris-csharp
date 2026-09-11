#nullable enable
using System.Text.Json.Nodes;
using Eniris.Models;
using Microsoft.Extensions.Logging;

namespace Eniris.Examples.Examples;

public sealed class DeviceDiscoveryExample
{
    private readonly IEnirisClient _client;
    private readonly ILogger<DeviceDiscoveryExample> _logger;

    public DeviceDiscoveryExample(IEnirisClient client, ILogger<DeviceDiscoveryExample> logger)
    {
        _client = client;
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

        return new DeviceDiscoverySummary(companies, roles, monitorsByRole, devices, controllers);
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
