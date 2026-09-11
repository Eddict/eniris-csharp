#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Eniris.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Eniris.Api;

/// <summary>
/// Client for Eniris metadata and telemetry APIs.
/// </summary>
public sealed class EnirisApiClient : IEnirisApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IEnirisAuthClient _authClient;
    private readonly ILogger<EnirisApiClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string _refreshToken;
    private string? _accessToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnirisApiClient"/> class.
    /// </summary>
    public EnirisApiClient(
        HttpClient httpClient,
        string refreshToken,
        IEnirisAuthClient authClient,
        ILogger<EnirisApiClient>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        _refreshToken = refreshToken;
        _logger = logger ?? NullLogger<EnirisApiClient>.Instance;
    }

    /// <inheritdoc />
    public string RefreshToken => _refreshToken;

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && !string.IsNullOrWhiteSpace(_accessToken))
            {
                return _accessToken;
            }

            _accessToken = await _authClient.GetAccessTokenAsync(_refreshToken, cancellationToken).ConfigureAwait(false);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _refreshToken = await _authClient.RefreshTokenAsync(_refreshToken, cancellationToken).ConfigureAwait(false);
            _accessToken = null;
            return _refreshToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <inheritdoc />
    public void UpdateRefreshToken(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        _tokenLock.Wait();
        try
        {
            _refreshToken = refreshToken;
            _accessToken = null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<JsonNode?> RequestAsync(
        HttpMethod method,
        string path,
        JsonNode? payload = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var accessToken = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return await SendAsync(method, path, payload, accessToken, retryAuth: true, retryRateLimit: true, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JsonObject>> CompaniesAsync(CancellationToken cancellationToken = default)
    {
        var payload = await RequestAsync(HttpMethod.Get, "/v1/company", cancellationToken: cancellationToken).ConfigureAwait(false);
        return ReadObjectArrayProperty(payload, "company");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JsonObject>> RolesAsync(CancellationToken cancellationToken = default)
    {
        var payload = await RequestAsync(HttpMethod.Post, "/v1/role/query", new JsonObject(), cancellationToken).ConfigureAwait(false);
        return ReadObjectArrayProperty(payload, "role");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JsonObject>> MonitorsAsync(string roleId = "*", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        var payload = await RequestAsync(HttpMethod.Post, $"/v1/role/{Uri.EscapeDataString(roleId)}/monitors/query", new JsonObject(), cancellationToken).ConfigureAwait(false);
        return ReadObjectArrayProperty(payload, "monitors");
    }

    /// <inheritdoc />
    public async Task<JsonObject?> DevicesAsync(string? skipHash = null, CancellationToken cancellationToken = default)
    {
        JsonObject? payload = null;
        if (!string.IsNullOrWhiteSpace(skipHash))
        {
            payload = new JsonObject
            {
                ["skipHash"] = skipHash,
            };
        }

        return await RequestAsync(HttpMethod.Post, "/v1/device/query", payload ?? new JsonObject(), cancellationToken).ConfigureAwait(false) as JsonObject;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JsonObject>> TelemetryAsync(IReadOnlyList<JsonObject> queries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queries);

        if (queries.Count == 0)
        {
            return Array.Empty<JsonObject>();
        }

        var queryPayload = new JsonArray(queries.Select(query => query.DeepClone()).ToArray());
        var payload = await RequestAsync(HttpMethod.Post, "/v1/telemetry/query", queryPayload, cancellationToken).ConfigureAwait(false);

        return payload switch
        {
            JsonArray array => array.OfType<JsonObject>().ToArray(),
            JsonObject single => [single],
            _ => Array.Empty<JsonObject>(),
        };
    }

    private async Task<JsonNode?> SendAsync(
        HttpMethod method,
        string path,
        JsonNode? payload,
        string accessToken,
        bool retryAuth,
        bool retryRateLimit,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new("Bearer", accessToken);
            if (payload is not null)
            {
                request.Content = JsonContent.Create(payload);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = await EnirisResponseReader.ReadAsync(response, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && retryAuth)
            {
                _logger.LogDebug("Refreshing Eniris access token after HTTP 401 from {Path}", path);
                var refreshedToken = await GetAccessTokenAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
                return await SendAsync(method, path, payload, refreshedToken, retryAuth: false, retryRateLimit, cancellationToken).ConfigureAwait(false);
            }

            if ((int)response.StatusCode == 429)
            {
                var retryAfter = EnirisResponseReader.GetRetryAfter(response, content);
                if (retryRateLimit && retryAfter is { } delay)
                {
                    _logger.LogDebug("Retrying Eniris request to {Path} after rate limit delay of {Delay}", path, delay);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    return await SendAsync(method, path, payload, accessToken, retryAuth, retryRateLimit: false, cancellationToken).ConfigureAwait(false);
                }

                throw new EnirisRateLimitError(EnirisResponseReader.GetErrorMessage(response, content), retryAfter);
            }

            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(content.Text))
                {
                    return null;
                }

                return content.Json ?? JsonValue.Create(EnirisResponseReader.NormalizeToken(content.Text));
            }

            var error = EnirisResponseReader.GetErrorMessage(response, content);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new EnirisAuthError(error);
            }

            throw new EnirisApiError(error);
        }
        catch (EnirisApiError)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new EnirisApiError($"Failed to connect to Eniris: {exception.Message}", exception);
        }
    }

    private static IReadOnlyList<JsonObject> ReadObjectArrayProperty(JsonNode? payload, string propertyName)
    {
        if (payload is not JsonObject jsonObject ||
            jsonObject[propertyName] is not JsonArray array)
        {
            return Array.Empty<JsonObject>();
        }

        return array.OfType<JsonObject>().ToArray();
    }
}
