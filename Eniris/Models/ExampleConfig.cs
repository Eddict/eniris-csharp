#nullable enable
using Eniris.Configuration;

namespace Eniris.Models;

public sealed class ExampleConfig
{
    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? SqlServerConnectionString { get; set; }

    public Uri AuthBaseUri { get; set; } = EnirisConstants.DefaultAuthBaseUri;

    public Uri ApiBaseUri { get; set; } = EnirisConstants.DefaultApiBaseUri;

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int SqlServerBatchSize { get; set; } = 1000;

    public int SqlServerBatchSizeLongRange { get; set; } = 5000;

    public string[] PreferredTelemetryFields { get; set; } = ["actualPowerTot_W", "voltageL1N_V", "currentL1_A"];

    public int HistoricalQueryLimit { get; set; } = 10000;

    public void ApplyTo(EnirisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AuthBaseUri = AuthBaseUri;
        options.ApiBaseUri = ApiBaseUri;
        options.RequestTimeout = RequestTimeout;
    }
}
