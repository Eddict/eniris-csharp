#nullable enable
using System.Text.Json.Nodes;
using Eniris.Api;
using Eniris.Data;
using Eniris.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Eniris.Telemetry;

/// <summary>
/// Fetches historical telemetry in manageable chunks and maps it to database-oriented records.
/// </summary>
public sealed class HistoricalDataFetcher
{
    private static readonly TimeSpan DefaultChunkSize = TimeSpan.FromDays(7);
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);
    private const int MaxRetryAttempts = 5;

    private readonly Func<IReadOnlyList<JsonObject>, CancellationToken, Task<IReadOnlyList<JsonObject>>> _telemetryAsync;
    private readonly ILogger<HistoricalDataFetcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoricalDataFetcher"/> class.
    /// </summary>
    public HistoricalDataFetcher(IEnirisApiClient apiClient, ILogger<HistoricalDataFetcher>? logger = null)
        : this(
            (queries, cancellationToken) => (apiClient ?? throw new ArgumentNullException(nameof(apiClient))).TelemetryAsync(queries, cancellationToken),
            logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoricalDataFetcher"/> class.
    /// </summary>
    public HistoricalDataFetcher(IEnirisClient client, ILogger<HistoricalDataFetcher>? logger = null)
        : this(
            (queries, cancellationToken) => (client ?? throw new ArgumentNullException(nameof(client))).TelemetryAsync(queries, cancellationToken),
            logger)
    {
    }

    private HistoricalDataFetcher(
        Func<IReadOnlyList<JsonObject>, CancellationToken, Task<IReadOnlyList<JsonObject>>> telemetryAsync,
        ILogger<HistoricalDataFetcher>? logger)
    {
        _telemetryAsync = telemetryAsync;
        _logger = logger ?? NullLogger<HistoricalDataFetcher>.Instance;
    }

    /// <summary>
    /// Fetches historical telemetry for a single device/source pair.
    /// </summary>
    public async Task<IEnumerable<TelemetryRecord>> FetchAsync(
        EnirisDevice device,
        TelemetrySource source,
        string[] fields,
        DateTime from,
        DateTime to,
        TimeSpan? chunkSize = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fields);

        if (to <= from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), "The end of the historical range must be after the start.");
        }

        var effectiveChunkSize = chunkSize ?? DefaultChunkSize;
        if (effectiveChunkSize <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
        }

        var requestQuery = fields
            .Where(static field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestQuery.Length == 0)
        {
            return Array.Empty<TelemetryRecord>();
        }

        var records = new List<TelemetryRecord>();
        var currentFrom = from;
        while (currentFrom < to)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentTo = currentFrom.Add(effectiveChunkSize);
            if (currentTo > to)
            {
                currentTo = to;
            }

            var query = TelemetryQueryBuilder.BuildHistoricalQuery(source, requestQuery, currentFrom, currentTo);
            if (query is null)
            {
                break;
            }

            _logger.LogInformation(
                "Fetching Eniris telemetry for device {DeviceId} source {SourceKey} between {From} and {To}",
                device.Id,
                source.Key,
                currentFrom,
                currentTo);

            var requests = new[] { new TelemetryRequest(device, source, query) };
            var responses = await ExecuteWithRetryAsync(requests, cancellationToken).ConfigureAwait(false);
            records.AddRange(TelemetryResponseParser.ParseHistorical(requests, responses));

            currentFrom = currentTo;
        }

        return records;
    }

    /// <summary>
    /// Fetches historical telemetry for multiple device/source pairs in parallel.
    /// </summary>
    public async Task<IEnumerable<TelemetryRecord>> FetchMultipleAsync(
        IEnumerable<(EnirisDevice device, TelemetrySource source)> sources,
        string[] fields,
        DateTime from,
        DateTime to,
        TimeSpan? chunkSize = null,
        int maxConcurrency = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(fields);

        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be greater than zero.");
        }

        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = sources.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var results = await FetchAsync(item.device, item.source, fields, from, to, chunkSize, cancellationToken).ConfigureAwait(false);
                return results.ToArray();
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        var batches = await Task.WhenAll(tasks).ConfigureAwait(false);
        return batches.SelectMany(static batch => batch).ToArray();
    }

    private async Task<IReadOnlyList<JsonObject>> ExecuteWithRetryAsync(
        IReadOnlyList<TelemetryRequest> requests,
        CancellationToken cancellationToken)
    {
        var delay = DefaultRetryDelay;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await _telemetryAsync(requests.Select(static request => request.Query).ToArray(), cancellationToken).ConfigureAwait(false);
            }
            catch (EnirisRateLimitError exception) when (attempt < MaxRetryAttempts)
            {
                var retryDelay = exception.RetryAfter ?? delay;
                _logger.LogWarning(
                    exception,
                    "Eniris rate limited historical telemetry request. Retrying attempt {Attempt} after {Delay}",
                    attempt,
                    retryDelay);
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                delay = NextDelay(retryDelay);
            }
            catch (EnirisApiError exception) when (attempt < MaxRetryAttempts)
            {
                _logger.LogWarning(
                    exception,
                    "Eniris historical telemetry request failed. Retrying attempt {Attempt} after {Delay}",
                    attempt,
                    delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = NextDelay(delay);
            }
        }
    }

    private static TimeSpan NextDelay(TimeSpan currentDelay)
    {
        var nextTicks = currentDelay.Ticks * 2;
        var maxTicks = TimeSpan.FromSeconds(30).Ticks;
        return TimeSpan.FromTicks(Math.Min(nextTicks, maxTicks));
    }
}
