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

        var batchSize = Math.Max(1, _config.SqlServerBatchSize);
        using var batches = records.Chunk(batchSize).GetEnumerator();
        if (!batches.MoveNext())
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

        using var bulkCopy = new SqlBulkCopy(connection)
        {
            BatchSize = batchSize,
            DestinationTableName = $"[dbo].[{_tableName}]",
            EnableStreaming = true,
        };

        AddColumnMappings(bulkCopy);

        var persistedCount = 0;
        do
        {
            using var dataTable = CreateDataTable(batches.Current);
            _logger.LogInformation("Persisting {RecordCount} telemetry record(s) into SQL Server table {TableName}", batches.Current.Length, _tableName);
            await bulkCopy.WriteToServerAsync(dataTable, cancellationToken).ConfigureAwait(false);
            persistedCount += batches.Current.Length;
        }
        while (batches.MoveNext());

        return new SqlServerPersistenceSummary(_tableName, persistedCount);
    }

    public async Task<SqlServerPersistenceSummary> PersistBatchesAsync(
        IAsyncEnumerable<IReadOnlyList<TelemetryRecord>> batches,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batches);

        var connectionString = _config.SqlServerConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A SQL Server connection string is required to persist telemetry records.");
        }

        var batchSize = Math.Max(1, _config.SqlServerBatchSize);
        await using var batchEnumerator = batches.GetAsyncEnumerator(cancellationToken);
        if (!await batchEnumerator.MoveNextAsync().ConfigureAwait(false))
        {
            return new SqlServerPersistenceSummary(_tableName, 0);
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTableAsync(connection, cancellationToken).ConfigureAwait(false);

        using var bulkCopy = new SqlBulkCopy(connection)
        {
            BatchSize = batchSize,
            DestinationTableName = $"[dbo].[{_tableName}]",
            EnableStreaming = true,
        };

        AddColumnMappings(bulkCopy);

        var persistedCount = 0;
        do
        {
            using var dataTable = CreateDataTable(batchEnumerator.Current);
            _logger.LogInformation("Persisting {RecordCount} telemetry record(s) into SQL Server table {TableName}", batchEnumerator.Current.Count, _tableName);
            await bulkCopy.WriteToServerAsync(dataTable, cancellationToken).ConfigureAwait(false);
            persistedCount += batchEnumerator.Current.Count;
        }
        while (await batchEnumerator.MoveNextAsync().ConfigureAwait(false));

        return new SqlServerPersistenceSummary(_tableName, persistedCount);
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
                    [DeviceName] VARCHAR(96) NOT NULL,
                    [Measurement] VARCHAR(48) NOT NULL,
                    [RetentionPolicy] VARCHAR(16) NOT NULL,
                    [Field] VARCHAR(48) NOT NULL,
                    [Value] VARCHAR(48) NOT NULL,
                    [ValueType] VARCHAR(16) NOT NULL,
                    [Unit] VARCHAR(4) NULL,
                    [DeviceType] VARCHAR(48) NOT NULL,
                    [Timestamp] DATETIMEOFFSET NOT NULL
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
        table.Columns.Add(nameof(TelemetryRecord.Timestamp), typeof(DateTimeOffset));

        foreach (var record in records)
        {
            table.Rows.Add(
                (int)record.DeviceId,
                record.DeviceName,
                record.Measurement,
                record.RetentionPolicy,
                record.Field,
                record.Value,
                record.ValueType,
                record.Unit is null ? DBNull.Value : record.Unit,
                record.DeviceType,
                ToUtcOffset(record.Timestamp));
        }

        return table;
    }

    private static DateTimeOffset ToUtcOffset(DateTime value)
    {
        var normalized = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        return new DateTimeOffset(normalized, TimeSpan.Zero);
    }
}

public sealed record SqlServerPersistenceSummary(string TableName, int RowCount);
