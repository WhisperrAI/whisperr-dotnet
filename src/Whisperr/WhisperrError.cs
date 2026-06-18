namespace Whisperr;

/// <summary>
/// Delivery error surfaced by the Whisperr client.
/// </summary>
public sealed class WhisperrError
{
    private WhisperrError(WhisperrErrorType type, string code, string message, int? status)
    {
        Type = type;
        Code = code;
        Message = message;
        Status = status;
    }

    /// <summary>
    /// Structured error type.
    /// </summary>
    public WhisperrErrorType Type { get; }

    /// <summary>
    /// Stable snake_case error code: auth, dropped, or retry_exhausted.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// HTTP status when known.
    /// </summary>
    public int? Status { get; }

    /// <summary>
    /// Creates an auth rejection error.
    /// </summary>
    public static WhisperrError Auth(string message, int? status = null)
    {
        return new WhisperrError(WhisperrErrorType.Auth, "auth", message, status);
    }

    /// <summary>
    /// Creates a dropped-event error.
    /// </summary>
    public static WhisperrError Dropped(string message, int? status = null)
    {
        return new WhisperrError(WhisperrErrorType.Dropped, "dropped", message, status);
    }

    /// <summary>
    /// Creates a retry-exhausted error.
    /// </summary>
    public static WhisperrError RetryExhausted(string message, int? status = null)
    {
        return new WhisperrError(WhisperrErrorType.RetryExhausted, "retry_exhausted", message, status);
    }
}

/// <summary>
/// Error categories surfaced by the SDK.
/// </summary>
public enum WhisperrErrorType
{
    /// <summary>
    /// The API key was rejected.
    /// </summary>
    Auth,

    /// <summary>
    /// An operation was intentionally dropped.
    /// </summary>
    Dropped,

    /// <summary>
    /// Transient retries were exhausted and the operation remains queued.
    /// </summary>
    RetryExhausted
}
