using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Whisperr;

/// <summary>
/// Server-side Whisperr SDK client.
/// </summary>
public sealed class WhisperrClient : IDisposable
#if NET5_0_OR_GREATER
    , IAsyncDisposable
#endif
{
    private const string SnakeCasePattern = "^[a-z0-9]+(?:_[a-z0-9]+)*$";
    private static readonly Regex SnakeCase = new Regex(SnakeCasePattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object _queueGate = new object();
    private readonly List<QueuedOperation> _queue = new List<QueuedOperation>();
    private readonly SemaphoreSlim _drainLock = new SemaphoreSlim(1, 1);
    private readonly WhisperrTransport _transport;
    private readonly int _flushAt;
    private readonly int _maxBatchSize;
    private readonly int _maxQueueSize;
    private readonly int _maxRetries;
    private readonly bool _disabled;
    private readonly bool _debug;
    private readonly Action<WhisperrError>? _onError;
    private readonly Action<string>? _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string> _messageIdGenerator;
    private readonly Func<int, TimeSpan> _retryDelay;

    private Timer? _flushTimer;
    private bool _shutdown;
    private bool _disposed;

    /// <summary>
    /// Creates a Whisperr client with default options.
    /// </summary>
    public WhisperrClient(string apiKey)
        : this(new WhisperrOptions { ApiKey = apiKey })
    {
    }

    /// <summary>
    /// Creates a Whisperr client with explicit options.
    /// </summary>
    public WhisperrClient(WhisperrOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _disabled = options.Disabled;
        if (string.IsNullOrWhiteSpace(options.ApiKey) && !_disabled)
        {
            throw new ArgumentException("ApiKey is required unless the client is disabled.", nameof(options));
        }

        if (options.BaseUrl == null || !options.BaseUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("BaseUrl must be an absolute URI.", nameof(options));
        }

        _flushAt = Math.Max(1, options.FlushAt);
        _maxBatchSize = Math.Min(Math.Max(1, options.MaxBatchSize), 500);
        _maxQueueSize = Math.Max(1, options.MaxQueueSize);
        _maxRetries = Math.Max(0, options.MaxRetries);
        _debug = options.Debug;
        _onError = options.OnError;
        _logger = options.Logger;
        _clock = options.Clock ?? (() => DateTimeOffset.UtcNow);
        _messageIdGenerator = options.MessageIdGenerator ?? (() => Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture));
        _retryDelay = options.RetryDelay ?? WhisperrOptions.DefaultRetryDelay;

        _transport = new WhisperrTransport(
            options.BaseUrl,
            options.ApiKey ?? string.Empty,
            options.RequestTimeout,
            options.HttpClient,
            Warn);

        Ready = !_disabled;

        if (!_disabled && options.FlushInterval > TimeSpan.Zero)
        {
            _flushTimer = new Timer(_ => _ = FlushFromTimerAsync(), null, options.FlushInterval, options.FlushInterval);
        }
    }

    /// <summary>
    /// True when this client captures events. False when disabled.
    /// </summary>
    public bool Ready { get; }

    /// <summary>
    /// Current number of buffered operations.
    /// </summary>
    public int PendingCount
    {
        get
        {
            lock (_queueGate)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>
    /// Associate traits and contact channels with a user.
    /// </summary>
    public bool Identify(string externalUserId, IdentifyOptions? options = null)
    {
        if (!CanCapture(externalUserId))
        {
            return false;
        }

        options ??= new IdentifyOptions();
        if (options.PreferredChannel.HasValue && !IsDefinedChannelType(options.PreferredChannel.Value))
        {
            Emit(WhisperrError.Dropped("invalid preferred_channel - expected email, sms, or push"));
            Warn("invalid preferred_channel - identify was not queued");
            return false;
        }

        var channels = BuildChannels(options);
        if (!ValidateChannels(channels))
        {
            return false;
        }

        var op = new IdentifyOperation(
            externalUserId,
            SnapshotDictionary(options.Traits),
            options.PreferredChannel,
            channels);

        return Enqueue(op);
    }

    /// <summary>
    /// Record a product event for a known user.
    /// </summary>
    public bool Track(
        string externalUserId,
        string eventType,
        IReadOnlyDictionary<string, object?>? properties = null,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        if (!CanCapture(externalUserId) || string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        var type = eventType.Trim();
        if (!SnakeCase.IsMatch(type))
        {
            Emit(WhisperrError.Dropped($"invalid event_type \"{type}\" - expected snake_case"));
            Warn($"invalid event_type \"{type}\" - event was not queued");
            return false;
        }

        var op = new TrackOperation(
            externalUserId,
            type,
            SnapshotDictionary(properties),
            SnapshotDictionary(context),
            FormatTimestamp(_clock()),
            NextMessageId());

        return Enqueue(op);
    }

    /// <summary>
    /// Deliver everything currently buffered. Events that cannot be delivered
    /// because of auth or exhausted transient retries stay buffered.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_disabled)
        {
            return;
        }

        await _drainLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DrainAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _drainLock.Release();
        }
    }

    /// <summary>
    /// Stop the flush timer and run one final flush.
    /// </summary>
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        var timer = Interlocked.Exchange(ref _flushTimer, null);
        timer?.Dispose();

        await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the flush timer, runs a final flush, and releases SDK resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ShutdownAsync().GetAwaiter().GetResult();
        DisposeResources();
        GC.SuppressFinalize(this);
    }

#if NET5_0_OR_GREATER
    /// <summary>
    /// Stops the flush timer, runs a final flush, and releases SDK resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await ShutdownAsync().ConfigureAwait(false);
        DisposeResources();
        GC.SuppressFinalize(this);
    }
