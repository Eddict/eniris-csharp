#nullable enable
using System.Globalization;
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

        var (value, valueType) = SerializeValue(sensorValue.Value);
        ArgumentNullException.ThrowIfNull(value);

        return new TelemetryRecord
        {
            DeviceId = device.Id,
            DeviceName = device.Name,
            Measurement = source.Measurement,
            RetentionPolicy = source.RetentionPolicy,
            Field = NormalizeFieldName(sensorValue.Key.Field),
            Value = value,
            ValueType = valueType,
            Unit = GetUnit(sensorValue.Key.Field),
            DeviceType = device.NodeType,
            Timestamp = sensorValue.Timestamp.UtcDateTime,
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

    private static (string? Value, string ValueType) SerializeValue(object value) =>
        value switch
        {
            //null => (null, "null"),
            bool boolValue => (boolValue ? "1" : "0", "number"),
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                (Convert.ToString(value, CultureInfo.InvariantCulture), "number"),
            DateTime dateTimeValue =>
                (dateTimeValue.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), "datetime"),
            DateTimeOffset dateTimeOffsetValue =>
                (dateTimeOffsetValue.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), "datetime"),
            string stringValue => (stringValue, "string"),
            IFormattable formattable =>
                (formattable.ToString(null, CultureInfo.InvariantCulture), "string"),
            _ => (value.ToString(), "string"),
        };
}
