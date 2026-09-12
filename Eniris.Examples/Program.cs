#nullable enable
using Eniris.Api;
using Eniris.Examples.Examples;
using Eniris.Examples.Helpers;
using Eniris.Examples.Models;
using Eniris.Examples.Services;
using Eniris.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Eniris.Examples;

internal static class Program
{
    private static readonly IReadOnlyList<(string Key, string Description)> MenuItems =
    [
        ("1", "Run authentication flow"),
        ("2", "Discover companies, roles, monitors, devices, and controllers"),
        ("3", "Fetch latest telemetry values"),
        ("4", "Fetch 7-day historical telemetry with where.time"),
        ("5", "Fetch 30-day chunked historical telemetry"),
        ("6", "Show telemetry query builder examples (generic or discovered source)"),
        ("7", "Run complete walkthrough"),
        ("8", "Fetch 30-day historical telemetry and persist it to SQL Server"),
        ("0", "Exit"),
    ];

    public static async Task<int> Main(string[] args)
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Configuration.Sources.Clear();
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        var exampleConfig = builder.Configuration.GetSection("Eniris").Get<ExampleConfig>() ?? new ExampleConfig();

        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services));

        builder.Services.AddSingleton(exampleConfig);
        builder.Services.AddEniris(exampleConfig.ApplyTo);
        builder.Services.AddScoped<AuthenticationExample>();
        builder.Services.AddScoped<DeviceDiscoveryExample>();
        builder.Services.AddScoped<LatestTelemetryExample>();
        builder.Services.AddScoped<HistoricalTelemetryExample>();
        builder.Services.AddScoped<TelemetryQueryBuilderExample>();
        builder.Services.AddScoped<SqlServerTelemetryWriter>();

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Eniris.Examples.Program");

        AuthenticationSummary? authentication = null;
        DeviceDiscoverySummary? discovery = null;
        IReadOnlyList<Eniris.Telemetry.SensorValue>? latestValues = null;
        IReadOnlyList<Eniris.Data.TelemetryRecord>? historicalValues = null;

        var menu = new ConsoleMenu("Eniris Examples", MenuItems);

        try
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                var selection = menu.PromptSelection();
                if (selection == "0")
                {
                    return 0;
                }

                try
                {
                    switch (selection)
                    {
                        case "1":
                            authentication = await AuthenticateAsync(scope.ServiceProvider, exampleConfig, cancellationTokenSource.Token).ConfigureAwait(false);
                            Console.WriteLine(ResponseFormatter.FormatAuthentication(authentication));
                            break;

                        case "2":
                            authentication = await EnsureAuthenticatedAsync(scope.ServiceProvider, exampleConfig, authentication, cancellationTokenSource.Token).ConfigureAwait(false);
                            discovery = await DiscoverAsync(scope.ServiceProvider, cancellationTokenSource.Token).ConfigureAwait(false);
                            Console.WriteLine(ResponseFormatter.FormatDiscovery(discovery));
                            break;

                        case "3":
                            authentication = await EnsureAuthenticatedAsync(scope.ServiceProvider, exampleConfig, authentication, cancellationTokenSource.Token).ConfigureAwait(false);
                            discovery ??= await DiscoverAsync(scope.ServiceProvider, cancellationTokenSource.Token).ConfigureAwait(false);
                            latestValues = await FetchLatestAsync(scope.ServiceProvider, discovery, exampleConfig, cancellationTokenSource.Token).ConfigureAwait(false);
                            Console.WriteLine(ResponseFormatter.FormatLatestTelemetry(latestValues));
                            break;

                        case "4":
                            authentication = await EnsureAuthenticatedAsync(scope.ServiceProvider, exampleConfig, authentication, cancellationTokenSource.Token).ConfigureAwait(false);
                            discovery ??= await DiscoverAsync(scope.ServiceProvider, cancellationTokenSource.Token).ConfigureAwait(false);
                            var weekRange = DateRangeHelper.LastDays(7);
                            historicalValues = await FetchHistoricalAsync(scope.ServiceProvider, discovery, exampleConfig, weekRange, chunked: false, cancellationTokenSource.Token).ConfigureAwait(false);
                            Console.WriteLine(ResponseFormatter.FormatHistorical(historicalValues, weekRange));
                            break;

                        case "5":
                            authentication = await EnsureAuthenticatedAsync(scope.ServiceProvider, exampleConfig, authentication, cancellationTokenSource.Token).ConfigureAwait(false);
                            discovery ??= await DiscoverAsync(scope.ServiceProvider, cancellationTokenSource.Token).ConfigureAwait(false);
                            var monthRange = DateRangeHelper.LastDays(30);
                            historicalValues = await FetchHistoricalAsync(scope.ServiceProvider, discovery, exampleConfig, monthRange, chunked: true, cancellationTokenSource.Token).ConfigureAwait(false);
                            Console.WriteLine(ResponseFormatter.FormatHistorical(historicalValues, monthRange));
                            break;

                        case "6":
                            discovery ??= authentication is null
                                ? null
                                : await DiscoverAsync(scope.ServiceProvider, cancellationTokenSource.Token).ConfigureAwait(false);
                            DisplayQueries(scope.ServiceProvider, discovery);
                            break;

                        case "7":
                            authentication = await AuthenticateAsync(scope.ServiceProvider, exampleConfig, cancellationTokenSource.Token).ConfigureAwait(false);
                            Console.WriteLine(ResponseFormatter.FormatAuthentication(authentication));
                            discovery = await DiscoverAsync(scope.ServiceProvider, cancellationTokenSource.Token).ConfigureAwait(false);
                            Console.WriteLine(ResponseFormatter.FormatDiscovery(discovery));
                            latestValues = await FetchLatestAsync(scope.ServiceProvider, discovery, exampleConfig, cancellationTokenSource.Token).ConfigureAwait(false);
                            Console.WriteLine(ResponseFormatter.FormatLatestTelemetry(latestValues));
                            var walkthroughRange = DateRangeHelper.LastDays(7);
                            historicalValues = await FetchHistoricalAsync(scope.ServiceProvider, discovery, exampleConfig, walkthroughRange, chunked: false, cancellationTokenSource.Token).ConfigureAwait(false);
                            Console.WriteLine(ResponseFormatter.FormatHistorical(historicalValues, walkthroughRange));
                            DisplayQueries(scope.ServiceProvider, discovery);
                            break;

                        case "8":
                            authentication = await EnsureAuthenticatedAsync(scope.ServiceProvider, exampleConfig, authentication, cancellationTokenSource.Token).ConfigureAwait(false);
                            discovery ??= await DiscoverAsync(scope.ServiceProvider, cancellationTokenSource.Token).ConfigureAwait(false);
                            var sqlRange = DateRangeHelper.LastDays(30);
                            var persisted = await PersistHistoricalAsync(scope.ServiceProvider, discovery, exampleConfig, sqlRange, cancellationTokenSource.Token).ConfigureAwait(false);
                            Console.WriteLine($"Fetched and persisted {persisted.RowCount} historical telemetry row(s) for {sqlRange.Label}.");
                            Console.WriteLine(ResponseFormatter.FormatPersistence(persisted));
                            break;
                    }
                }
                catch (EnirisRateLimitError rateLimitError)
                {
                    logger.LogWarning(rateLimitError, "Eniris rate limit encountered. RetryAfter={RetryAfter}", rateLimitError.RetryAfter);
                    Console.WriteLine($"Rate limit encountered. Retry after: {rateLimitError.RetryAfter?.ToString() ?? "unspecified"}.");
                }
                catch (EnirisAuthError authError)
                {
                    authentication = null;
                    discovery = null;
                    latestValues = null;
                    historicalValues = null;
                    logger.LogError(authError, "Authentication failed");
                    Console.WriteLine($"Authentication failed: {authError.Message}");
                }
                catch (EnirisApiError apiError)
                {
                    logger.LogError(apiError, "Eniris API call failed");
                    Console.WriteLine($"Eniris API call failed: {apiError.Message}");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Unexpected example failure");
                    Console.WriteLine($"Unexpected error: {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Example execution cancelled");
        }

        return 0;
    }

    private static async Task<AuthenticationSummary> EnsureAuthenticatedAsync(
        IServiceProvider serviceProvider,
        ExampleConfig config,
        AuthenticationSummary? current,
        CancellationToken cancellationToken)
        => current ?? await AuthenticateAsync(serviceProvider, config, cancellationToken).ConfigureAwait(false);

    private static async Task<AuthenticationSummary> AuthenticateAsync(IServiceProvider serviceProvider, ExampleConfig config, CancellationToken cancellationToken)
    {
        var username = ResolveRequiredValue("Eniris username", config.Username);
        var password = ResolveRequiredValue("Eniris password", config.Password, secret: true);
        config.Username = username;

        var example = serviceProvider.GetRequiredService<AuthenticationExample>();
        return await example.RunAsync(username, password, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DeviceDiscoverySummary> DiscoverAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => await serviceProvider.GetRequiredService<DeviceDiscoveryExample>().RunAsync(cancellationToken).ConfigureAwait(false);

    private static async Task<IReadOnlyList<Eniris.Telemetry.SensorValue>> FetchLatestAsync(
        IServiceProvider serviceProvider,
        DeviceDiscoverySummary discovery,
        ExampleConfig config,
        CancellationToken cancellationToken)
    {
        var example = serviceProvider.GetRequiredService<LatestTelemetryExample>();
        return await example.RunAsync(discovery.Devices, config.PreferredTelemetryFields, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<Eniris.Data.TelemetryRecord>> FetchHistoricalAsync(
        IServiceProvider serviceProvider,
        DeviceDiscoverySummary discovery,
        ExampleConfig config,
        ExampleDateRange range,
        bool chunked,
        CancellationToken cancellationToken)
    {
        var example = serviceProvider.GetRequiredService<HistoricalTelemetryExample>();
        var selectedFieldCount = chunked ? 2 : 1;
        using var targets = SelectTelemetryTargets(
            discovery.Devices,
            config.PreferredTelemetryFields,
            selectedFieldCount,
            minimumFieldCount: 1).GetEnumerator();
        if (!targets.MoveNext())
        {
            throw new InvalidOperationException("No discovered device exposed a telemetry source for the requested fields.");
        }

        var target = targets.Current;
        return chunked
            ? await example.RunChunkedRangeQueryAsync(target.Device, target.Source, target.Fields, range.Start, range.End, cancellationToken).ConfigureAwait(false)
            : await example.RunDirectRangeQueryAsync(target.Device, target.Source, target.Fields, range.Start, range.End, config.HistoricalQueryLimit, cancellationToken).ConfigureAwait(false);
    }

    private static void DisplayQueries(IServiceProvider serviceProvider, DeviceDiscoverySummary? discovery)
    {
        var example = serviceProvider.GetRequiredService<TelemetryQueryBuilderExample>();
        TelemetrySource? source = null;
        if (discovery is not null)
        {
            using var targets = SelectTelemetryTargets(
                discovery.Devices,
                ["actualPowerTot_W", "voltageL1N_V"],
                selectedFieldCount: 2,
                minimumFieldCount: 1).GetEnumerator();
            if (targets.MoveNext())
            {
                source = targets.Current.Source;
            }
        }

        if (source is null)
        {
            Console.WriteLine("No discovered telemetry source is available yet; showing generic sample queries.");
        }

        foreach (var (name, query) in example.BuildExamples(source))
        {
            Console.WriteLine(ResponseFormatter.FormatQuery(name, query));
        }
    }

    private static async Task<SqlServerPersistenceSummary> PersistHistoricalAsync(
        IServiceProvider serviceProvider,
        DeviceDiscoverySummary discovery,
        ExampleConfig config,
        ExampleDateRange range,
        CancellationToken cancellationToken)
    {
        config.SqlServerConnectionString = ResolveRequiredValue("SQL Server connection string", config.SqlServerConnectionString, secret: true);
        var example = serviceProvider.GetRequiredService<HistoricalTelemetryExample>();
        var writer = serviceProvider.GetRequiredService<SqlServerTelemetryWriter>();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Eniris.Examples.Program");
        var persistedCount = 0;
        var matchedAny = false;

        foreach (var target in SelectTelemetryTargets(
            discovery.Devices,
            config.PreferredTelemetryFields,
            selectedFieldCount: 2,
            minimumFieldCount: 1))
        {
            matchedAny = true;
            logger.LogInformation(
                "Persisting historical telemetry for {DeviceName} ({DeviceId}) source {SourceKey} with fields [{Fields}]",
                target.Device.Name,
                target.Device.Id,
                target.Source.Key,
                string.Join(", ", target.Fields));

            var batches = example.StreamChunkedRangeQueryAsync(target.Device, target.Source, target.Fields, range.Start, range.End, cancellationToken);
            var persisted = await writer.PersistBatchesAsync(batches, cancellationToken).ConfigureAwait(false);
            persistedCount += persisted.RowCount;
        }

        if (!matchedAny)
        {
            throw new InvalidOperationException("No discovered device exposed a telemetry source for the requested fields.");
        }

        return new SqlServerPersistenceSummary("TelemetryData", persistedCount);
    }

    private static IEnumerable<(EnirisDevice Device, TelemetrySource Source, string[] Fields)> SelectTelemetryTargets(
        IReadOnlyList<EnirisDevice> devices,
        IReadOnlyList<string> requestedFields,
        int selectedFieldCount,
        int minimumFieldCount)
    {
        foreach (var device in devices.Where(static candidate => candidate.TelemetrySources.Count > 0))
        {
            foreach (var source in device.TelemetrySources)
            {
                var fields = SelectFields(source, requestedFields).Take(selectedFieldCount).ToArray();
                if (fields.Length >= minimumFieldCount)
                {
                    yield return (device, source, fields);
                }
            }
        }
    }

    private static IReadOnlyList<string> SelectFields(TelemetrySource source, IReadOnlyList<string> requestedFields)
    {
        return TelemetryFieldSelection.SelectFields(source, requestedFields);
    }

    private static string ResolveRequiredValue(string label, string? currentValue, bool secret = false)
    {
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            return currentValue;
        }

        if (Console.IsInputRedirected && string.IsNullOrWhiteSpace(currentValue))
        {
            throw new InvalidOperationException($"{label} is required. Set it in appsettings.json or as an environment variable before running non-interactively.");
        }

        return ConsoleMenu.PromptRequired(label, currentValue, secret);
    }
}
