using System.Collections.Generic;

namespace Whisperr;

/// <summary>
/// Optional traits and delivery channels for an identify call.
/// </summary>
public sealed class IdentifyOptions
{
    /// <summary>
    /// Arbitrary user traits, such as plan, signup date, or account state.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Traits { get; set; }

    /// <summary>
    /// Convenience shortcut that expands to an opted-in email channel.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Convenience shortcut that expands to an opted-in SMS channel.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Convenience shortcut that expands to an opted-in push channel.
    /// </summary>
    public string? PushToken { get; set; }

    /// <summary>
    /// Preferred outreach channel for this user.
    /// </summary>
    public WhisperrChannelType? PreferredChannel { get; set; }

    /// <summary>
    /// Explicit channels. When set and non-empty, these override the shortcut fields.
    /// </summary>
    public IReadOnlyList<WhisperrChannel>? Channels { get; set; }
}
