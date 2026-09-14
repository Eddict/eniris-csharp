#nullable enable
using Eniris.Models;
using Eniris.Telemetry;
using Microsoft.Extensions.Logging;

namespace Eniris.Examples;

public sealed class LatestTelemetryExample
{
    private readonly IEnirisClient _client;
    private readonly ILogger<LatestTelemetryExample> _logger;

    public LatestTelemetryExample(IEnirisClient client, ILogger<LatestTelemetryExample> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SensorValue>> RunAsync(
        IReadOnlyList<EnirisDevice> devices,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(fields);

        var requests = TelemetryQueryBuilder.BuildRequests(devices, fields)
            .Where(static request => request.Query is not null)
            .ToArray();

        if (requests.Length == 0)
        {
            throw new InvalidOperationException("No telemetry requests could be created for the discovered devices.");
        }

        _logger.LogInformation("Fetching latest telemetry using {RequestCount} request(s)", requests.Length);
        var responses = await _client.TelemetryAsync(requests.Select(static request => request.Query).ToArray(), cancellationToken).ConfigureAwait(false);

        return TelemetryResponseParser.Parse(requests, responses)
            .OrderBy(static value => value.Device.Name, StringComparer.Ordinal)
            .ThenBy(static value => value.Key.Field, StringComparer.Ordinal)
            .ToArray();
    }
}
