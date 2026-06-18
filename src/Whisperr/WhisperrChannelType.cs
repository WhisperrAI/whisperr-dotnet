using System;

namespace Whisperr;

/// <summary>
/// Contact channel types supported by the Whisperr ingestion contract.
/// </summary>
public enum WhisperrChannelType
{
    /// <summary>
    /// Email delivery.
    /// </summary>
    Email,

    /// <summary>
    /// SMS delivery.
    /// </summary>
    Sms,

    /// <summary>
    /// Push notification delivery.
    /// </summary>
    Push
}

internal static class WhisperrChannelTypeExtensions
{
    public static string ToWireValue(this WhisperrChannelType type)
    {
        switch (type)
        {
            case WhisperrChannelType.Email:
                return "email";
            case WhisperrChannelType.Sms:
                return "sms";
            case WhisperrChannelType.Push:
                return "push";
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown Whisperr channel type.");
        }
    }
}
