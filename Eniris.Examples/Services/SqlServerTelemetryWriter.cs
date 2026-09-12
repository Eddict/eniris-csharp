#nullable enable
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using Eniris.Data;
using Eniris.Examples.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Eniris.Examples.Services;

public sealed class SqlServerTelemetryWriter
{
    private readonly ExampleConfig _config;
    private readonly ILogger<SqlServerTelemetryWriter> _logger;
    private readonly string _tableName;

    public SqlServerTelemetryWriter(ExampleConfig config, ILogger<SqlServerTelemetryWriter> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tableName = typeof(TelemetryRecord).GetCustomAttributes(typeof(TableAttribute), inherit: false)
            .OfType<TableAttribute>()
            .Select(static attribute => attribute.Name)
            .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name))
            ?? nameof(TelemetryRecord);
    }

    public async Task<SqlServerPersistenceSummary> PersistAsync(
        IEnumerable<TelemetryRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var materializedRecords = records.ToArray();
        if (materializedRecords.Length == 0)
        {
            return new SqlServerPersistenceSummary(_tableName, 0);
        }

        var connectionString = _config.SqlServerConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A SQL Server connection string is required to persist telemetry records.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTableAsync(connection, cancellationToken).ConfigureAwait(false);

        using var dataTable = CreateDataTable(materializedRecords);
        using var bulkCopy = new SqlBulkCopy(connection)
        {
            BatchSize = Math.Max(1, _config.SqlServerBatchSize),
            DestinationTableName = $"[dbo].[{_tableName}]",
            EnableStreaming = true,
        };

        AddColumnMappings(bulkCopy);

        _logger.LogInformation("Persisting {RecordCount} telemetry record(s) into SQL Server table {TableName}", materializedRecords.Length, _tableName);
        await bulkCopy.WriteToServerAsync(dataTable, cancellationToken).ConfigureAwait(false);
        return new SqlServerPersistenceSummary(_tableName, materializedRecords.Length);
    }

    private async Task EnsureTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var escapedTableName = _tableName.Replace("]", "]]", StringComparison.Ordinal);
        var commandText = $"""
            IF OBJECT_ID(N'[dbo].[{escapedTableName}]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{escapedTableName}]
                (
                    [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [DeviceId] INT NOT NULL,
                    [DeviceName] NVARCHAR(255) NOT NULL,
                    [Measurement] NVARCHAR(255) NOT NULL,
                    [RetentionPolicy] NVARCHAR(255) NOT NULL,
                    [Field] NVARCHAR(255) NOT NULL,
                    [Value] NVARCHAR(MAX) NULL,
                    [ValueType] NVARCHAR(32) NOT NULL,
                    [Unit] NVARCHAR(64) NULL,
                    [DeviceType] NVARCHAR(255) NULL,
                    [Timestamp] DATETIME2 NOT NULL,
                    [RecordedAt] DATETIME2 NOT NULL
                );

                CREATE INDEX [IX_{escapedTableName}_DeviceId_Timestamp]
                    ON [dbo].[{escapedTableName}] ([DeviceId], [Timestamp]);
            END
            """;

        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddColumnMappings(SqlBulkCopy bulkCopy)
    {
        bulkCopy.ColumnMappings.Add(nameof(TelemetryRecord.DeviceId), nameof(TelemetryRecord.DeviceId));
        bulkCopy.ColumnMappings.Add(nameof(TelemetryRecord.DeviceName), nameof(TelemetryRecord.DeviceName));
        bulkCopy.ColumnMappings.Add(nameof(TelemetryRecord.Measurement), nameof(TelemetryRecord.Measurement));
        bulkCopy.ColumnMappings.Add(nameof(TelemetryRecord.RetentionPolicy), nameof(TelemetryRecord.RetentionPolicy));
        bulkCopy.ColumnMappings.Add(nameof(TelemetryRecord.Field), nameof(TelemetryRecord.Field));
        bulkCopy.ColumnMappings.Add(nameof(TelemetryRecord.Value), nameof(TelemetryRecord.Value));
        bulkCopy.ColumnMappings.Add(nameof(TelemetryRecord.ValueType), nameof(TelemetryRecord.ValueType));
        bulkCopy.ColumnMappings.Add(nameof(TelemetryRecord.Unit), nameof(TelemetryRecord.Unit));
        bulkCopy.ColumnMappings.Add(nameof(TelemetryRecord.DeviceType), nameof(TelemetryRecord.DeviceType));
        bulkCopy.ColumnMappings.Add(nameof(TelemetryRecord.Timestamp), nameof(TelemetryRecord.Timestamp));
        bulkCopy.ColumnMappings.Add(nameof(TelemetryRecord.RecordedAt), nameof(TelemetryRecord.RecordedAt));
    }

    private static DataTable CreateDataTable(IEnumerable<TelemetryRecord> records)
    {
        var table = new DataTable();
        table.Columns.Add(nameof(TelemetryRecord.DeviceId), typeof(int));
        table.Columns.Add(nameof(TelemetryRecord.DeviceName), typeof(string));
        table.Columns.Add(nameof(TelemetryRecord.Measurement), typeof(string));
        table.Columns.Add(nameof(TelemetryRecord.RetentionPolicy), typeof(string));
        table.Columns.Add(nameof(TelemetryRecord.Field), typeof(string));
        table.Columns.Add(nameof(TelemetryRecord.Value), typeof(string));
        table.Columns.Add(nameof(TelemetryRecord.ValueType), typeof(string));
        table.Columns.Add(nameof(TelemetryRecord.Unit), typeof(string));
        table.Columns.Add(nameof(TelemetryRecord.DeviceType), typeof(string));
        table.Columns.Add(nameof(TelemetryRecord.Timestamp), typeof(DateTime));
        table.Columns.Add(nameof(TelemetryRecord.RecordedAt), typeof(DateTime));

        foreach (var record in records)
        {
            table.Rows.Add(
                record.DeviceId,
                record.DeviceName,
                record.Measurement,
                record.RetentionPolicy,
                record.Field,
                record.Value is null ? DBNull.Value : record.Value,
                record.ValueType,
                record.Unit is null ? DBNull.Value : record.Unit,
                record.DeviceType is null ? DBNull.Value : record.DeviceType,
                record.Timestamp,
                record.RecordedAt);
        }

        return table;
    }
}

public sealed record SqlServerPersistenceSummary(string TableName, int RowCount);
