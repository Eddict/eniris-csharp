#nullable enable
namespace Eniris.Api;

/// <summary>
/// Base exception raised for Eniris API failures.
/// </summary>
public class EnirisApiError : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnirisApiError"/> class.
    /// </summary>
    public EnirisApiError(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EnirisApiError"/> class.
    /// </summary>
    public EnirisApiError(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when Eniris authentication fails.
/// </summary>
public class EnirisAuthError : EnirisApiError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnirisAuthError"/> class.
    /// </summary>
    public EnirisAuthError(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EnirisAuthError"/> class.
    /// </summary>
    public EnirisAuthError(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when Eniris asks the client to slow down.
/// </summary>
public class EnirisRateLimitError : EnirisApiError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnirisRateLimitError"/> class.
    /// </summary>
    public EnirisRateLimitError(string message, TimeSpan? retryAfter = null)
        : base(message)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Gets the retry delay requested by Eniris, if supplied.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>
/// Raised when the account requires an unsupported 2FA flow.
/// </summary>
public sealed class EnirisTwoFactorRequired : EnirisAuthError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnirisTwoFactorRequired"/> class.
    /// </summary>
    public EnirisTwoFactorRequired(string message)
        : base(message)
    {
    }
}
