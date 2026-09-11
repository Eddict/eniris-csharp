#nullable enable
using System.Net;
using System.Net.Http.Json;
using Eniris.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Eniris.Authentication;

/// <summary>
/// Client for the Eniris authentication API.
/// </summary>
public sealed class EnirisAuthClient : IEnirisAuthClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EnirisAuthClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnirisAuthClient"/> class.
    /// </summary>
    public EnirisAuthClient(HttpClient httpClient, ILogger<EnirisAuthClient>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? NullLogger<EnirisAuthClient>.Instance;
    }

    /// <inheritdoc />
    public Task<string> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        return SendTokenRequestAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "/auth/login")
            {
                Content = JsonContent.Create(new { username, password }),
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> GetAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        return SendTokenRequestAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/auth/accesstoken");
                request.Headers.Authorization = new("Bearer", refreshToken);
                return request;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        return SendTokenRequestAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/auth/refreshtoken");
                request.Headers.Authorization = new("Bearer", refreshToken);
                return request;
            },
            cancellationToken);
    }

    private async Task<string> SendTokenRequestAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = requestFactory();
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = await EnirisResponseReader.ReadAsync(response, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var token = EnirisResponseReader.NormalizeToken(content.Text);
                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new EnirisAuthError("Eniris returned an empty token");
                }

                return token;
            }

            var error = EnirisResponseReader.GetErrorMessage(response, content);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (EnirisResponseReader.IsTwoFactorError(error))
                {
                    throw new EnirisTwoFactorRequired(error);
                }

                throw new EnirisAuthError(error);
            }

            if ((int)response.StatusCode == 429)
            {
                throw new EnirisRateLimitError(error, EnirisResponseReader.GetRetryAfter(response, content));
            }

            throw new EnirisApiError(error);
        }
        catch (EnirisApiError)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogDebug(exception, "Failed to connect to Eniris authentication endpoint");
            throw new EnirisAuthError($"Failed to connect to Eniris authentication endpoint: {exception.Message}", exception);
        }
    }
}
