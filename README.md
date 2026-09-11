# eniris-csharp

C# .NET 10 library for the Eniris SmartgridOne authentication and API endpoints.

## Contents

- `Eniris.sln`
- `Eniris/Eniris.csproj`
- `Eniris.Authentication.EnirisAuthClient` for login and token renewal
- `Eniris.Api.EnirisApiClient` for companies, roles, monitors, devices, and telemetry
- `Eniris.Models` for device/controller/source parsing
- `Eniris.Telemetry` for query building and response parsing
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
var client = provider.GetRequiredService<IEnirisClient>();

var refreshToken = await client.LoginAsync("user@example.com", "password");
var companies = await client.CompaniesAsync();
```

For telemetry discovery, parse devices with `EnirisDevice.ParseMany(...)`, group them with `EnirisController.GroupControllers(...)`, build request payloads with `TelemetryQueryBuilder`, and parse responses with `TelemetryResponseParser`.
