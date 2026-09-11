#nullable enable
namespace Eniris.Configuration;

/// <summary>
/// Configurable options for the Eniris clients.
/// </summary>
public sealed class EnirisOptions
{
    /// <summary>
    /// Gets or sets the authentication API base URI.
    /// </summary>
    public Uri AuthBaseUri { get; set; } = EnirisConstants.DefaultAuthBaseUri;

    /// <summary>
    /// Gets or sets the SmartgridOne API base URI.
    /// </summary>
    public Uri ApiBaseUri { get; set; } = EnirisConstants.DefaultApiBaseUri;

    /// <summary>
    /// Gets or sets the default request timeout applied to HTTP clients.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
