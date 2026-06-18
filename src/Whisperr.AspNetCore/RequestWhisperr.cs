using System.Collections.Generic;
using Whisperr;

namespace Whisperr.AspNetCore;

/// <summary>
/// A per-request Whisperr helper bound to the request's resolved user id.
/// <see cref="Track"/> and <see cref="Identify"/> no-op when no user was
/// resolved for the request.
/// </summary>
public sealed class RequestWhisperr
{
    private readonly WhisperrClient _client;

    internal RequestWhisperr(WhisperrClient client, string? userId)
    {
        _client = client;
        UserId = userId;
    }

    /// <summary>
    /// The user id resolved for this request, or <c>null</c> when none was found.
    /// </summary>
    public string? UserId { get; }

    /// <summary>
    /// The underlying singleton client, for advanced use.
    /// </summary>
    public WhisperrClient Client => _client;

    /// <summary>
    /// Record an event for this request's user. No-op without a resolved user.
    /// </summary>
    public bool Track(
        string eventType,
        IReadOnlyDictionary<string, object?>? properties = null,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        return UserId != null && _client.Track(UserId, eventType, properties, context);
    }

    /// <summary>
    /// Identify this request's user. No-op without a resolved user.
    /// </summary>
    public bool Identify(IdentifyOptions? options = null)
    {
        return UserId != null && _client.Identify(UserId, options);
    }
}
