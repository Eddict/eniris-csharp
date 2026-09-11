#nullable enable
using System.Collections.ObjectModel;

namespace Eniris.Configuration;

/// <summary>
/// Shared constants for the Eniris authentication, device discovery, and telemetry APIs.
/// </summary>
public static class EnirisConstants
{
    /// <summary>
    /// Default Eniris authentication base URL.
    /// </summary>
    public static readonly Uri DefaultAuthBaseUri = new("https://authentication.eniris.be");

    /// <summary>
    /// Default Eniris API base URL.
    /// </summary>
    public static readonly Uri DefaultApiBaseUri = new("https://api.eniris.be");

    /// <summary>
    /// Refresh-token configuration field name.
    /// </summary>
    public const string RefreshTokenField = "refresh_token";

    /// <summary>
    /// Refresh-token timestamp field name.
    /// </summary>
    public const string RefreshTokenCreatedAtField = "refresh_token_created_at";

    /// <summary>
    /// Username configuration field name.
    /// </summary>
    public const string UsernameField = "username";

    /// <summary>
    /// Controller identifier configuration field name.
    /// </summary>
    public const string ControllerIdField = "controller_id";

    /// <summary>
    /// Controller serial configuration field name.
    /// </summary>
    public const string ControllerSerialField = "controller_serial";

    /// <summary>
    /// Default polling interval used by the upstream integration.
    /// </summary>
    public static readonly TimeSpan DefaultScanInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Refresh-token renewal interval used by the upstream integration.
    /// </summary>
    public static readonly TimeSpan RefreshTokenRenewInterval = TimeSpan.FromDays(10);

    /// <summary>
    /// Retention policies supported by the SmartgridOne integration.
    /// </summary>
    public static readonly ReadOnlyCollection<string> RetentionPolicies = Array.AsReadOnly(["rp_one_s", "rp_one_m"]);

    /// <summary>
    /// Device node types that are intentionally hidden from the device model.
    /// </summary>
    public static readonly IReadOnlySet<string> ExcludedDeviceNodeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "smartgridController",
        "smartgridControllerSite",
        "switchboard",
    };

    /// <summary>
    /// Device measurements used when the API response does not specify a concrete measurement.
    /// </summary>
    public static readonly ReadOnlyCollection<string> DefaultMeasurements = Array.AsReadOnly(
    [
        "batteryMetrics",
        "boilerMetrics",
        "evChargerMetrics",
        "externalSignalMetrics",
        "gridMetrics",
        "heatPumpMetrics",
        "hybridInverterMetrics",
        "installationMetrics",
        "planning",
        "solarInstallationMetrics",
        "solarInverterMetrics",
        "solarOptimizerMetrics",
        "solarStringMetrics",
        "submeteringMetrics",
        "switchedLoadMetrics",
    ]);

    /// <summary>
    /// Known telemetry fields exposed by the SmartgridOne integration.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, TelemetryFieldDefinition> TelemetryFields =
        new ReadOnlyDictionary<string, TelemetryFieldDefinition>(
            new Dictionary<string, TelemetryFieldDefinition>(StringComparer.Ordinal)
            {
                ["actualPowerTot_W"] = new("W", "power"),
                ["actualPowerL1_W"] = new("W", "power"),
                ["actualPowerL2_W"] = new("W", "power"),
                ["actualPowerL3_W"] = new("W", "power"),
                ["childrenStoragePower_W"] = new("W", "power"),
                ["childrenProducedPower_W"] = new("W", "power"),
                ["childrenEVPower_W"] = new("W", "power"),
                ["childrenConsumedPower_W"] = new("W", "power"),
                ["childrenOtherPower_W"] = new("W", "power"),
                ["powerSetpoint_W"] = new("W", "power"),
                ["reactivePowerSetpoint_VAr"] = new("VAr", "reactive_power"),
                ["reacPowerSetpoint_VAr"] = new("VAr", "reactive_power"),
                ["setpoint_W"] = new("W", "power"),
                ["importLimit_W"] = new("W", "power"),
                ["exportLimit_W"] = new("W", "power"),
                ["currentL1_A"] = new("A", "current"),
                ["currentL2_A"] = new("A", "current"),
                ["currentL3_A"] = new("A", "current"),
                ["currentN_A"] = new("A", "current"),
                ["voltageL1N_V"] = new("V", "voltage"),
                ["voltageL2N_V"] = new("V", "voltage"),
                ["voltageL3N_V"] = new("V", "voltage"),
                ["voltageL1L2_V"] = new("V", "voltage"),
                ["voltageL2L3_V"] = new("V", "voltage"),
                ["voltageL3L1_V"] = new("V", "voltage"),
                ["frequency_Hz"] = new("Hz", "frequency"),
                ["powerFactorTot"] = new(null, "power_factor"),
                ["powerFactorL1"] = new(null, "power_factor"),
                ["powerFactorL2"] = new(null, "power_factor"),
                ["powerFactorL3"] = new(null, "power_factor"),
                ["voltageDC_V"] = new("V", "voltage"),
                ["currentDC_A"] = new("A", "current"),
                ["status"] = new(null, "enum"),
                ["operationMode"] = new(null, "enum"),
                ["stateOfCharge_frac"] = new("%", "battery"),
                ["stateOfHealth_frac"] = new("%", "battery"),
                ["childrenStorageStateOfCharge_frac"] = new("%", "battery"),
                ["batteryCurrent_A"] = new("A", "current"),
                ["batteryVoltage_V"] = new("V", "voltage"),
                ["evRequiringCharge"] = new(null, "boolean"),
                ["policy"] = new(null, "enum"),
                ["strategy"] = new(null, "enum"),
                ["signalActive"] = new(null, "boolean"),
                ["constraint_ph0_label"] = new(null, "enum"),
            });
}

/// <summary>
/// Describes a known telemetry field and the metadata needed to expose it.
/// </summary>
/// <param name="Unit">The engineering unit, if one exists.</param>
/// <param name="Category">The logical field category.</param>
public readonly record struct TelemetryFieldDefinition(string? Unit, string Category);
