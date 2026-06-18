using System.Collections.Generic;

namespace Whisperr;

internal abstract class QueuedOperation
{
    protected QueuedOperation(string externalUserId)
    {
        ExternalUserId = externalUserId;
    }

    public string ExternalUserId { get; }
}

internal sealed class TrackOperation : QueuedOperation
{
    public TrackOperation(
        string externalUserId,
        string eventType,
        IReadOnlyDictionary<string, object?>? properties,
        IReadOnlyDictionary<string, object?>? context,
        string occurredAt,
        string messageId)
        : base(externalUserId)
    {
        EventType = eventType;
        Properties = properties;
        Context = context;
        OccurredAt = occurredAt;
        MessageId = messageId;
    }

    public string EventType { get; }

    public IReadOnlyDictionary<string, object?>? Properties { get; }

    public IReadOnlyDictionary<string, object?>? Context { get; }

    public string OccurredAt { get; }

    public string MessageId { get; }
}

internal sealed class IdentifyOperation : QueuedOperation
{
    public IdentifyOperation(
        string externalUserId,
        IReadOnlyDictionary<string, object?>? traits,
        WhisperrChannelType? preferredChannel,
        IReadOnlyList<WhisperrChannel>? channels)
        : base(externalUserId)
    {
        Traits = traits;
        PreferredChannel = preferredChannel;
        Channels = channels;
    }

    public IReadOnlyDictionary<string, object?>? Traits { get; }

    public WhisperrChannelType? PreferredChannel { get; }

    public IReadOnlyList<WhisperrChannel>? Channels { get; }
}
