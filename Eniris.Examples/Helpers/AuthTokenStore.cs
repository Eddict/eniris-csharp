#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eniris.Examples.Helpers;

public sealed class AuthTokenStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;

    public AuthTokenStore(string? homeDirectory = null)
    {
        var resolvedHome = string.IsNullOrWhiteSpace(homeDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : homeDirectory;

        _filePath = Path.Combine(
            resolvedHome,
            ".config",
            "eniris",
            "auth.json");
    }

    public string FilePath => _filePath;

    public AuthTokenData? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<AuthTokenData>(json);
            if (data is null ||
                string.IsNullOrWhiteSpace(data.Username) ||
                string.IsNullOrWhiteSpace(data.RefreshToken))
            {
                return null;
            }

            return data;
        }
        catch (Exception) when (
            File.Exists(_filePath))
        {
            return null;
        }
    }

    public void Save(AuthTokenData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(data.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(data.RefreshToken);

        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException($"Could not resolve directory for token file path {_filePath}.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(data, SerializerOptions));
    }
}

public sealed class AuthTokenData
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token_created_at")]
    public string RefreshTokenCreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}
