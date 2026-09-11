#nullable enable
using System.Globalization;
using System.Text.Json.Nodes;

namespace Eniris.Api;

internal static class EnirisResponseReader
{
    public static async Task<EnirisResponseContent> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return new EnirisResponseContent(text, TryParseJson(text));
    }

    public static string NormalizeToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var token = raw.Trim();
        if (token.Length >= 2 && token[0] == '"' && token[^1] == '"')
        {
            token = token[1..^1];
        }

        return token;
    }

    public static string GetErrorMessage(HttpResponseMessage response, EnirisResponseContent content)
    {
        if (content.Json is JsonObject payload)
        {
            var message = ReadString(payload["message"]) ?? ReadString(payload["error"]);
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }

        return string.IsNullOrWhiteSpace(content.Text)
            ? $"Eniris API returned HTTP {(int)response.StatusCode}"
            : content.Text.Trim();
    }

    public static bool IsTwoFactorError(string message) =>
        message.Contains("2FA", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("PARTIAL", StringComparison.OrdinalIgnoreCase);

    public static TimeSpan? GetRetryAfter(HttpResponseMessage response, EnirisResponseContent content)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            foreach (var value in values)
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                {
                    return TimeSpan.FromSeconds(Math.Max(0, seconds));
                }

                if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var retryAt))
                {
                    var delay = retryAt - DateTimeOffset.UtcNow;
                    return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
                }
            }
        }

        if (content.Json is JsonObject payload &&
            payload["retryAfter"] is JsonNode retryAfterNode &&
            TryReadDouble(retryAfterNode, out var retryAfterSeconds))
        {
            return TimeSpan.FromSeconds(Math.Max(0d, retryAfterSeconds));
        }

        return null;
    }

    public static JsonNode? TryParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch
        {
            return null;
        }
    }

    public static string? ReadString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            if (value.TryGetValue<bool>(out var boolValue))
            {
                return boolValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<double>(out var doubleValue))
            {
                return doubleValue.ToString(CultureInfo.InvariantCulture);
            }
        }

        return node.ToJsonString().Trim('"');
    }

    public static bool TryReadDouble(JsonNode? node, out double value)
    {
        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<double>(out value))
            {
                return true;
            }

            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                value = intValue;
                return true;
            }

            if (jsonValue.TryGetValue<long>(out var longValue))
            {
                value = longValue;
                return true;
            }
        }

        if (double.TryParse(ReadString(node), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}

internal sealed record EnirisResponseContent(string Text, JsonNode? Json);
