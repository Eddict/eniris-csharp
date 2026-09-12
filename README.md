# eniris-csharp

C# .NET 10 library for the Eniris SmartgridOne authentication and API endpoints.

## Contents

- `Eniris.sln`
- `Eniris/Eniris.csproj`
- `Eniris.Authentication.EnirisAuthClient` for login and token renewal
- `Eniris.Api.EnirisApiClient` for companies, roles, monitors, devices, and telemetry
- `Eniris.Models` for device/controller/source parsing
- `Eniris.Telemetry` for query building, latest-value parsing, and historical fetching
- `Eniris.Data` for MSSQL-friendly telemetry record mapping
- `EnirisClient` facade plus `AddEniris(...)` dependency-injection helpers

## Usage

```csharp
using Eniris;
using Eniris.Configuration;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection()
    .AddEniris(options =>
    {
        options.AuthBaseUri = EnirisConstants.DefaultAuthBaseUri;
        options.ApiBaseUri = EnirisConstants.DefaultApiBaseUri;
    });

var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var client = scope.ServiceProvider.GetRequiredService<IEnirisClient>();

var refreshToken = await client.LoginAsync("user@example.com", "password");
var companies = await client.CompaniesAsync();
```

For telemetry discovery, parse devices with `EnirisDevice.ParseMany(...)`, group them with `EnirisController.GroupControllers(...)`, build request payloads with `TelemetryQueryBuilder`, and parse responses with `TelemetryResponseParser`.

## Example console application

A runnable example app lives in `Eniris.Examples/` and demonstrates:

- authentication (login, access-token exchange, refresh-token renewal)
- companies, roles, monitors, device discovery, and controller grouping
- latest telemetry queries for common power and voltage fields
- historical telemetry queries over 7-day and 30-day ranges
- persisting historical telemetry records into SQL Server with `SqlBulkCopy`
- `where.time` query construction, namespace examples, and aggregation examples

Run it with:

```bash
dotnet run --project Eniris.Examples/Eniris.Examples.csproj
```

Configuration can come from `Eniris.Examples/appsettings.json` or environment variables such as `Eniris__Username`, `Eniris__Password`, `Eniris__SqlServerConnectionString`, `Eniris__AuthBaseUri`, and `Eniris__ApiBaseUri`.
The examples app also persists `username`, `refresh_token`, and `refresh_token_created_at` in `~/.config/eniris/auth.json` so subsequent runs can reuse and renew tokens without prompting for a password every time.

Expected console output includes controller/device hierarchy, latest telemetry timestamps and values, and historical row counts with covered date ranges.

```csharp
using Eniris.Data;
using Eniris.Models;
using Eniris.Telemetry;

var devicesPayload = await client.DevicesAsync();
var devices = EnirisDevice.ParseMany(devicesPayload);

client.SetRefreshToken(refreshToken);
var fetcher = new HistoricalDataFetcher(client);
var records = await fetcher.FetchAsync(
    devices[0],
    devices[0].TelemetrySources[0],
    ["actualPowerTot_W", "voltageL1N_V"],
    DateTime.UtcNow.AddDays(-7),
    DateTime.UtcNow);

foreach (TelemetryRecord record in records)
{
    Console.WriteLine($"{record.Timestamp:o} {record.DeviceName} {record.Field}={record.Value}");
}
```

To persist retrieved historical data into SQL Server, use the example app menu option `8`. It fetches chunked historical telemetry, ensures `dbo.TelemetryData` exists, and bulk inserts the mapped `TelemetryRecord` rows with SQL Server's native bulk copy support.
