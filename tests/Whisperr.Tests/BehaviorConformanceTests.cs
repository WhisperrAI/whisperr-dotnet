using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Whisperr.Tests;

public sealed class BehaviorConformanceTests
{
    [Fact]
    public async Task HonorsSharedDeliverySemantics()
    {
        using var spec = JsonDocument.Parse(await SpecLoader.LoadBehaviorSpecAsync());
        var cases = spec.RootElement.GetProperty("cases").EnumerateArray().ToList();
        Assert.NotEmpty(cases);

        foreach (var behaviorCase in cases)
        {
            var name = behaviorCase.GetProperty("name").GetString() ?? "(unnamed)";
            var scenario = behaviorCase.GetProperty("scenario");
            var expect = behaviorCase.GetProperty("expect");
            var handler = new TestHttpMessageHandler
            {
                StatusCode = behaviorCase.GetProperty("firstResponse").GetProperty("status").GetInt32()
            };
            var errors = new List<WhisperrError>();

            using var http = new HttpClient(handler);
            using var client = new WhisperrClient(new WhisperrOptions
            {
                ApiKey = "wrk_test",
                FlushInterval = TimeSpan.Zero,
                MaxRetries = ReadMaxRetries(behaviorCase),
                RetryDelay = _ => TimeSpan.Zero,
                HttpClient = http,
                OnError = errors.Add
            });

            client.Track(
                scenario.GetProperty("externalUserId").GetString() ?? string.Empty,
                scenario.GetProperty("eventType").GetString() ?? string.Empty,
                ReadObject(scenario, "properties"));

            await client.FlushAsync();

            Assert.Contains(errors, error => error.Code == expect.GetProperty("errorType").GetString());
            var afterFirst = BatchCalls(handler.Requests);
            Assert.Single(afterFirst);
            Assert.Equal(
                expect.GetProperty("retainedAfterFirstFlush").GetBoolean() ? 1 : 0,
                client.PendingCount);

            handler.StatusCode = behaviorCase.GetProperty("recoveryResponse").GetProperty("status").GetInt32();
            await client.FlushAsync();

            var afterRecovery = BatchCalls(handler.Requests);
            var retried = afterRecovery.Count > afterFirst.Count;
            Assert.Equal(expect.GetProperty("retriesAfterRecovery").GetBoolean(), retried);

            var deliveredAfterRecovery =
                retried && ReadFirstEventType(afterRecovery[afterRecovery.Count - 1]) == scenario.GetProperty("eventType").GetString();
            Assert.Equal(expect.GetProperty("deliveredAfterRecovery").GetBoolean(), deliveredAfterRecovery);

            if (expect.TryGetProperty("stableMessageIdOnRetry", out var stableMessageId) && stableMessageId.GetBoolean())
            {
                Assert.Equal(ReadFirstMessageId(afterRecovery[0]), ReadFirstMessageId(afterRecovery[1]));
            }
        }
    }

    private static int ReadMaxRetries(JsonElement behaviorCase)
    {
        return behaviorCase.TryGetProperty("clientOptions", out var options) &&
            options.TryGetProperty("maxRetries", out var maxRetries)
            ? maxRetries.GetInt32()
            : 0;
    }

    private static IReadOnlyDictionary<string, object?>? ReadObject(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return obj.EnumerateObject().ToDictionary(
            prop => prop.Name,
            prop => (object?)prop.Value.Clone(),
            StringComparer.Ordinal);
    }

    private static List<CapturedRequest> BatchCalls(IEnumerable<CapturedRequest> requests)
    {
        return requests.Where(request => request.Path.EndsWith("/v1/events/batch", StringComparison.Ordinal)).ToList();
    }

    private static string? ReadFirstEventType(CapturedRequest request)
    {
        using var json = JsonDocument.Parse(request.Body);
        return json.RootElement.GetProperty("events")[0].GetProperty("event_type").GetString();
    }

    private static string? ReadFirstMessageId(CapturedRequest request)
    {
        using var json = JsonDocument.Parse(request.Body);
        return json.RootElement.GetProperty("events")[0].GetProperty("context").GetProperty("$message_id").GetString();
    }
}
