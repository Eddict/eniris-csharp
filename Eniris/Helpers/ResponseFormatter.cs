#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Eniris.Configuration;
using Eniris.Data;
using Eniris.Examples;
using Eniris.Services;
using Eniris.Models;
using Eniris.Telemetry;

namespace Eniris.Helpers;

public static class ResponseFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FormatAuthentication(AuthenticationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return string.Join(Environment.NewLine,
        [
            "Authentication flow complete:",
            $"  Refresh token (login): issued ({summary.RefreshToken.Length} chars)",
            $"  Access token:          issued ({summary.AccessToken.Length} chars)",
            string.IsNullOrWhiteSpace(summary.RenewedRefreshToken)
                ? "  Refresh token (renew): skipped"
                : $"  Refresh token (renew): issued ({summary.RenewedRefreshToken.Length} chars)",
            string.IsNullOrWhiteSpace(summary.RenewedAccessToken)
                ? "  Access token (renew):  skipped"
                : $"  Access token (renew):  issued ({summary.RenewedAccessToken.Length} chars)",
        ]);
    }

    public static string FormatDiscovery(DeviceDiscoverySummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var builder = new StringBuilder();
        builder.AppendLine($"Companies: {summary.Companies.Count}");
        builder.AppendLine($"Roles: {summary.Roles.Count}");
        builder.AppendLine($"Monitors: {summary.MonitorsByRole.Sum(static pair => pair.Value.Count)}");
        builder.AppendLine($"Devices discovered: {summary.Devices.Count}");
        builder.AppendLine($"Controllers grouped: {summary.Controllers.Count}");

        foreach (var controller in summary.Controllers)
        {
            builder.AppendLine($"- Controller {controller.Name} ({controller.Id})");
            foreach (var child in controller.Children)
            {
                builder.AppendLine($"    • {child.Name} [{child.NodeType}] telemetry sources: {child.TelemetrySources.Count}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatLatestTelemetry(IEnumerable<SensorValue> values, int maxRows = 12)
    {
        ArgumentNullException.ThrowIfNull(values);

        var materialized = values.ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"Latest telemetry rows: {materialized.Length}");
        foreach (var value in materialized.Take(maxRows))
        {
            builder.AppendLine($"- {value.Device.Name} | {DescribeField(value.Key.Field)} | {FormatTimestamp(value.Timestamp)} | {FormatValue(value.Value)}");
        }

        if (materialized.Length > maxRows)
        {
            builder.AppendLine($"... {materialized.Length - maxRows} more row(s) omitted");
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatHistorical(IEnumerable<TelemetryRecord> records, ExampleDateRange range, int maxRows = 12)
    {
        ArgumentNullException.ThrowIfNull(records);

        var ordered = records.OrderBy(record => record.Timestamp).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"Historical telemetry for {range.Label}: {ordered.Length} row(s)");
        if (ordered.Length > 0)
        {
            builder.AppendLine($"Range covered: {ordered[0].Timestamp:o} → {ordered[^1].Timestamp:o}");
        }
        else
        {
            builder.AppendLine($"Range requested: {range.Start:o} → {range.End:o}");
        }

        foreach (var record in ordered.Take(maxRows))
        {
            builder.AppendLine($"- {record.Timestamp:o} | {record.DeviceName} | {DescribeField(record.Field)} | {FormatValue(record.GetTypedValue())}");
        }

        if (ordered.Length > maxRows)
        {
            builder.AppendLine($"... {ordered.Length - maxRows} more row(s) omitted");
        }

        return builder.ToString().TrimEnd();
    }

    public static string FormatQuery(string title, JsonNode query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(query);
        return $"{title}:{Environment.NewLine}{query.ToJsonString(JsonOptions)}";
    }

    public static string FormatPersistence(SqlServerPersistenceSummary summary) =>
        $"Attempted {summary.AttemptedRowCount} row(s) into dbo.{summary.TableName}; added {summary.AddedRowCount}, ignored {summary.IgnoredRowCount}.";

    public static string DescribeField(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        return EnirisConstants.TelemetryFields.TryGetValue(field, out var definition) && !string.IsNullOrWhiteSpace(definition.Unit)
            ? $"{field} ({definition.Unit})"
            : field;
    }

    public static string FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "n/a";

    public static string FormatValue(object? value) =>
        value switch
        {
            null => "null",
            DateTimeOffset dto => dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
}
