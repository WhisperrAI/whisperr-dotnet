using System;
using System.Net.Http;

namespace Whisperr;

/// <summary>
/// Runtime and delivery options for <see cref="WhisperrClient"/>.
/// </summary>
public sealed class WhisperrOptions
{
    /// <summary>
    /// Default Whisperr ingestion API base URL.
    /// </summary>
    public const string DefaultBaseUrl = "https://api.whisperr.net";

    private static readonly Random Jitter = new Random();
    private static readonly object JitterGate = new object();

    /// <summary>
    /// App ingestion key (wrk_...). Required unless Disabled is true.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Whisperr ingestion API base URL.
    /// </summary>
    public Uri BaseUrl { get; set; } = new Uri(DefaultBaseUrl);

    /// <summary>
    /// The release version of your app or service (for example 2026.10.1 or a
    /// git SHA). When set, every event carries it as <c>context.app_version</c>.
    /// Not set: the key is not sent.
    /// </summary>
    public string? AppVersion { get; set; }

    /// <summary>
    /// Flush when this many operations are queued. Default: 20.
    /// </summary>
    public int FlushAt { get; set; } = 20;

    /// <summary>
    /// Background flush cadence. Set to TimeSpan.Zero to disable. Default: 10 seconds.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum operations held in memory. Oldest operations are dropped on overflow.
    /// </summary>
    public int MaxQueueSize { get; set; } = 10_000;

    /// <summary>
    /// Maximum track events sent in one /v1/events/batch call. Backend cap is 500.
    /// </summary>
    public int MaxBatchSize { get; set; } = 500;

    /// <summary>
    /// Consecutive transient retries before retaining the batch for a later flush.
    /// </summary>
    public int MaxRetries { get; set; } = 6;

    /// <summary>
    /// Per-request timeout. Timeout is classified as transient retry.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Disable capture and networking.
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// Emit verbose diagnostic warnings to Logger or stderr.
    /// </summary>
    public bool Debug { get; set; }

    /// <summary>
    /// Optional logger used only when Debug is true.
    /// </summary>
    public Action<string>? Logger { get; set; }

    /// <summary>
    /// Called when delivery fails, auth is rejected, or queued events are dropped.
    /// </summary>
    public Action<WhisperrError>? OnError { get; set; }

    /// <summary>
    /// Catalog call retries after the first attempt on 429, 5xx and network errors. Default: 5.
    /// </summary>
    public int CatalogMaxRetries { get; set; } = 5;

    /// <summary>
    /// Per-request timeout for catalog calls. Default: 30 seconds.
    /// </summary>
    public TimeSpan CatalogRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional HttpClient. When omitted, the SDK owns a private HttpClient.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Optional clock override for deterministic tests.
    /// </summary>
    public Func<DateTimeOffset>? Clock { get; set; }

    /// <summary>
    /// Optional id generator override for deterministic tests.
    /// </summary>
    public Func<string>? MessageIdGenerator { get; set; }

    /// <summary>
    /// Optional retry delay override. The attempt argument starts at 1.
    /// </summary>
    public Func<int, TimeSpan>? RetryDelay { get; set; }

    /// <summary>
    /// Default exponential retry delay with small jitter.
    /// </summary>
    public static TimeSpan DefaultRetryDelay(int attempt)
    {
        var boundedAttempt = Math.Max(1, attempt);
        var baseMs = Math.Min(30_000, 1_000 * Math.Pow(2, boundedAttempt));
        int jitterMs;
        lock (JitterGate)
        {
            jitterMs = Jitter.Next(0, 251);
        }

        return TimeSpan.FromMilliseconds(baseMs + jitterMs);
    }
}
