#nullable enable
using System.Text.Json.Nodes;

namespace Eniris.Models;

/// <summary>
/// A SmartgridOne controller and its child devices.
/// </summary>
public sealed class EnirisController
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnirisController"/> class.
    /// </summary>
    public EnirisController(EnirisDevice device, IReadOnlyList<EnirisDevice>? children = null)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        Children = children?.ToList() ?? [];
    }

    /// <summary>
    /// Gets the controller device.
    /// </summary>
    public EnirisDevice Device { get; }

    /// <summary>
    /// Gets the child devices associated with the controller.
    /// </summary>
    public List<EnirisDevice> Children { get; }

    /// <summary>
    /// Gets a stable controller id.
    /// </summary>
    public string Id => Device.NodeId;

    /// <summary>
    /// Gets the controller display name.
    /// </summary>
    public string Name => SerialNumber;

    /// <summary>
    /// Gets the controller serial number.
    /// </summary>
    public string SerialNumber => Device.SerialNumber;

    /// <summary>
    /// Groups devices under their discovered SmartgridOne controllers.
    /// </summary>
    public static IReadOnlyList<EnirisController> GroupControllers(IEnumerable<EnirisDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var deviceList = devices.ToList();
        var allControllers = deviceList
            .Where(static device => device.IsController)
            .ToDictionary(static device => device.NodeId, static device => device, StringComparer.Ordinal);

        var mergedSiteToParent = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (nodeId, device) in allControllers)
        {
            if (device.NodeType == "smartgridControllerSite" &&
                device.ControllerNodeId is { } parentNodeId &&
                allControllers.ContainsKey(parentNodeId))
            {
                mergedSiteToParent[nodeId] = parentNodeId;
            }
        }

        var controllers = new Dictionary<string, EnirisController>(StringComparer.Ordinal);
        foreach (var (nodeId, device) in allControllers)
        {
            if (!mergedSiteToParent.ContainsKey(nodeId))
            {
                controllers[nodeId] = new EnirisController(device);
            }
        }

        if (controllers.Count == 0 && deviceList.Count > 0)
        {
            controllers["eniris"] = new EnirisController(
                new EnirisDevice(
                    0,
                    null,
                    new JsonObject
                    {
                        ["nodeId"] = "eniris",
                        ["name"] = "Eniris SmartgridOne",
                    }));
        }

        foreach (var device in deviceList)
        {
            if (controllers.ContainsKey(device.NodeId) || !device.ShouldExposeAsDevice)
            {
                continue;
            }

            var controller = ResolveController(device, controllers, mergedSiteToParent);
            controller?.Children.Add(device);
        }

        return controllers.Values.ToArray();
    }

    private static EnirisController? ResolveController(
        EnirisDevice device,
        IReadOnlyDictionary<string, EnirisController> controllers,
        IReadOnlyDictionary<string, string> mergedSiteToParent)
    {
        var controllerId = device.ControllerNodeId;
        if (!string.IsNullOrWhiteSpace(controllerId))
        {
            if (mergedSiteToParent.TryGetValue(controllerId, out var mergedParent))
            {
                controllerId = mergedParent;
            }

            if (controllers.TryGetValue(controllerId, out var controller))
            {
                return controller;
            }
        }

        if (controllers.Count == 1)
        {
            return controllers.Values.First();
        }

        var flattenedStrings = FlattenStrings(device.Properties);
        foreach (var controller in controllers.Values)
        {
            var candidates = new[]
            {
                controller.Id,
                controller.SerialNumber,
                $"{controller.SerialNumber}_site_0",
            };

            if (candidates.Any(candidate => !string.IsNullOrWhiteSpace(candidate) && flattenedStrings.Contains(candidate)))
            {
                return controller;
            }

            if (candidates.Any(candidate => !string.IsNullOrWhiteSpace(candidate) && flattenedStrings.Any(value => value.Contains(candidate, StringComparison.Ordinal))))
            {
                return controller;
            }
        }

        return null;
    }

    private static HashSet<string> FlattenStrings(JsonNode? value)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        FlattenInto(value, result);
        return result;
    }

    private static void FlattenInto(JsonNode? value, ISet<string> result)
    {
        switch (value)
        {
            case JsonObject jsonObject:
                foreach (var pair in jsonObject)
                {
                    result.Add(pair.Key);
                    FlattenInto(pair.Value, result);
                }

                break;

            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    FlattenInto(item, result);
                }

                break;

            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var stringValue):
                result.Add(stringValue);
                break;

            case JsonValue jsonValue when jsonValue.TryGetValue<int>(out var intValue):
                result.Add(intValue.ToString());
                break;

            case JsonValue jsonValue when jsonValue.TryGetValue<long>(out var longValue):
                result.Add(longValue.ToString());
                break;

            case JsonValue jsonValue when jsonValue.TryGetValue<double>(out var doubleValue):
                result.Add(doubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;

            case JsonValue jsonValue when jsonValue.TryGetValue<bool>(out var boolValue):
                result.Add(boolValue.ToString());
                break;
        }
    }
}
