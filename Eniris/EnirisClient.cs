#nullable enable
using Eniris.Api;
using Eniris.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;

namespace Eniris;

/// <summary>
/// High-level facade for Eniris authentication and SmartgridOne API access.
/// </summary>
public sealed class EnirisClient : IEnirisClient
{
    /// <summary>
    /// The named <see cref="HttpClient"/> registration used for authentication requests.
    /// </summary>
    public const string AuthHttpClientName = "Eniris.Authentication";

    /// <summary>
    /// The named <see cref="HttpClient"/> registration used for SmartgridOne API requests.
    /// </summary>
    public const string ApiHttpClientName = "Eniris.Api";

    private readonly HttpClient _apiHttpClient;
    private readonly IEnirisAuthClient _authClient;
    private readonly ILoggerFactory _loggerFactory;
    private EnirisApiClient? _apiClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnirisClient"/> class using manually supplied HTTP clients.
    /// </summary>
    public EnirisClient(HttpClient authHttpClient, HttpClient apiHttpClient, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(authHttpClient);
        ArgumentNullException.ThrowIfNull(apiHttpClient);

        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _authClient = new EnirisAuthClient(authHttpClient, _loggerFactory.CreateLogger<EnirisAuthClient>());
        _apiHttpClient = apiHttpClient;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EnirisClient"/> class from dependency injection.
    /// </summary>
    public EnirisClient(IHttpClientFactory httpClientFactory, ILoggerFactory? loggerFactory = null)
        : this(
            (httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory))).CreateClient(AuthHttpClientName),
            httpClientFactory.CreateClient(ApiHttpClientName),
            loggerFactory)
    {
    }

    /// <inheritdoc />
    public string? RefreshToken => _apiClient?.RefreshToken;

    /// <inheritdoc />
    public async Task<string> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var refreshToken = await _authClient.LoginAsync(username, password, cancellationToken).ConfigureAwait(false);
        SetRefreshToken(refreshToken);
        return refreshToken;
    }

    /// <inheritdoc />
    public void SetRefreshToken(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        if (_apiClient is null)
        {
            _apiClient = new EnirisApiClient(
                _apiHttpClient,
                refreshToken,
                _authClient,
                _loggerFactory.CreateLogger<EnirisApiClient>());
            return;
        }

        _apiClient.UpdateRefreshToken(refreshToken);
    }

    /// <inheritdoc />
    public Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
        RequireApiClient().GetAccessTokenAsync(forceRefresh, cancellationToken);

    /// <inheritdoc />
    public Task<string> RefreshTokenAsync(CancellationToken cancellationToken = default) =>
        RequireApiClient().RefreshTokenAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<JsonObject>> CompaniesAsync(CancellationToken cancellationToken = default) =>
        RequireApiClient().CompaniesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<JsonObject>> RolesAsync(CancellationToken cancellationToken = default) =>
        RequireApiClient().RolesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<JsonObject>> MonitorsAsync(string roleId = "*", CancellationToken cancellationToken = default) =>
        RequireApiClient().MonitorsAsync(roleId, cancellationToken);

    /// <inheritdoc />
    public Task<JsonObject?> DevicesAsync(string? skipHash = null, CancellationToken cancellationToken = default) =>
        RequireApiClient().DevicesAsync(skipHash, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<JsonObject>> TelemetryAsync(IReadOnlyList<JsonObject> queries, CancellationToken cancellationToken = default) =>
        RequireApiClient().TelemetryAsync(queries, cancellationToken);

    private EnirisApiClient RequireApiClient() =>
        _apiClient ?? throw new InvalidOperationException("A refresh token must be configured before calling the Eniris API.");
}
