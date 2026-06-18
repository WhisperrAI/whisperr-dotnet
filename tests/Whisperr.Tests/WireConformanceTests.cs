using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Whisperr.Tests;

public sealed class WireConformanceTests
{
    private static readonly Regex Rfc3339UtcMilliseconds = new Regex(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public async Task SerializesEverySharedWireCase()
    {
        using var spec = JsonDocument.Parse(await SpecLoader.LoadWireSpecAsync());
        var cases = spec.RootElement.GetProperty("cases").EnumerateArray().ToList();
        Assert.NotEmpty(cases);

        foreach (var conformanceCase in cases)
        {
            var name = conformanceCase.GetProperty("name").GetString() ?? "(unnamed)";
            var scenario = conformanceCase.GetProperty("scenario");
            var handler = new TestHttpMessageHandler();
            using var http = new HttpClient(handler);
            using var client = new WhisperrClient(new WhisperrOptions
            {
                ApiKey = "wrk_test",
                FlushInterval = TimeSpan.Zero,
                HttpClient = http,
                Clock = BuildClock(scenario)
            });

            ApplyCase(client, conformanceCase);
            await client.FlushAsync();

            var endpoint = conformanceCase.GetProperty("endpoint").GetString();
            var call = handler.Requests.SingleOrDefault(request => request.Path.EndsWith(endpoint ?? string.Empty, StringComparison.Ordinal));
            Assert.NotNull(call);

            using var actual = JsonDocument.Parse(call!.Body);
            if (conformanceCase.GetProperty("op").GetString() == "track")
            {
                var actualEvent = actual.RootElement.GetProperty("events")[0];
                foreach (var expected in conformanceCase.GetProperty("expectedEvent").EnumerateObject())
                {
                    AssertJsonEqual(expected.Value, actualEvent.GetProperty(expected.Name), $"{name}.{expected.Name}");
                }

                if (conformanceCase.TryGetProperty("expectedOccurredAt", out var expectedOccurredAt))
                {
                    Assert.Equal(expectedOccurredAt.GetString(), actualEvent.GetProperty("occurred_at").GetString());
                }

                if (conformanceCase.TryGetProperty("contextMustContain", out var keys))
                {
                    var context = actualEvent.GetProperty("context");
                    foreach (var key in keys.EnumerateArray())
                    {
                        Assert.True(context.TryGetProperty(key.GetString() ?? string.Empty, out _), $"{name}: missing context.{key}");
                    }
                }

                if (conformanceCase.TryGetProperty("occurredAtRfc3339Z", out var rfc) && rfc.GetBoolean())
                {
                    Assert.Matches(Rfc3339UtcMilliseconds, actualEvent.GetProperty("occurred_at").GetString() ?? string.Empty);
                }
            }
            else
            {
                foreach (var expected in conformanceCase.GetProperty("expectedBody").EnumerateObject())
                {
                    AssertJsonEqual(expected.Value, actual.RootElement.GetProperty(expected.Name), $"{name}.{expected.Name}");
                }
            }
        }
    }

    private static void ApplyCase(WhisperrClient client, JsonElement conformanceCase)
    {
        var scenario = conformanceCase.GetProperty("scenario");
        if (conformanceCase.GetProperty("op").GetString() == "track")
        {
            client.Track(
                scenario.GetProperty("externalUserId").GetString() ?? string.Empty,
                scenario.GetProperty("eventType").GetString() ?? string.Empty,
                ReadObject(scenario, "properties"));
            return;
        }

        client.Identify(
            scenario.GetProperty("externalUserId").GetString() ?? string.Empty,
            new IdentifyOptions
            {
                Traits = ReadObject(scenario, "traits"),
                Email = ReadString(scenario, "email"),
                Phone = ReadString(scenario, "phone"),
                PushToken = ReadString(scenario, "pushToken"),
                PreferredChannel = ReadChannelTypeOrNull(scenario, "preferredChannel"),
                Channels = ReadChannels(scenario)
            });
    }

    private static Func<DateTimeOffset> BuildClock(JsonElement scenario)
    {
        if (scenario.TryGetProperty("clockIso", out var clockIso))
        {
            var value = DateTimeOffset.Parse(
                clockIso.GetString() ?? string.Empty,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return () => value;
        }

        return () => DateTimeOffset.UtcNow;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) ? value.GetString() : null;
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

    private static IReadOnlyList<WhisperrChannel>? ReadChannels(JsonElement scenario)
    {
        if (!scenario.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var output = new List<WhisperrChannel>();
        foreach (var channel in channels.EnumerateArray())
        {
            output.Add(new WhisperrChannel(
                ReadChannelType(channel.GetProperty("type").GetString() ?? "email"),
                channel.GetProperty("address").GetString() ?? string.Empty,
                channel.TryGetProperty("optedIn", out var optedIn) ? optedIn.GetBoolean() : null,
                channel.TryGetProperty("verified", out var verified) ? verified.GetBoolean() : null));
        }

        return output;
    }

    private static WhisperrChannelType? ReadChannelTypeOrNull(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value)
            ? ReadChannelType(value.GetString() ?? "email")
            : null;
    }

    private static WhisperrChannelType ReadChannelType(string value)
    {
        return value switch
        {
            "email" => WhisperrChannelType.Email,
            "sms" => WhisperrChannelType.Sms,
            "push" => WhisperrChannelType.Push,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown Whisperr channel type.")
        };
    }

    private static void AssertJsonEqual(JsonElement expected, JsonElement actual, string label)
    {
        var expectedNode = JsonNode.Parse(expected.GetRawText());
        var actualNode = JsonNode.Parse(actual.GetRawText());
        Assert.True(JsonNode.DeepEquals(expectedNode, actualNode), $"{label}: expected {expected.GetRawText()}, got {actual.GetRawText()}");
    }
}
