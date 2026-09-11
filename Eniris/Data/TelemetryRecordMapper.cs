#nullable enable
using System.Text;
using Eniris.Configuration;
using Eniris.Models;
using Eniris.Telemetry;

namespace Eniris.Data;

/// <summary>
/// Maps parsed Eniris telemetry values to database-oriented records.
/// </summary>
public static class TelemetryRecordMapper
{
    /// <summary>
    /// Maps a parsed sensor value to a telemetry record using explicit device/source metadata.
    /// </summary>
    public static TelemetryRecord MapSensorValue(
        SensorValue sensorValue,
        EnirisDevice device,
        TelemetrySource source)
    {
        ArgumentNullException.ThrowIfNull(sensorValue);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(source);
        var timestamp = sensorValue.Timestamp ?? throw new InvalidOperationException("A telemetry timestamp is required to map a record for persistence.");

        return new TelemetryRecord
        {
            DeviceId = device.Id,
            DeviceName = device.Name,
            Measurement = source.Measurement,
            RetentionPolicy = source.RetentionPolicy,
            Field = NormalizeFieldName(sensorValue.Key.Field),
            Value = sensorValue.Value,
            Unit = GetUnit(sensorValue.Key.Field),
            DeviceType = string.IsNullOrWhiteSpace(device.NodeType) ? null : device.NodeType,
            Timestamp = timestamp.UtcDateTime,
            RecordedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Maps a parsed sensor value to a telemetry record using the metadata carried by the value itself.
    /// </summary>
    public static TelemetryRecord MapSensorValue(SensorValue sensorValue)
    {
        ArgumentNullException.ThrowIfNull(sensorValue);
        return MapSensorValue(sensorValue, sensorValue.Device, sensorValue.Source);
    }

    /// <summary>
    /// Maps a batch of parsed sensor values to telemetry records.
    /// </summary>
    public static IEnumerable<TelemetryRecord> MapBulk(IEnumerable<SensorValue> sensorValues)
    {
        ArgumentNullException.ThrowIfNull(sensorValues);
        foreach (var sensorValue in sensorValues)
        {
            yield return MapSensorValue(sensorValue);
        }
    }

    /// <summary>
    /// Gets the configured engineering unit for a telemetry field.
    /// </summary>
    public static string? GetUnit(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        return EnirisConstants.TelemetryFields.TryGetValue(fieldName, out var definition)
            ? definition.Unit
            : null;
    }

    /// <summary>
    /// Normalizes a field name so it is safe to reuse in database-oriented contexts.
    /// </summary>
    public static string NormalizeFieldName(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        var builder = new StringBuilder(fieldName.Length);
        var previousWasUnderscore = false;
        foreach (var character in fieldName)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                builder.Append(character);
                previousWasUnderscore = false;
                continue;
            }

            if (!previousWasUnderscore)
            {
                builder.Append('_');
                previousWasUnderscore = true;
            }
        }

        return builder.ToString().Trim('_');
    }
}
