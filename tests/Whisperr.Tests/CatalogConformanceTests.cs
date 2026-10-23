using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Whisperr.Tests;

/// <summary>
/// Catalog push conformance (Fixtures/catalog_push.json), a byte-identical copy
/// of whisperr-spec conformance/catalog_push.json. Point
/// WHISPERR_CATALOG_SPEC_PATH at a spec checkout's file to run that copy instead
/// and to check the bundled one has not drifted from it. Fixture items are wire
/// JSON, read into <see cref="CatalogItem"/> through its JSON names.
/// </summary>
public sealed class CatalogConformanceTests
{
    public static IEnumerable<object[]> CaseNames()
    {
        using var doc = JsonDocument.Parse(CatalogFixture.Load());
        return doc.RootElement.GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("name").GetString()! }).ToList();
    }

    public static IEnumerable<object[]> MinorUnitCases()
    {
        using var doc = JsonDocument.Parse(CatalogFixture.Load());
        return doc.RootElement.GetProperty("minor_units").EnumerateArray().Select(m => new object[] { m.GetRawText() }).ToList();
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Case(string name)
    {
        using var doc = JsonDocument.Parse(CatalogFixture.Load());
        var c = doc.RootElement.GetProperty("cases").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        var exchanges = c.GetProperty("exchanges").EnumerateArray().ToList();
        var apiKey = c.TryGetProperty("api_key", out var k) ? k.GetString()! : "wrk_test";

        var handler = new ScriptedCatalogHandler((body, i) =>
        {
            if (i >= exchanges.Count)
            {
                throw new InvalidOperationException($"{name}: unexpected request");
            }

            var r = exchanges[i].GetProperty("response");
            var headers = new Dictionary<string, string>();
            if (r.TryGetProperty("headers", out var h))
            {
                foreach (var p in h.EnumerateObject())
                {
                    headers[p.Name] = p.Value.GetString()!;
                }
            }

            return new CatalogReply(r.GetProperty("status").GetInt32(), r.TryGetProperty("body", out var b) ? b.GetRawText() : null, headers);
        });
        using var http = new HttpClient(handler);
        using var client = new WhisperrCatalogClient(new WhisperrCatalogOptions
        {
            ApiKey = apiKey,
            BaseUrl = new Uri("https://api.test"),
            HttpClient = http,
            Delay = (_, _) => Task.CompletedTask,
        });

        object? result = null;
        Exception? error = null;
        try
        {
            result = await Run(client, c.GetProperty("op").GetString()!, c.GetProperty("args"));
        }
        catch (Exception e)
        {
            error = e;
        }

        Assert.True(exchanges.Count == handler.Calls.Count, $"request count: {error}");
        for (var i = 0; i < exchanges.Count; i++)
        {
            var req = exchanges[i].GetProperty("request");
            var got = handler.Calls[i];
            Assert.Equal(req.GetProperty("method").GetString(), got.Method);
            Assert.Equal(req.GetProperty("path").GetString(), got.Path);
            Assert.Equal(apiKey, got.Headers["X-API-Key"]);
            if (got.Method == "GET")
            {
                Assert.Null(got.Body);
            }
            else
            {
                Assert.StartsWith("application/json", got.Headers["Content-Type"]);
            }

            if (req.TryGetProperty("body", out var wantBody))
            {
                Assert.Equal(CanonicalJson.Of(wantBody), CanonicalJson.Of(got.Body!));
            }

            if (req.TryGetProperty("item_ids", out var ids))
            {
                Assert.Equal(ids.EnumerateArray().Select(x => x.GetString()), SentIds(got));
            }

            if (req.TryGetProperty("item_count", out var count))
            {
                var sent = SentIds(got);
                Assert.Equal(count.GetInt32(), sent.Count);
                Assert.Equal(req.GetProperty("first_item_id").GetString(), sent[0]);
                Assert.Equal(req.GetProperty("last_item_id").GetString(), sent[sent.Count - 1]);
            }

            if (req.TryGetProperty("same_body_as_previous", out var same) && same.GetBoolean())
            {
                Assert.Equal(handler.Calls[i - 1].Body, got.Body);
            }
        }

        if (c.TryGetProperty("error", out var want))
        {
            if (want.TryGetProperty("configuration", out _) || want.TryGetProperty("local", out _))
            {
                Assert.IsType<WhisperrConfigurationException>(error);
            }
            else
            {
                var e = Assert.IsType<WhisperrCatalogException>(error);
                Assert.Equal(want.GetProperty("status").GetInt32(), e.Status);
                Assert.Equal(want.GetProperty("code").GetString(), e.Code);
                Assert.Equal(want.GetProperty("retryable").GetBoolean(), e.Retryable);
            }

            return;
        }

        Assert.Null(error);
        var actual = JsonSerializer.Serialize(result, result!.GetType(), WhisperrCatalogClient.Json);
        Assert.Equal(CanonicalJson.Of(ExpectedResult(doc.RootElement, c.GetProperty("result"), actual)), CanonicalJson.Of(actual));
    }

    /// <summary>Client-side reject codes carry an SDK-defined message: check it is set, not its text.</summary>
    private static string ExpectedResult(JsonElement spec, JsonElement result, string actual)
    {
        var client = spec.GetProperty("reject_codes").GetProperty("client").EnumerateArray().Select(x => x.GetString()).ToHashSet();
        var want = JsonNode.Parse(result.GetRawText())!;
        var got = JsonNode.Parse(actual)!;
        if (want["rejected"] is JsonArray rejected)
        {
            for (var i = 0; i < rejected.Count; i++)
            {
                if (client.Contains(rejected[i]!["code"]!.GetValue<string>()))
                {
                    var message = got["rejected"]?[i]?["message"]?.GetValue<string>();
                    Assert.False(string.IsNullOrWhiteSpace(message), "a client-side reject needs a message");
                    rejected[i]!["message"] = message;
                }
            }
        }

        return want.ToJsonString();
    }

    [Fact]
    public void BundledFixtureIsByteIdenticalToTheSpecCopy()
    {
        var spec = Environment.GetEnvironmentVariable("WHISPERR_CATALOG_SPEC_PATH");
        if (string.IsNullOrWhiteSpace(spec))
        {
            return; // nothing to compare against without a spec checkout
        }

        Assert.Equal(File.ReadAllBytes(spec), File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalog_push.json")));
    }

    [Fact]
    public void TheOnlySdkWrittenRejectCodeIsListedAsClientSide()
    {
        // item_too_large is the only code the SDK adds; the server never sends it.
        using var doc = JsonDocument.Parse(CatalogFixture.Load());
        var codes = doc.RootElement.GetProperty("reject_codes");
        Assert.Equal(new[] { "item_too_large" }, codes.GetProperty("client").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain("item_too_large", codes.GetProperty("server").EnumerateArray().Select(x => x.GetString()));
    }

    [Theory]
    [MemberData(nameof(MinorUnitCases))]
    public void MinorUnits(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var m = doc.RootElement;
        var amount = m.GetProperty("amount").GetString()!;
        var currency = m.GetProperty("currency").GetString()!;
        if (m.TryGetProperty("error", out _))
        {
            Assert.ThrowsAny<ArgumentException>(() => WhisperrMoney.ToMinorUnits(amount, currency));
        }
        else
        {
            Assert.Equal(m.GetProperty("minor").GetInt64(), WhisperrMoney.ToMinorUnits(amount, currency));
        }
    }

    private static List<string?> SentIds(CatalogCall call)
    {
        using var doc = JsonDocument.Parse(call.Body!);
        return doc.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("source_item_id").GetString()).ToList();
    }

    private static async Task<object> Run(WhisperrCatalogClient client, string op, JsonElement a)
    {
        switch (op)
        {
            case "upsert":
                List<CatalogItem> items;
                if (a.TryGetProperty("items", out var raw))
                {
                    items = JsonSerializer.Deserialize<List<CatalogItem>>(raw.GetRawText(), WhisperrCatalogClient.Json)!;
                }
                else
                {
                    var g = a.GetProperty("generate_items");
                    var template = g.GetProperty("template").GetRawText();
                    var prefix = g.GetProperty("source_item_id_prefix").GetString();
                    items = Enumerable.Range(0, g.GetProperty("count").GetInt32()).Select(i =>
                    {
                        var item = JsonSerializer.Deserialize<CatalogItem>(template, WhisperrCatalogClient.Json)!;
                        item.SourceItemId = prefix + i;
                        return item;
                    }).ToList();
                }

                return await client.UpsertAsync(items);
            case "delete":
                if (a.TryGetProperty("items", out var del))
                {
                    return await client.DeleteAsync(JsonSerializer.Deserialize<List<CatalogDeletion>>(del.GetRawText(), WhisperrCatalogClient.Json)!);
                }

                var deletedAt = DateTimeOffset.Parse(a.GetProperty("deleted_at").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                List<string> idList;
                if (a.TryGetProperty("ids", out var ids))
                {
                    idList = ids.EnumerateArray().Select(x => x.GetString()!).ToList();
                }
                else
                {
                    var g = a.GetProperty("generate_ids");
                    var prefix = g.GetProperty("prefix").GetString();
                    idList = Enumerable.Range(0, g.GetProperty("count").GetInt32()).Select(i => prefix + i).ToList();
                }

                return await client.DeleteAsync(idList, deletedAt);
            case "begin_snapshot":
                return await client.BeginSnapshotAsync();
            case "complete_snapshot":
                return await client.CompleteSnapshotAsync(a.GetProperty("snapshot_id").GetString()!, a.GetProperty("item_count").GetInt64());
            case "heartbeat":
                return await client.HeartbeatAsync(a.GetProperty("item_count").GetInt64());
            case "status":
                return await client.StatusAsync();
            default:
                throw new InvalidOperationException("unknown op " + op);
        }
    }
}
