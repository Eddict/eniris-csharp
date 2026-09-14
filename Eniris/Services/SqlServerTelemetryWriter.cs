#nullable enable
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using Eniris.Data;
using Eniris.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Eniris.Services;

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
            return new SqlServerPersistenceSummary(_tableName, 0, 0, 0);
        }

        var connectionString = _config.SqlServerConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A SQL Server connection string is required to persist telemetry records.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTableAsync(connection, cancellationToken).ConfigureAwait(false);

        var rowCountBefore = await CountRowsAsync(connection, cancellationToken).ConfigureAwait(false);

        using var bulkCopy = new SqlBulkCopy(connection)
        {
            BatchSize = batchSize,
            DestinationTableName = $"[dbo].[{_tableName}]",
            EnableStreaming = true,
        };

        AddColumnMappings(bulkCopy);

        var attemptedRowCount = 0;
        do
        {
            using var dataTable = CreateDataTable(batches.Current);
            _logger.LogInformation("Persisting {RecordCount} telemetry record(s) into SQL Server table {TableName}", batches.Current.Length, _tableName);
            await bulkCopy.WriteToServerAsync(dataTable, cancellationToken).ConfigureAwait(false);
            attemptedRowCount += batches.Current.Length;
        }
        while (batches.MoveNext());

        var rowCountAfter = await CountRowsAsync(connection, cancellationToken).ConfigureAwait(false);
        var addedRowCount = (int)Math.Max(0L, rowCountAfter - rowCountBefore);
        var ignoredRowCount = Math.Max(0, attemptedRowCount - addedRowCount);

        return new SqlServerPersistenceSummary(_tableName, attemptedRowCount, addedRowCount, ignoredRowCount);
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
            return new SqlServerPersistenceSummary(_tableName, 0, 0, 0);
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTableAsync(connection, cancellationToken).ConfigureAwait(false);

        var rowCountBefore = await CountRowsAsync(connection, cancellationToken).ConfigureAwait(false);

        using var bulkCopy = new SqlBulkCopy(connection)
        {
            BatchSize = batchSize,
            DestinationTableName = $"[dbo].[{_tableName}]",
            EnableStreaming = true,
        };

        AddColumnMappings(bulkCopy);

        var attemptedRowCount = 0;
        do
        {
            using var dataTable = CreateDataTable(batchEnumerator.Current);
            _logger.LogInformation("Persisting {RecordCount} telemetry record(s) into SQL Server table {TableName}", batchEnumerator.Current.Count, _tableName);
            await bulkCopy.WriteToServerAsync(dataTable, cancellationToken).ConfigureAwait(false);
            attemptedRowCount += batchEnumerator.Current.Count;
        }
        while (await batchEnumerator.MoveNextAsync().ConfigureAwait(false));

        var rowCountAfter = await CountRowsAsync(connection, cancellationToken).ConfigureAwait(false);
        var addedRowCount = (int)Math.Max(0L, rowCountAfter - rowCountBefore);
        var ignoredRowCount = Math.Max(0, attemptedRowCount - addedRowCount);

        return new SqlServerPersistenceSummary(_tableName, attemptedRowCount, addedRowCount, ignoredRowCount);
    }

    public async Task<DateTimeOffset?> GetMinimumFieldMaxTimestampAsync(
        int deviceId,
        string measurement,
        string retentionPolicy,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(measurement);
        ArgumentException.ThrowIfNullOrWhiteSpace(retentionPolicy);
        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Count == 0)
        {
            return null;
        }

        var connectionString = _config.SqlServerConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A SQL Server connection string is required to read persisted telemetry records.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset? minimumFieldMaxTimestamp = null;
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            var fieldMaxTimestamp = await GetFieldMaxTimestampAsync(
                connection,
                deviceId,
                measurement,
                retentionPolicy,
                field,
                cancellationToken).ConfigureAwait(false);

            if (!fieldMaxTimestamp.HasValue)
            {
                return null;
            }

            var normalizedFieldMaxTimestamp = fieldMaxTimestamp.Value.ToUniversalTime();
            if (!minimumFieldMaxTimestamp.HasValue || normalizedFieldMaxTimestamp < minimumFieldMaxTimestamp.Value)
            {
                minimumFieldMaxTimestamp = normalizedFieldMaxTimestamp;
            }
        }

        return minimumFieldMaxTimestamp;
    }

    private async Task EnsureTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var escapedTableName = _tableName.Replace("]", "]]", StringComparison.Ordinal);
        var commandText = $"""
            IF OBJECT_ID(N'[dbo].[{escapedTableName}]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{escapedTableName}]
                (
                    [Id] BIGINT IDENTITY(1,1) NOT NULL,
                    [DeviceId] INT NOT NULL,
                    [DeviceName] VARCHAR(96) NOT NULL,
                    [Measurement] VARCHAR(48) NOT NULL,
                    [RetentionPolicy] VARCHAR(16) NOT NULL,
                    [Field] VARCHAR(48) NOT NULL,
                    [Value] VARCHAR(48) NOT NULL,
                    [ValueType] VARCHAR(16) NOT NULL,
                    [Unit] VARCHAR(4) NULL,
                    [DeviceType] VARCHAR(48) NOT NULL,
                    [Timestamp] DATETIMEOFFSET NOT NULL,
                    CONSTRAINT [pk_{escapedTableName}] PRIMARY KEY ([Id])
                );

                CREATE UNIQUE NONCLUSTERED INDEX [ux_{escapedTableName}]
                ON [dbo].[{escapedTableName}]
                (
                    [DeviceId] ASC,
                    [Measurement] ASC,
                    [RetentionPolicy] ASC,
                    [Field] ASC,
                    [Timestamp] DESC
                )
                WITH
                (
                    DROP_EXISTING = ON,
                    IGNORE_DUP_KEY = ON
                );
            END
            """;

        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> CountRowsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var escapedTableName = _tableName.Replace("]", "]]", StringComparison.Ordinal);

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT_BIG(1) FROM [dbo].[{escapedTableName}]";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    private async Task<DateTimeOffset?> GetFieldMaxTimestampAsync(
        SqlConnection connection,
        int deviceId,
        string measurement,
        string retentionPolicy,
        string field,
        CancellationToken cancellationToken)
    {
        var escapedTableName = _tableName.Replace("]", "]]", StringComparison.Ordinal);

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF OBJECT_ID(N'[dbo].[{escapedTableName}]', N'U') IS NULL
            BEGIN
                SELECT CAST(NULL AS DATETIMEOFFSET);
            END
            ELSE
            BEGIN
                SELECT MAX([Timestamp])
                FROM [dbo].[{escapedTableName}]
                WHERE [DeviceId] = @DeviceId
                  AND [Measurement] = @Measurement
                  AND [RetentionPolicy] = @RetentionPolicy
                  AND [Field] = @Field;
            END
            """;

        command.Parameters.Add(new SqlParameter("@DeviceId", SqlDbType.Int) { Value = deviceId });
        command.Parameters.Add(new SqlParameter("@Measurement", SqlDbType.VarChar, 48) { Value = measurement });
        command.Parameters.Add(new SqlParameter("@RetentionPolicy", SqlDbType.VarChar, 16) { Value = retentionPolicy });
        command.Parameters.Add(new SqlParameter("@Field", SqlDbType.VarChar, 48) { Value = field });

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? null : (DateTimeOffset?)result;
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

public sealed record SqlServerPersistenceSummary(string TableName, int AttemptedRowCount, int AddedRowCount, int IgnoredRowCount);
