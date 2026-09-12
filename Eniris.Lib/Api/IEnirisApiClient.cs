#nullable enable
using System.Text.Json.Nodes;

namespace Eniris.Api;

/// <summary>
/// Operations supported by the Eniris metadata and telemetry APIs.
/// </summary>
public interface IEnirisApiClient
{
    /// <summary>
    /// Gets the refresh token used by the client.
    /// </summary>
    string RefreshToken { get; }

    /// <summary>
    /// Gets or refreshes the short-lived access token.
    /// </summary>
    Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews and stores the refresh token used by the client.
    /// </summary>
    Task<string> RefreshTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the refresh token after another workflow renews it.
    /// </summary>
    void UpdateRefreshToken(string refreshToken);

    /// <summary>
    /// Executes an authenticated Eniris API request.
    /// </summary>
    Task<JsonNode?> RequestAsync(
        HttpMethod method,
        string path,
        JsonNode? payload = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists companies associated with the authenticated user.
    /// </summary>
    Task<IReadOnlyList<JsonObject>> CompaniesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists roles associated with the authenticated user.
    /// </summary>
    Task<IReadOnlyList<JsonObject>> RolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists monitor relations for a role.
    /// </summary>
    Task<IReadOnlyList<JsonObject>> MonitorsAsync(string roleId = "*", CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries accessible devices.
    /// </summary>
    Task<JsonObject?> DevicesAsync(string? skipHash = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes telemetry queries.
    /// </summary>
    Task<IReadOnlyList<JsonObject>> TelemetryAsync(IReadOnlyList<JsonObject> queries, CancellationToken cancellationToken = default);
}
