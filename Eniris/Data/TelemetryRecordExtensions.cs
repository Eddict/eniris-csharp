#nullable enable
using System.Globalization;

namespace Eniris.Data;

/// <summary>
/// Helpers for reading typed values back from persisted telemetry records.
/// </summary>
public static class TelemetryRecordExtensions
{
    /// <summary>
    /// Converts the persisted record value back to a typed CLR value when possible.
    /// </summary>
    public static object? GetTypedValue(this TelemetryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.ValueType switch
        {
            "null" => null,
            "boolean" when bool.TryParse(record.Value, out var boolValue) => boolValue,
            "integer" when long.TryParse(record.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue) => integerValue,
            "number" when double.TryParse(record.Value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var numberValue) => numberValue,
            "datetime" when DateTime.TryParse(record.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dateTimeValue) => dateTimeValue,
            _ => record.Value,
        };
    }
}
