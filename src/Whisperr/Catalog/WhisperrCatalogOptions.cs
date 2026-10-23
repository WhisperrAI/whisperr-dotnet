using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Whisperr;

/// <summary>
/// Options for <see cref="WhisperrCatalogClient"/>.
/// </summary>
public sealed class WhisperrCatalogOptions
{
    /// <summary>A server key (wrk_...). A publishable key (wpk_...) is refused.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Whisperr API base URL.</summary>
    public Uri BaseUrl { get; set; } = new Uri(WhisperrOptions.DefaultBaseUrl);

    /// <summary>Retries after the first attempt on 429, 5xx and network errors. Default: 5.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Per-request timeout. Default: 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Optional HttpClient. When omitted, the client owns a private HttpClient.</summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>Optional clock override, used for the default <c>deleted_at</c>.</summary>
    public Func<DateTimeOffset>? Clock { get; set; }

    /// <summary>Optional backoff override when the server sends no Retry-After. The attempt argument starts at 1.</summary>
    public Func<int, TimeSpan>? RetryDelay { get; set; }

    /// <summary>Optional wait override, for tests. Defaults to Task.Delay.</summary>
    public Func<TimeSpan, CancellationToken, Task>? Delay { get; set; }

    internal bool Disabled { get; set; }
}
