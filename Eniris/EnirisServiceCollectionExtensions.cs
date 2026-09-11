#nullable enable
using Eniris.Authentication;
using Eniris.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Eniris;

/// <summary>
/// Dependency-injection registration helpers for Eniris clients.
/// </summary>
public static class EnirisServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Eniris library with named <see cref="HttpClient"/> instances and the facade client.
    /// </summary>
    public static IServiceCollection AddEniris(
        this IServiceCollection services,
        Action<EnirisOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<EnirisOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddHttpClient(EnirisClient.AuthHttpClientName, static (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<EnirisOptions>>().Value;
            client.BaseAddress = options.AuthBaseUri;
            client.Timeout = options.RequestTimeout;
        });

        services.AddHttpClient(EnirisClient.ApiHttpClientName, static (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<EnirisOptions>>().Value;
            client.BaseAddress = options.ApiBaseUri;
            client.Timeout = options.RequestTimeout;
        });

        services.AddTransient<IEnirisAuthClient>(static serviceProvider =>
        {
            var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var logger = serviceProvider.GetService<ILogger<EnirisAuthClient>>();
            return new EnirisAuthClient(factory.CreateClient(EnirisClient.AuthHttpClientName), logger);
        });

        services.AddScoped<EnirisClient>(static serviceProvider =>
        {
            var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var authClient = serviceProvider.GetRequiredService<IEnirisAuthClient>();
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            return new EnirisClient(authClient, factory.CreateClient(EnirisClient.ApiHttpClientName), loggerFactory);
        });
        services.AddScoped<IEnirisClient>(static serviceProvider => serviceProvider.GetRequiredService<EnirisClient>());

        return services;
    }
}
