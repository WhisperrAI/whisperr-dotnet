using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Whisperr.Tests;

/// <summary>
/// Contract v2 flows (whisperr-spec conformance/v2.json). Runs every section
/// whose appliesTo names "server".
/// </summary>
public sealed class V2ConformanceTests
{
    private static readonly string[] GovernedContextKeys =
    {
        "app_version", "app_build", "platform", "sdk_name", "sdk_version", "locale"
    };

    [Fact]
    public async Task RunsEveryServerSection()
    {
        var spec = JsonNode.Parse(await SpecLoader.LoadV2SpecAsync())!;
        var sections = spec["sections"]!.AsObject()
            .Where(section => section.Value!["appliesTo"]!.AsArray().Any(kind => kind!.GetValue<string>() == "server"))
            .ToList();
        Assert.NotEmpty(sections);

        foreach (var section in sections)
        {
            foreach (var conformanceCase in section.Value!["cases"]!.AsArray())
            {
                var label = section.Key + "/" + conformanceCase!["name"]!.GetValue<string>();
                var handler = new TestHttpMessageHandler();
                using var http = new HttpClient(handler);
                using var client = new WhisperrClient(new WhisperrOptions
                {
                    ApiKey = "wrk_test",
                    FlushInterval = TimeSpan.Zero,
                    HttpClient = http,
                    AppVersion = conformanceCase["config"]?["release"]?.GetValue<string>()
                });

                foreach (var step in conformanceCase["steps"]!.AsArray())
                {
                    ApplyStep(client, step!.AsObject(), label);
                    await client.FlushAsync();
                }

                var expectedRequests = conformanceCase["expectedRequests"]!.AsArray();
                Assert.True(
                    handler.Requests.Count == expectedRequests.Count,
                    $"{label}: request count {handler.Requests.Count} != {expectedRequests.Count}");

                for (var i = 0; i < expectedRequests.Count; i++)
                {
                    var expected = expectedRequests[i]!;
                    var actual = handler.Requests[i];
                    var endpoint = expected["endpoint"]!.GetValue<string>();
                    Assert.True(actual.Path.EndsWith(endpoint, StringComparison.Ordinal), $"{label}[{i}] endpoint {actual.Path}");

                    var body = JsonNode.Parse(actual.Body)!;
                    if (endpoint == "/v1/identify")
                    {
                        AssertDeepEqual(Resolve(expected["body"]), body, $"{label}[{i}] body");
                        continue;
                    }

                    var events = body["events"]?.AsArray() ?? new JsonArray(body.DeepClone());
                    var expectedEvents = expected["events"]!.AsArray();
                    Assert.True(events.Count == expectedEvents.Count, $"{label}[{i}] event count");
                    for (var j = 0; j < expectedEvents.Count; j++)
                    {
                        AssertEvent(events[j]!.AsObject(), Resolve(expectedEvents[j])!.AsObject(), $"{label}[{i}].events[{j}]");
                    }
                }
            }
        }
    }

    private static void ApplyStep(WhisperrClient client, JsonObject step, string label)
    {
        if (step["track"] is JsonObject track)
        {
            client.Track(
                track["externalUserId"]!.GetValue<string>(),
                track["eventType"]!.GetValue<string>(),
                ToDictionary(track["properties"]));
        }
        else if (step["identify"] is JsonObject identify)
        {
            client.Identify(identify["externalUserId"]!.GetValue<string>(), new IdentifyOptions
            {
                Traits = ToDictionary(identify["traits"]),
                PushToken = identify["pushToken"]?.GetValue<string>()
            });
        }
        else
        {
            throw new InvalidOperationException($"{label}: step {step.ToJsonString()} is not supported by a server SDK");
        }
    }

    private static void AssertEvent(JsonObject actual, JsonObject expected, string label)
    {
        var actualRest = (JsonObject)actual.DeepClone();
        actualRest.Remove("occurred_at");
        var actualContext = actualRest["context"]!.AsObject();
        actualRest.Remove("context");

        var expectedRest = (JsonObject)expected.DeepClone();
        var expectedContext = expectedRest["context"] as JsonObject;
        expectedRest.Remove("context");

        AssertDeepEqual(expectedRest, actualRest, label);
        Assert.True(actualContext["$message_id"] is JsonValue, $"{label} context.$message_id");
        if (expectedContext == null)
        {
            return;
        }

        foreach (var key in GovernedContextKeys)
        {
            if (expectedContext.ContainsKey(key))
            {
                Assert.True(actualContext.ContainsKey(key), $"{label} context.{key} missing");
                AssertDeepEqual(expectedContext[key], actualContext[key], $"{label} context.{key}");
            }
            else
            {
                Assert.False(actualContext.ContainsKey(key), $"{label} context.{key} must be absent");
            }
        }
    }

    private static JsonNode? Resolve(JsonNode? node)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                return text switch
                {
                    "$sdk_name" => JsonValue.Create(SdkInfo.Name),
                    "$sdk_version" => JsonValue.Create(SdkInfo.Version),
                    _ => node.DeepClone()
                };
            case JsonArray array:
                return new JsonArray(array.Select(Resolve).ToArray());
            case JsonObject obj:
                var resolved = new JsonObject();
                foreach (var item in obj)
                {
                    resolved[item.Key] = Resolve(item.Value);
                }

                return resolved;
            default:
                return node?.DeepClone();
        }
    }

    private static IReadOnlyDictionary<string, object?>? ToDictionary(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        return obj.ToDictionary(
            item => item.Key,
            item => (object?)JsonDocument.Parse(item.Value?.ToJsonString() ?? "null").RootElement.Clone(),
            StringComparer.Ordinal);
    }

    private static void AssertDeepEqual(JsonNode? expected, JsonNode? actual, string label)
    {
        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"{label}: expected {expected?.ToJsonString()}, got {actual?.ToJsonString()}");
    }
}
