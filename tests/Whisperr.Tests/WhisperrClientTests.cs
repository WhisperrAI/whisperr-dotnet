using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Whisperr.Tests;

public sealed class WhisperrClientTests
{
    [Fact]
    public void TrackRejectsInvalidEventTypesBeforeQueueing()
    {
        var errors = new List<WhisperrError>();
        using var client = new WhisperrClient(new WhisperrOptions
        {
            ApiKey = "wrk_test",
            FlushInterval = TimeSpan.Zero,
            OnError = errors.Add
        });

        var queued = client.Track("user_1", "Payment Failed");

        Assert.False(queued);
        Assert.Equal(0, client.PendingCount);
        Assert.Contains(errors, error => error.Code == "dropped");
    }

    [Fact]
    public async Task IdentifyShortcutChannelsSerializeToCanonicalWireFields()
    {
        var handler = new TestHttpMessageHandler();
        using var http = new HttpClient(handler);
        using var client = new WhisperrClient(new WhisperrOptions
        {
            ApiKey = "wrk_test",
            FlushInterval = TimeSpan.Zero,
            HttpClient = http
        });

        client.Identify("user_1", new IdentifyOptions
        {
            Email = "ada@example.com",
            Phone = "+15551234567",
            PushToken = "push-token"
        });

        await client.FlushAsync();

        var request = Assert.Single(handler.Requests, item => item.Path.EndsWith("/v1/identify", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(request.Body);
        var channels = json.RootElement.GetProperty("channels");

        Assert.Equal("email", channels[0].GetProperty("channel").GetString());
        Assert.Equal("sms", channels[1].GetProperty("channel").GetString());
        Assert.Equal("push", channels[2].GetProperty("channel").GetString());
        Assert.True(channels[0].GetProperty("opted_in").GetBoolean());
    }

    [Fact]
    public async Task IdentifyDropsOnlyTheInvalidChannelAndStillSendsTraits()
    {
        var handler = new TestHttpMessageHandler();
        var errors = new List<WhisperrError>();
        using var http = new HttpClient(handler);
        using var client = new WhisperrClient(new WhisperrOptions
        {
            ApiKey = "wrk_test",
            FlushInterval = TimeSpan.Zero,
            HttpClient = http,
            OnError = errors.Add
        });

        var queued = client.Identify("user_1", new IdentifyOptions
        {
            Traits = new Dictionary<string, object?> { ["plan"] = "pro" },
            Channels = new[]
            {
                new WhisperrChannel((WhisperrChannelType)999, "ada@example.com"),
                WhisperrChannel.Sms("+15551234567")
            }
        });

        Assert.True(queued);
        await client.FlushAsync();

        var request = Assert.Single(handler.Requests, item => item.Path.EndsWith("/v1/identify", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal("pro", json.RootElement.GetProperty("traits").GetProperty("plan").GetString());

        var channels = json.RootElement.GetProperty("channels");
        Assert.Equal(1, channels.GetArrayLength());
        Assert.Equal("sms", channels[0].GetProperty("channel").GetString());
        Assert.Contains(errors, error => error.Code == "dropped");
    }

    [Fact]
    public async Task IdentifyIgnoresInvalidPreferredChannelButStillSends()
    {
        var handler = new TestHttpMessageHandler();
        using var http = new HttpClient(handler);
        using var client = new WhisperrClient(new WhisperrOptions
        {
            ApiKey = "wrk_test",
            FlushInterval = TimeSpan.Zero,
            HttpClient = http
        });

        var queued = client.Identify("user_1", new IdentifyOptions
        {
            Traits = new Dictionary<string, object?> { ["plan"] = "pro" },
            PreferredChannel = (WhisperrChannelType)999
        });

        Assert.True(queued);
        await client.FlushAsync();

        var request = Assert.Single(handler.Requests, item => item.Path.EndsWith("/v1/identify", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal("pro", json.RootElement.GetProperty("traits").GetProperty("plan").GetString());
        Assert.False(json.RootElement.TryGetProperty("preferred_channel", out _));
    }

    [Fact]
    public async Task TrackSnapshotsPropertiesBeforeFlush()
    {
        var handler = new TestHttpMessageHandler();
        using var http = new HttpClient(handler);
        using var client = new WhisperrClient(new WhisperrOptions
        {
            ApiKey = "wrk_test",
            FlushInterval = TimeSpan.Zero,
            HttpClient = http
        });
        var properties = new Dictionary<string, object?>
        {
            ["amount_cents"] = 4900
        };

        client.Track("user_1", "payment_failed", properties);
        properties["amount_cents"] = 9900;

        await client.FlushAsync();

        var request = Assert.Single(handler.Requests, item => item.Path.EndsWith("/v1/events/batch", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal(4900, json.RootElement.GetProperty("events")[0].GetProperty("properties").GetProperty("amount_cents").GetInt32());
    }

    [Fact]
    public async Task SerializationFailureDropsBatchInsteadOfRetainingForever()
    {
        var handler = new TestHttpMessageHandler();
        var errors = new List<WhisperrError>();
        using var http = new HttpClient(handler);
        using var client = new WhisperrClient(new WhisperrOptions
        {
            ApiKey = "wrk_test",
            FlushInterval = TimeSpan.Zero,
            HttpClient = http,
            OnError = errors.Add
        });
        var properties = new Dictionary<string, object?>();
        properties["self"] = properties;

        client.Track("user_1", "bad_payload", properties);
        await client.FlushAsync();

        Assert.Equal(0, client.PendingCount);
        Assert.Empty(handler.Requests);
        Assert.Contains(errors, error => error.Code == "dropped");
    }

    [Fact]
    public void QueueOverflowDropsOldestOperations()
    {
        var errors = new List<WhisperrError>();
        using var client = new WhisperrClient(new WhisperrOptions
        {
            ApiKey = "wrk_test",
            FlushInterval = TimeSpan.Zero,
            FlushAt = 100,
            MaxQueueSize = 1,
            OnError = errors.Add
        });

        client.Track("user_1", "first_event");
        client.Track("user_1", "second_event");

        Assert.Equal(1, client.PendingCount);
        Assert.Contains(errors, error => error.Code == "dropped");
    }

    [Fact]
    public async Task CancelledFlushRetainsInFlightBatch()
    {
        var handler = new TestHttpMessageHandler
        {
            DelayBeforeResponse = TimeSpan.FromSeconds(5)
        };
        using var http = new HttpClient(handler);
        using var client = new WhisperrClient(new WhisperrOptions
        {
            ApiKey = "wrk_test",
            FlushInterval = TimeSpan.Zero,
            HttpClient = http
        });

        client.Track("user_1", "feature_used");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<TaskCanceledException>(() => client.FlushAsync(cancellation.Token));
        Assert.Equal(1, client.PendingCount);

        handler.DelayBeforeResponse = TimeSpan.Zero;
        await client.FlushAsync();

        Assert.Equal(0, client.PendingCount);
    }
}