#endif

    internal string SerializeBatchForTest(IReadOnlyList<TrackOperation> events)
    {
        return _transport.SerializeBatch(events);
    }

    internal string SerializeIdentifyForTest(IdentifyOperation op)
    {
        return _transport.SerializeIdentify(op);
    }

    private bool CanCapture(string externalUserId)
    {
        return !_disabled && !_shutdown && !string.IsNullOrWhiteSpace(externalUserId);
    }

    private bool Enqueue(QueuedOperation op)
    {
        int dropped;
        int size;
        lock (_queueGate)
        {
            _queue.Add(op);
            dropped = Math.Max(0, _queue.Count - _maxQueueSize);
            if (dropped > 0)
            {
                _queue.RemoveRange(0, dropped);
            }

            size = _queue.Count;
        }

        if (dropped > 0)
        {
            Emit(WhisperrError.Dropped($"queue full - dropped {dropped} oldest event(s)"));
        }

        if (size >= _flushAt)
        {
            _ = FlushFromTimerAsync();
        }

        return true;
    }

    private async Task FlushFromTimerAsync()
    {
        if (_shutdown || _disposed)
        {
            return;
        }

        try
        {
            await FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            // Background delivery must never crash the host.
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var batch = TakeNextBatch();
            if (batch.Count == 0)
            {
                return;
            }

            DeliveryResult result;
            try
            {
                result = await DeliverAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                RequeueFront(batch);
                throw;
            }
            catch (Exception ex)
            {
                RequeueFront(batch);
                Warn("delivery failed unexpectedly - retained batch for next flush: " + ex.Message);
                Emit(WhisperrError.RetryExhausted("delivery failed unexpectedly; will retry on next flush"));
                return;
            }

            if (result == DeliveryResult.Ok)
            {
                continue;
            }

            if (result == DeliveryResult.Drop)
            {
                Emit(WhisperrError.Dropped($"dropped {batch.Count} event(s) - rejected by server"));
                continue;
            }

            RequeueFront(batch);
            if (result == DeliveryResult.Auth)
            {
                Emit(WhisperrError.Auth("delivery paused - API key rejected", status: 401));
            }
            else
            {
                Emit(WhisperrError.RetryExhausted("delivery failed after retries; will retry on next flush"));
            }

            return;
        }
    }

    private List<QueuedOperation> TakeNextBatch()
    {
        lock (_queueGate)
        {
            if (_queue.Count == 0)
            {
                return new List<QueuedOperation>();
            }

            if (_queue[0] is IdentifyOperation)
            {
                var identify = _queue[0];
                _queue.RemoveAt(0);
                return new List<QueuedOperation> { identify };
            }

            var count = 0;
            while (count < _queue.Count && count < _maxBatchSize && _queue[count] is TrackOperation)
            {
                count++;
            }

            var batch = _queue.GetRange(0, count);
            _queue.RemoveRange(0, count);
            return batch;
        }
    }

    private void RequeueFront(IReadOnlyList<QueuedOperation> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        lock (_queueGate)
        {
            _queue.InsertRange(0, batch);
        }
    }

    private async Task<DeliveryResult> DeliverAsync(IReadOnlyList<QueuedOperation> batch, CancellationToken cancellationToken)
    {
        var retries = 0;
        while (true)
        {
            var result = batch[0] is IdentifyOperation identify
                ? await _transport.SendIdentifyAsync(identify, cancellationToken).ConfigureAwait(false)
                : await _transport.SendBatchAsync(batch.Cast<TrackOperation>().ToList(), cancellationToken).ConfigureAwait(false);

            if (result != DeliveryResult.Retry)
            {
                return result;
            }

            retries++;
            if (retries > _maxRetries)
            {
                return DeliveryResult.RetryExhausted;
            }

            var delay = _retryDelay(retries);
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private string NextMessageId()
    {
        var generated = _messageIdGenerator();
        return string.IsNullOrWhiteSpace(generated)
            ? Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture)
            : generated;
    }

    private static IReadOnlyDictionary<string, object?>? SnapshotDictionary(IReadOnlyDictionary<string, object?>? values)
    {
        if (values == null || values.Count == 0)
        {
            return null;
        }

        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var item in values)
        {
            snapshot[item.Key] = item.Value;
        }

        return snapshot;
    }

    private static IReadOnlyList<WhisperrChannel>? BuildChannels(IdentifyOptions options)
    {
        if (options.Channels != null && options.Channels.Count > 0)
        {
            return SnapshotChannels(options.Channels);
        }

        var channels = new List<WhisperrChannel>();
        if (!string.IsNullOrWhiteSpace(options.Email))
        {
            channels.Add(WhisperrChannel.Email(options.Email!));
        }

        if (!string.IsNullOrWhiteSpace(options.Phone))
        {
            channels.Add(WhisperrChannel.Sms(options.Phone!));
        }

        if (!string.IsNullOrWhiteSpace(options.PushToken))
        {
            channels.Add(WhisperrChannel.Push(options.PushToken!));
        }

        return channels.Count == 0 ? null : channels;
    }

    private static IReadOnlyList<WhisperrChannel> SnapshotChannels(IReadOnlyList<WhisperrChannel> channels)
    {
        var snapshot = new List<WhisperrChannel>(channels.Count);
        foreach (var channel in channels)
        {
            snapshot.Add(new WhisperrChannel(channel.Type, channel.Address, channel.OptedIn, channel.Verified));
        }

        return snapshot;
    }

    private bool ValidateChannels(IReadOnlyList<WhisperrChannel>? channels)
    {
        if (channels == null)
        {
            return true;
        }

        foreach (var channel in channels)
        {
            if (!IsDefinedChannelType(channel.Type))
            {
                Emit(WhisperrError.Dropped("invalid channel type - expected email, sms, or push"));
                Warn("invalid channel type - identify was not queued");
                return false;
            }

            if (string.IsNullOrWhiteSpace(channel.Address))
            {
                Emit(WhisperrError.Dropped("invalid channel address - address is required"));
                Warn("invalid channel address - identify was not queued");
                return false;
            }
        }

        return true;
    }

    private static bool IsDefinedChannelType(WhisperrChannelType type)
    {
        return type == WhisperrChannelType.Email ||
            type == WhisperrChannelType.Sms ||
            type == WhisperrChannelType.Push;
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    private void Emit(WhisperrError error)
    {
        if (_onError == null)
        {
            return;
        }

        try
        {
            _onError(error);
        }
        catch
        {
            // Host callbacks should not destabilize event capture.
        }
    }

    private void Warn(string message)
    {
        if (!_debug)
        {
            return;
        }

        try
        {
            (_logger ?? Console.Error.WriteLine)("[whisperr] " + message);
        }
        catch
        {
            // Logging must stay best-effort.
        }
    }

    private void DisposeResources()
    {
        if (_disposed)
        {
            return;
        }

        _flushTimer?.Dispose();
        _transport.Dispose();
        _drainLock.Dispose();
        _disposed = true;
    }
}
