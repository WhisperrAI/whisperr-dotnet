using System;

namespace Whisperr;

/// <summary>
/// Whisperr refused a catalog call, or could not be reached.
/// </summary>
public sealed class WhisperrCatalogException : Exception
{
    internal WhisperrCatalogException(string message, int status, string code, bool retryable, object? partial = null)
        : base("[whisperr] " + message)
    {
        Status = status;
        Code = code;
        Retryable = retryable;
        Partial = partial;
    }

    /// <summary>HTTP status; 0 for a network error or timeout.</summary>
    public int Status { get; }

    /// <summary>
    /// The server's error code (for example <c>server_key_required</c>,
    /// <c>catalog_push_disabled</c>, <c>snapshot_not_open</c>), or <c>network_error</c>.
    /// </summary>
    public string Code { get; }

    /// <summary>True when retries ran out on a transient failure; calling again later can succeed.</summary>
    public bool Retryable { get; }

    /// <summary>
    /// For upsert and delete: what earlier chunks already did, a <see cref="CatalogUpsertResult"/>
    /// or a <see cref="CatalogDeleteResult"/>.
    /// </summary>
    public object? Partial { get; }

    internal WhisperrCatalogException WithPartial(object partial)
    {
        return new WhisperrCatalogException(Message.Substring("[whisperr] ".Length), Status, Code, Retryable, partial);
    }
}

/// <summary>
/// A key or argument the Whisperr API can never accept. Nothing was sent.
/// </summary>
public sealed class WhisperrConfigurationException : InvalidOperationException
{
    internal WhisperrConfigurationException(string message)
        : base("[whisperr] " + message)
    {
    }
}
