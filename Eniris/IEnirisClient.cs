#nullable enable
using System.Text.Json.Nodes;

namespace Eniris;

/// <summary>
/// Facade that combines Eniris authentication and API access.
/// </summary>
public interface IEnirisClient
{
    /// <summary>
    /// Gets the current refresh token, if one has been configured.
    /// </summary>
    string? RefreshToken { get; }

    /// <summary>
    /// Configures the refresh token used for subsequent API calls.
    /// </summary>
    void SetRefreshToken(string refreshToken);

    /// <summary>
    /// Exchanges username and password credentials for a refresh token and stores it for later use.
    /// </summary>
    Task<string> LoginAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or refreshes the short-lived access token.
    /// </summary>
    Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews the current refresh token.
    /// </summary>
    Task<string> RefreshTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists companies associated with the authenticated account.
    /// </summary>
    Task<IReadOnlyList<JsonObject>> CompaniesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists roles associated with the authenticated account.
    /// </summary>
    Task<IReadOnlyList<JsonObject>> RolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists monitors for a role.
    /// </summary>
    Task<IReadOnlyList<JsonObject>> MonitorsAsync(string roleId = "*", CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries devices available to the authenticated account.
    /// </summary>
    Task<JsonObject?> DevicesAsync(string? skipHash = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes telemetry queries.
    /// </summary>
    Task<IReadOnlyList<JsonObject>> TelemetryAsync(IReadOnlyList<JsonObject> queries, CancellationToken cancellationToken = default);
}
