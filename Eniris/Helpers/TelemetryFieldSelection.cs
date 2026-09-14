#nullable enable
using Eniris.Models;

namespace Eniris.Helpers;

internal static class TelemetryFieldSelection
{
    public static IReadOnlyList<string> SelectFields(TelemetrySource source, IReadOnlyList<string> requestedFields)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requestedFields);

        var availableFields = requestedFields
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Where(field => source.Fields is null || source.Fields.Contains(field, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (availableFields.Length > 0)
        {
            return availableFields;
        }

        return source.Fields?.Take(2).ToArray() ?? [];
    }
}
