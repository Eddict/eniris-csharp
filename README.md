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
