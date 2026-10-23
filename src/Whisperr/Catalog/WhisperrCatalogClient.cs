using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Whisperr;

/// <summary>
/// Catalog push client (whisperr-go catalog v2 contract, section 6):
/// <see cref="WhisperrClient.Catalog"/>, or standalone.
/// </summary>
/// <remarks>
/// Calls are request/response, not queued like <see cref="WhisperrClient.Track"/>:
/// each task completes once Whisperr has answered. Upserts are chunked at 500
/// items (and 2 MiB), deletes at 1000. 429 and 5xx answers and network errors
/// are retried with backoff, honoring Retry-After. Every retry resends the same
/// body, and the server orders writes by <c>updated_at</c> / <c>deleted_at</c>, so
/// a retried upsert or delete can never apply twice or overwrite a newer write.
/// </remarks>
public sealed class WhisperrCatalogClient : IDisposable
{
    /// <summary>Items per upsert request.</summary>
    public const int UpsertBatch = 500;

    /// <summary>Items per delete request.</summary>
    public const int DeleteBatch = 1000;

    /// <summary>Largest request body Whisperr accepts.</summary>
    public const int MaxBodyBytes = 2 * 1024 * 1024;

    private const int Envelope = 16;
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
    private static readonly Regex SnapshotId = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);
    private static readonly byte[] ItemsOpen = Encoding.UTF8.GetBytes("{\"items\":[");
    private static readonly byte[] ItemsClose = Encoding.UTF8.GetBytes("]}");

    internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Random Jitter = new Random();
    private static readonly object JitterGate = new object();

    private readonly string _apiKey;
    private readonly Uri _baseUrl;
    private readonly int _maxRetries;
    private readonly TimeSpan _timeout;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<int, TimeSpan> _retryDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly bool _disabled;

    /// <summary>Creates a catalog client for a server key.</summary>
    public WhisperrCatalogClient(string apiKey)
        : this(new WhisperrCatalogOptions { ApiKey = apiKey })
    {
    }

    /// <summary>Creates a catalog client with explicit options.</summary>
    public WhisperrCatalogClient(WhisperrCatalogOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.BaseUrl == null || !options.BaseUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("BaseUrl must be an absolute URI.", nameof(options));
        }

        _apiKey = (options.ApiKey ?? string.Empty).Trim();
        _baseUrl = options.BaseUrl;
        _maxRetries = Math.Max(0, options.MaxRetries);
        _timeout = options.RequestTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : options.RequestTimeout;
        _httpClient = options.HttpClient ?? new HttpClient();
        _ownsHttpClient = options.HttpClient == null;
        _clock = options.Clock ?? (() => DateTimeOffset.UtcNow);
        _retryDelay = options.RetryDelay ?? DefaultRetryDelay;
        _delay = options.Delay ?? ((wait, token) => Task.Delay(wait, token));
        _disabled = options.Disabled;
    }

    /// <summary>
    /// Create or update items. Rejected items are listed in <see cref="CatalogUpsertResult.Rejected"/>, not thrown.
    /// </summary>
    public async Task<CatalogUpsertResult> UpsertAsync(IEnumerable<CatalogItem> items, CancellationToken cancellationToken = default)
    {
        CheckKey();
        if (items == null)
        {
            throw new WhisperrConfigurationException("UpsertAsync takes a list of items");
        }

        var result = new CatalogUpsertResult();
        var chunks = new List<List<Encoded>>();
        var chunk = new List<Encoded>();
        var size = Envelope;
        var index = 0;
        foreach (var item in items)
        {
            if (item == null)
            {
                throw new WhisperrConfigurationException("a catalog item is null");
            }

            var encoded = new Encoded(index++, item.SourceItemId ?? string.Empty, JsonSerializer.SerializeToUtf8Bytes(item, Json));
            var n = encoded.Json.Length + 1;
            if (n + Envelope > MaxBodyBytes)
            {
                result.Rejected.Add(TooLarge(encoded, "the item is larger than the 2 MiB request limit"));
                continue;
            }

            if (chunk.Count == UpsertBatch || size + n > MaxBodyBytes)
            {
                chunks.Add(chunk);
                chunk = new List<Encoded>();
                size = Envelope;
            }

            chunk.Add(encoded);
            size += n;
        }

        if (chunk.Count > 0)
        {
            chunks.Add(chunk);
        }

        foreach (var part in chunks)
        {
            await SendUpsertAsync(part, result, cancellationToken).ConfigureAwait(false);
        }

        Sort(result.Rejected);
        return result;
    }

    /// <summary>
    /// Delete items by id. All get <paramref name="deletedAt"/> (default: now), once per call. Chunks at 1000.
    /// </summary>
    public Task<CatalogDeleteResult> DeleteAsync(IEnumerable<string> ids, DateTimeOffset? deletedAt = null, CancellationToken cancellationToken = default)
    {
        CheckKey();
        if (ids == null)
        {
            throw new WhisperrConfigurationException("DeleteAsync takes a list of ids");
        }

        var at = deletedAt ?? _clock();
        return DeleteCoreAsync(ids.Select(id => new CatalogDeletion { SourceItemId = id ?? string.Empty, DeletedAt = at }), at, cancellationToken);
    }

    /// <summary>
    /// Delete items, each with its own <see cref="CatalogDeletion.DeletedAt"/> (default: now, once per call). Chunks at 1000.
    /// </summary>
    public Task<CatalogDeleteResult> DeleteAsync(IEnumerable<CatalogDeletion> items, CancellationToken cancellationToken = default)
    {
        CheckKey();
        if (items == null)
        {
            throw new WhisperrConfigurationException("DeleteAsync takes a list of items");
        }

        return DeleteCoreAsync(items, _clock(), cancellationToken);
    }

    /// <summary>Start a full sync: upsert every item, then call <see cref="CompleteSnapshotAsync"/>.</summary>
    public async Task<CatalogSnapshot> BeginSnapshotAsync(CancellationToken cancellationToken = default)
    {
        CheckKey();
        var body = await RequestAsync(HttpMethod.Post, "/v1/catalog/snapshots", Encoding.UTF8.GetBytes("{}"), cancellationToken).ConfigureAwait(false);
        return Deserialize<CatalogSnapshot>(body);
    }

    /// <summary>End a full sync: items not upserted since <see cref="BeginSnapshotAsync"/> are removed.</summary>
    public async Task<CatalogSnapshotResult> CompleteSnapshotAsync(string snapshotId, long itemCount, CancellationToken cancellationToken = default)
    {
        CheckKey();
        if (snapshotId == null || !SnapshotId.IsMatch(snapshotId))
        {
            throw new WhisperrConfigurationException("snapshotId must be the id BeginSnapshotAsync returned");
        }

        CheckCount(itemCount);
        var body = await RequestAsync(HttpMethod.Post, "/v1/catalog/snapshots/" + snapshotId + "/complete", CountBody(itemCount), cancellationToken).ConfigureAwait(false);
        return Deserialize<CatalogSnapshotResult>(body);
    }

    /// <summary>Tell Whisperr the catalog is still current. Send every 30 minutes with your live item count.</summary>
    public async Task<CatalogHeartbeatResult> HeartbeatAsync(long itemCount, CancellationToken cancellationToken = default)
    {
        CheckKey();
        CheckCount(itemCount);
        var body = await RequestAsync(HttpMethod.Post, "/v1/catalog/heartbeat", CountBody(itemCount), cancellationToken).ConfigureAwait(false);
        return Deserialize<CatalogHeartbeatResult>(body);
    }

    /// <summary>The health of your pushed catalog.</summary>
    public async Task<CatalogSourceHealth> StatusAsync(CancellationToken cancellationToken = default)
    {
        CheckKey();
        var body = await RequestAsync(HttpMethod.Get, "/v1/catalog/status", null, cancellationToken).ConfigureAwait(false);
        return Deserialize<CatalogSourceHealth>(body);
    }

    /// <summary>Releases the HttpClient the catalog client owns.</summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    // ---- internals ----

    private async Task SendUpsertAsync(List<Encoded> part, CatalogUpsertResult result, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await RequestAsync(HttpMethod.Post, "/v1/catalog/items/batch", ItemsBody(part), cancellationToken).ConfigureAwait(false);
        }
        catch (WhisperrCatalogException e) when (e.Status == 413)
        {
            // The server measures the body; split and resend rather than fail.
            if (part.Count > 1)
            {
                var half = (part.Count + 1) / 2;
                await SendUpsertAsync(part.Take(half).ToList(), result, cancellationToken).ConfigureAwait(false);
                await SendUpsertAsync(part.Skip(half).ToList(), result, cancellationToken).ConfigureAwait(false);
                return;
            }

            result.Rejected.Add(TooLarge(part[0], e.Message));
            return;
        }
        catch (WhisperrCatalogException e)
        {
            Sort(result.Rejected);
            throw e.WithPartial(result);
        }

        var res = Deserialize<CatalogUpsertResult>(body);
        result.Accepted += res.Accepted;
        result.Applied += res.Applied;
        result.Unchanged += res.Unchanged;
        result.IgnoredOlder += res.IgnoredOlder;
        result.Rejected.AddRange(MapRejections(res.Rejected, part));
    }

    private async Task<CatalogDeleteResult> DeleteCoreAsync(IEnumerable<CatalogDeletion> items, DateTimeOffset fallback, CancellationToken cancellationToken)
    {
        var encoded = new List<Encoded>();
        var index = 0;
        foreach (var item in items)
        {
            if (item == null)
            {
                throw new WhisperrConfigurationException("a deletion is null");
            }

            var wire = new CatalogDeletion { SourceItemId = item.SourceItemId ?? string.Empty, DeletedAt = item.DeletedAt ?? fallback };
            encoded.Add(new Encoded(index++, wire.SourceItemId, JsonSerializer.SerializeToUtf8Bytes(wire, Json)));
        }

        var result = new CatalogDeleteResult();
        for (var start = 0; start < encoded.Count; start += DeleteBatch)
        {
            var part = encoded.Skip(start).Take(DeleteBatch).ToList();
            string body;
            try
            {
                body = await RequestAsync(HttpMethod.Post, "/v1/catalog/items/delete", ItemsBody(part), cancellationToken).ConfigureAwait(false);
            }
            catch (WhisperrCatalogException e)
            {
                Sort(result.Rejected);
                throw e.WithPartial(result);
            }

            var res = Deserialize<CatalogDeleteResult>(body);
            result.Deleted += res.Deleted;
            result.IgnoredOlder += res.IgnoredOlder;
            result.NotFound += res.NotFound;
            result.Rejected.AddRange(MapRejections(res.Rejected, part));
        }

        Sort(result.Rejected);
        return result;
    }

    private void CheckKey()
    {
        if (_disabled)
        {
            throw new WhisperrConfigurationException("the Whisperr client is disabled; catalog calls are not sent");
        }

        if (_apiKey.Length == 0)
        {
            throw new WhisperrConfigurationException("catalog calls need a server key (wrk_...)");
        }

        if (_apiKey.StartsWith("wpk_", StringComparison.Ordinal))
        {
            throw new WhisperrConfigurationException(
                "catalog push needs a server key (wrk_...); a publishable key (wpk_...) ships in apps and browsers and is refused");
        }
    }

    private async Task<string> RequestAsync(HttpMethod method, string path, byte[]? body, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            var status = 0;
            var code = "network_error";
            var message = "network error or timeout";
            TimeSpan? retryAfter = null;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(_timeout);
                try
                {
                    using var request = new HttpRequestMessage(method, new Uri(_baseUrl.ToString().TrimEnd('/') + path));
                    request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
                    request.Headers.TryAddWithoutValidation("Accept", "application/json");
                    request.Headers.TryAddWithoutValidation("User-Agent", SdkInfo.Name + "/" + SdkInfo.Version);
                    if (body != null)
                    {
                        request.Content = new ByteArrayContent(body);
                        request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
                    }

                    using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
                    status = (int)response.StatusCode;
#if NET8_0_OR_GREATER
                    var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
#else
                    var text = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
                    if (status >= 200 && status <= 299)
                    {
                        if (!IsJsonObject(text))
                        {
                            throw new WhisperrCatalogException($"unreadable response from {path}", status, "invalid_response", false);
                        }

                        return text;
                    }

                    (code, message) = ErrorOf(text, status);
                    if (status == 429 || status == 503)
                    {
                        retryAfter = RetryAfter(response);
                    }

                    if (!(status == 429 || status >= 500))
                    {
                        throw new WhisperrCatalogException($"{path} refused ({status} {code}): {message}", status, code, false);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // timeout: retried below
                }
                catch (HttpRequestException)
                {
                    // network error: retried below
                }
            }

            if (attempt >= _maxRetries)
            {
                var shown = status == 0 ? "network" : status.ToString(System.Globalization.CultureInfo.InvariantCulture);
                throw new WhisperrCatalogException($"{path} failed after {attempt + 1} attempts ({shown} {code}): {message}", status, code, true);
            }

            attempt++;
            await _delay(retryAfter ?? _retryDelay(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header == null)
        {
            return null;
        }

        TimeSpan wait;
        if (header.Delta.HasValue)
        {
            wait = header.Delta.Value;
        }
        else if (header.Date.HasValue)
        {
            wait = header.Date.Value - DateTimeOffset.UtcNow;
        }
        else
        {
            return null;
        }

        if (wait < TimeSpan.Zero)
        {
            wait = TimeSpan.Zero;
        }

        return wait > MaxRetryAfter ? MaxRetryAfter : wait;
    }

    private static (string Code, string Message) ErrorOf(string text, int status)
    {
        var fallback = "http_" + status.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (fallback, string.Empty);
            }

            var err = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object ? e : root;
            var code = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String && c.GetString()!.Length > 0 ? c.GetString()! : fallback;
            var message = err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : string.Empty;
            return (code, message);
        }
        catch (JsonException)
        {
            return (fallback, string.Empty);
        }
    }

    private static bool IsJsonObject(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static T Deserialize<T>(string text)
        where T : class, new()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, Json) ?? new T();
        }
        catch (Exception e) when (e is JsonException || e is FormatException || e is NotSupportedException)
        {
            throw new WhisperrCatalogException("unreadable response: " + e.Message, 200, "invalid_response", false);
        }
    }

    private static IEnumerable<CatalogRejection> MapRejections(List<CatalogRejection>? rejected, List<Encoded> part)
    {
        if (rejected == null)
        {
            yield break;
        }

        foreach (var r in rejected)
        {
            var known = r.Index >= 0 && r.Index < part.Count;
            yield return new CatalogRejection
            {
                Index = known ? part[r.Index].Index : -1,
                SourceItemId = !string.IsNullOrEmpty(r.SourceItemId) ? r.SourceItemId : (known ? part[r.Index].Id : string.Empty),
                Code = r.Code ?? string.Empty,
                Message = r.Message ?? string.Empty,
            };
        }
    }

    private static byte[] ItemsBody(List<Encoded> part)
    {
        var size = ItemsOpen.Length + ItemsClose.Length + part.Sum(e => e.Json.Length) + Math.Max(0, part.Count - 1);
        var buffer = new byte[size];
        var offset = 0;
        Buffer.BlockCopy(ItemsOpen, 0, buffer, offset, ItemsOpen.Length);
        offset += ItemsOpen.Length;
        for (var i = 0; i < part.Count; i++)
        {
            if (i > 0)
            {
                buffer[offset++] = (byte)',';
            }

            Buffer.BlockCopy(part[i].Json, 0, buffer, offset, part[i].Json.Length);
            offset += part[i].Json.Length;
        }

        Buffer.BlockCopy(ItemsClose, 0, buffer, offset, ItemsClose.Length);
        return buffer;
    }

    private static byte[] CountBody(long itemCount)
    {
        return Encoding.UTF8.GetBytes("{\"item_count\":" + itemCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
    }

    private static CatalogRejection TooLarge(Encoded e, string message)
    {
        return new CatalogRejection { Index = e.Index, SourceItemId = e.Id, Code = "item_too_large", Message = message };
    }

    private static void Sort(List<CatalogRejection> rejected)
    {
        var sorted = rejected.OrderBy(r => r.Index).ToList();
        rejected.Clear();
        rejected.AddRange(sorted);
    }

    private static void CheckCount(long itemCount)
    {
        if (itemCount < 0)
        {
            throw new WhisperrConfigurationException("itemCount must be at least 0");
        }
    }

    private static TimeSpan DefaultRetryDelay(int attempt)
    {
        var baseMs = Math.Min(30_000, 500 * Math.Pow(2, Math.Max(1, attempt)));
        int jitterMs;
        lock (JitterGate)
        {
            jitterMs = Jitter.Next(0, 251);
        }

        return TimeSpan.FromMilliseconds(baseMs + jitterMs);
    }

    private sealed class Encoded
    {
        public Encoded(int index, string id, byte[] json)
        {
            Index = index;
            Id = id;
            Json = json;
        }

        public int Index { get; }

        public string Id { get; }

        public byte[] Json { get; }
    }
}
