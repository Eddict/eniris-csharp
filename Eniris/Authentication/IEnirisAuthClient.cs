#nullable enable
namespace Eniris.Authentication;

/// <summary>
/// Operations supported by the Eniris authentication API.
/// </summary>
public interface IEnirisAuthClient
{
    /// <summary>
    /// Exchanges username and password credentials for a refresh token.
    /// </summary>
    Task<string> LoginAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a refresh token for a short-lived access token.
    /// </summary>
    Task<string> GetAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews a refresh token.
    /// </summary>
    Task<string> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}
