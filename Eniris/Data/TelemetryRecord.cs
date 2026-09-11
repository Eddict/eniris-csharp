#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Eniris.Data;

/// <summary>
/// Database-oriented representation of one Eniris telemetry value.
/// </summary>
[Table("TelemetryData")]
public class TelemetryRecord
{
    /// <summary>
    /// Gets or sets the database identity key.
    /// </summary>
    [Key]
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the Eniris device identifier.
    /// </summary>
    public int DeviceId { get; set; }

    /// <summary>
    /// Gets or sets the device display name.
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the telemetry measurement name.
    /// </summary>
    public string Measurement { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the retention policy used for the measurement.
    /// </summary>
    public string RetentionPolicy { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the telemetry field name.
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the raw telemetry value.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Gets or sets the engineering unit for the field.
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// Gets or sets the device type exposed by Eniris.
    /// </summary>
    public string? DeviceType { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the measurement was recorded.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the row was prepared for persistence.
    /// </summary>
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
