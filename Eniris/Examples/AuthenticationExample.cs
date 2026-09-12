#nullable enable
using Eniris.Api;
using Microsoft.Extensions.Logging;

namespace Eniris.Examples.Examples;

public sealed class AuthenticationExample
{
    private readonly IEnirisClient _client;
    private readonly ILogger<AuthenticationExample> _logger;

    public AuthenticationExample(IEnirisClient client, ILogger<AuthenticationExample> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AuthenticationSummary> RunAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        _logger.LogInformation("Logging in to Eniris as {Username}", username);
        var refreshToken = await _client.LoginAsync(username, password, cancellationToken).ConfigureAwait(false);
        var accessToken = await _client.GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Refreshing the Eniris refresh token");
        var renewedRefreshToken = await _client.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
        var renewedAccessToken = await _client.GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return new AuthenticationSummary(refreshToken, accessToken, renewedRefreshToken, renewedAccessToken);
    }
}

public sealed record AuthenticationSummary(
    string RefreshToken,
    string AccessToken,
    string RenewedRefreshToken,
    string RenewedAccessToken);
