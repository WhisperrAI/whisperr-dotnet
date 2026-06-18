using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Whisperr;

internal sealed class WhisperrTransport : IDisposable
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyProperties =
        new Dictionary<string, object?>();

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Uri _baseUrl;
    private readonly string _apiKey;
    private readonly TimeSpan _timeout;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Action<string> _warn;

    public WhisperrTransport(Uri baseUrl, string apiKey, TimeSpan timeout, HttpClient? httpClient, Action<string> warn)
    {
        _baseUrl = baseUrl;
        _apiKey = apiKey;
        _timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : timeout;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient == null;
        _warn = warn;
    }

    public Task<DeliveryResult> SendBatchAsync(IReadOnlyList<TrackOperation> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return Task.FromResult(DeliveryResult.Ok);
        }

        return PostAsync("/v1/events/batch", BuildBatch(events), cancellationToken);
    }

    public Task<DeliveryResult> SendIdentifyAsync(IdentifyOperation op, CancellationToken cancellationToken)
    {
        return PostAsync("/v1/identify", BuildIdentify(op), cancellationToken);
    }

    public string SerializeBatch(IReadOnlyList<TrackOperation> events)
    {
        return JsonSerializer.Serialize(BuildBatch(events), JsonOptions);
    }

    public string SerializeIdentify(IdentifyOperation op)
    {
        return JsonSerializer.Serialize(BuildIdentify(op), JsonOptions);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<DeliveryResult> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(path))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);

            using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;

            if (status >= 200 && status <= 299)
            {
                return DeliveryResult.Ok;
            }

            if (status == 401 || status == 403)
            {
                _warn($"auth rejected ({status}) - check your Whisperr API key");
                return DeliveryResult.Auth;
            }

            if (status == 429 || status >= 500)
            {
                return DeliveryResult.Retry;
            }

            _warn($"request to {path} dropped ({status})");
            return DeliveryResult.Drop;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DeliveryResult.Retry;
        }
        catch (HttpRequestException)
        {
            return DeliveryResult.Retry;
        }
        catch (JsonException ex)
        {
            _warn("request payload could not be serialized - dropping batch: " + ex.Message);
            return DeliveryResult.Drop;
        }
        catch (NotSupportedException ex)
        {
            _warn("request payload could not be serialized - dropping batch: " + ex.Message);
            return DeliveryResult.Drop;
        }
    }

    private Uri BuildUri(string path)
    {
        return new Uri(_baseUrl.ToString().TrimEnd('/') + path);
    }

    private static TrackBatchWireBody BuildBatch(IEnumerable<TrackOperation> events)
    {
        return new TrackBatchWireBody
        {
            Events = events.Select(static op => new TrackWireEvent
            {
                ExternalUserId = op.ExternalUserId,
                EventType = op.EventType,
                OccurredAt = op.OccurredAt,
                Properties = op.Properties ?? EmptyProperties,
                Context = BuildContext(op)
            }).ToList()
        };
    }

    private static IdentifyWireBody BuildIdentify(IdentifyOperation op)
    {
        return new IdentifyWireBody
        {
            ExternalUserId = op.ExternalUserId,
            Traits = op.Traits != null && op.Traits.Count > 0 ? op.Traits : null,
            PreferredChannel = op.PreferredChannel?.ToWireValue(),
            Channels = op.Channels != null && op.Channels.Count > 0
                ? op.Channels.Select(static channel => new IdentifyWireChannel
                {
                    Channel = channel.Type.ToWireValue(),
                    Address = channel.Address,
                    OptedIn = channel.OptedIn ?? true,
                    Verified = channel.Verified
                }).ToList()
                : null
        };
    }

    private static IReadOnlyDictionary<string, object?> BuildContext(TrackOperation op)
    {
        var context = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (op.Context != null)
        {
            foreach (var item in op.Context)
            {
                context[item.Key] = item.Value;
            }
        }

        context["$message_id"] = op.MessageId;
        return context;
    }

    private sealed class TrackBatchWireBody
    {
        [JsonPropertyName("events")]
        public IReadOnlyList<TrackWireEvent> Events { get; set; } = Array.Empty<TrackWireEvent>();
    }

    private sealed class TrackWireEvent
    {
        [JsonPropertyName("external_user_id")]
        public string ExternalUserId { get; set; } = string.Empty;

        [JsonPropertyName("event_type")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("occurred_at")]
        public string OccurredAt { get; set; } = string.Empty;

        [JsonPropertyName("properties")]
        public IReadOnlyDictionary<string, object?> Properties { get; set; } = EmptyProperties;

        [JsonPropertyName("context")]
        public IReadOnlyDictionary<string, object?> Context { get; set; } = new Dictionary<string, object?>();
    }

    private sealed class IdentifyWireBody
    {
        [JsonPropertyName("external_user_id")]
        public string ExternalUserId { get; set; } = string.Empty;

        [JsonPropertyName("traits")]
        public IReadOnlyDictionary<string, object?>? Traits { get; set; }

        [JsonPropertyName("preferred_channel")]
        public string? PreferredChannel { get; set; }

        [JsonPropertyName("channels")]
        public IReadOnlyList<IdentifyWireChannel>? Channels { get; set; }
    }

    private sealed class IdentifyWireChannel
    {
        [JsonPropertyName("channel")]
        public string Channel { get; set; } = string.Empty;

        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [JsonPropertyName("opted_in")]
        public bool OptedIn { get; set; }

        [JsonPropertyName("verified")]
        public bool? Verified { get; set; }
    }
}
