namespace Whisperr;

/// <summary>
/// A contact channel for a user identified in Whisperr.
/// </summary>
public sealed class WhisperrChannel
{
    /// <summary>
    /// Creates an empty channel for object-initializer usage.
    /// </summary>
    public WhisperrChannel()
    {
    }

    /// <summary>
    /// Creates a channel with the supplied type and address.
    /// </summary>
    public WhisperrChannel(WhisperrChannelType type, string address, bool? optedIn = null, bool? verified = null)
    {
        Type = type;
        Address = address;
        OptedIn = optedIn;
        Verified = verified;
    }

    /// <summary>
    /// Channel type. Whisperr accepts email, SMS, and push.
    /// </summary>
    public WhisperrChannelType Type { get; set; }

    /// <summary>
    /// Email address, E.164 phone number, or push token for the channel.
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Whether the user opted in to this channel. Defaults to true when omitted.
    /// </summary>
    public bool? OptedIn { get; set; }

    /// <summary>
    /// Whether the address has been verified. Omitted from the wire payload when null.
    /// </summary>
    public bool? Verified { get; set; }

    /// <summary>
    /// Creates an opted-in email channel.
    /// </summary>
    public static WhisperrChannel Email(string address, bool? verified = null)
    {
        return new WhisperrChannel(WhisperrChannelType.Email, address, optedIn: true, verified: verified);
    }

    /// <summary>
    /// Creates an opted-in SMS channel.
    /// </summary>
    public static WhisperrChannel Sms(string phoneNumber, bool? verified = null)
    {
        return new WhisperrChannel(WhisperrChannelType.Sms, phoneNumber, optedIn: true, verified: verified);
    }

    /// <summary>
    /// Creates an opted-in push channel.
    /// </summary>
    public static WhisperrChannel Push(string token, bool? verified = null)
    {
        return new WhisperrChannel(WhisperrChannelType.Push, token, optedIn: true, verified: verified);
    }
}
