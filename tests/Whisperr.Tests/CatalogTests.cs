using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Whisperr.Tests;

public sealed class CatalogTests
{
    private static CatalogReply Ok(int n) =>
        new CatalogReply(200, "{\"accepted\":" + n + ",\"applied\":" + n + ",\"unchanged\":0,\"ignored_older\":0,\"rejected\":[]}");

    private static CatalogItem Item(string id) => new CatalogItem
    {
        SourceItemId = id,
        Type = CatalogItemTypes.Product,
        Name = "Item " + id,
        Availability = CatalogAvailability.Available,
        UpdatedAt = DateTimeOffset.Parse("2026-10-23T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
    };

    private static (WhisperrCatalogClient Client, ScriptedCatalogHandler Handler, List<TimeSpan> Waits) Client(Func<string?, int, CatalogReply> reply, int maxRetries = 5, bool realBackoff = false)
    {
        var handler = new ScriptedCatalogHandler(reply);
        var waits = new List<TimeSpan>();
        var client = new WhisperrCatalogClient(new WhisperrCatalogOptions
        {
            ApiKey = "wrk_x",
            BaseUrl = new Uri("https://api.test"),
            HttpClient = new HttpClient(handler),
            MaxRetries = maxRetries,
            Delay = (wait, _) =>
            {
                waits.Add(wait);
                return Task.CompletedTask;
            },
        });
        return (client, handler, waits);
    }

    [Fact]
    public async Task PublishableKeyIsRefusedFromWhisperrClientWithoutARequest()
    {
        var handler = new ScriptedCatalogHandler((_, _) => Ok(1));
        using var w = new WhisperrClient(new WhisperrOptions { ApiKey = "wpk_live_x", HttpClient = new HttpClient(handler), FlushInterval = TimeSpan.Zero });
        await Assert.ThrowsAsync<WhisperrConfigurationException>(() => w.Catalog.UpsertAsync(new[] { Item("a") }));
        await Assert.ThrowsAsync<WhisperrConfigurationException>(() => w.Catalog.StatusAsync());
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task DisabledClientRefusesCatalogCalls()
    {
        using var w = new WhisperrClient(new WhisperrOptions { Disabled = true });
        await Assert.ThrowsAsync<WhisperrConfigurationException>(() => w.Catalog.HeartbeatAsync(1));
    }

    [Fact]
    public async Task WhisperrClientCatalogUsesItsKeyBaseAndHttpClient()
    {
        var handler = new ScriptedCatalogHandler((_, _) => new CatalogReply(200, "{\"refreshed\":true}"));
        using var w = new WhisperrClient(new WhisperrOptions
        {
            ApiKey = "wrk_live_k",
            BaseUrl = new Uri("https://eu.api.test/"),
            HttpClient = new HttpClient(handler),
            FlushInterval = TimeSpan.Zero,
        });
        Assert.Same(w.Catalog, w.Catalog);
        var res = await w.Catalog.HeartbeatAsync(3);
        Assert.True(res.Refreshed);
        Assert.Equal("https://eu.api.test/v1/catalog/heartbeat", handler.Calls[0].Url);
        Assert.Equal("wrk_live_k", handler.Calls[0].Headers["X-API-Key"]);
        Assert.StartsWith("whisperr-dotnet/", handler.Calls[0].Headers["User-Agent"]);
    }

    [Fact]
    public async Task Retries500AndNetworkErrorsWithBackoffAndTheSameBody()
    {
        var (client, handler, waits) = Client((_, i) => i == 0 ? new CatalogReply(500) : i == 1 ? new CatalogReply(0, throws: new HttpRequestException("reset")) : Ok(1));
        var res = await client.UpsertAsync(new[] { Item("a") });
        Assert.Equal(1, res.Accepted);
        Assert.Equal(3, handler.Calls.Count);
        Assert.Single(handler.Calls.Select(c => c.Body).Distinct());
        Assert.True(waits[0] >= TimeSpan.FromSeconds(1));
        Assert.True(waits[1] >= TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ExhaustedRetriesThrowRetryableWithPartial()
    {
        var (client, handler, _) = Client((_, i) => i == 0 ? Ok(500) : new CatalogReply(503), maxRetries: 2);
        var items = Enumerable.Range(0, 501).Select(i => Item("s" + i)).ToList();
        var e = await Assert.ThrowsAsync<WhisperrCatalogException>(() => client.UpsertAsync(items));
        Assert.True(e.Retryable);
        Assert.Equal(503, e.Status);
        Assert.Equal(500, Assert.IsType<CatalogUpsertResult>(e.Partial).Accepted);
        Assert.Equal(4, handler.Calls.Count);
    }

    [Fact]
    public async Task A4xxIsNotRetried()
    {
        var (client, handler, _) = Client((_, _) => new CatalogReply(400, "{\"error\":{\"code\":\"invalid_request\",\"message\":\"bad\"}}"));
        var e = await Assert.ThrowsAsync<WhisperrCatalogException>(() => client.UpsertAsync(new[] { Item("a") }));
        Assert.Equal((400, "invalid_request", false), (e.Status, e.Code, e.Retryable));
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task A413SplitsAndRejectsALoneItemStillTooLarge()
    {
        var (client, _, _) = Client((body, _) =>
        {
            using var doc = JsonDocument.Parse(body!);
            var items = doc.RootElement.GetProperty("items");
            return items.GetArrayLength() > 1 || items[0].GetProperty("source_item_id").GetString() == "big"
                ? new CatalogReply(413, "{\"error\":{\"code\":\"batch_too_large\",\"message\":\"too large\"}}")
                : Ok(1);
        });
        var res = await client.UpsertAsync(new[] { Item("a"), Item("big"), Item("c") });
        Assert.Equal(2, res.Accepted);
        var r = Assert.Single(res.Rejected);
        Assert.Equal((1, "big", "item_too_large"), (r.Index, r.SourceItemId, r.Code));
    }

    [Fact]
    public async Task ChunksByBodySize()
    {
        var (client, handler, _) = Client((body, _) =>
        {
            using var doc = JsonDocument.Parse(body!);
            return Ok(doc.RootElement.GetProperty("items").GetArrayLength());
        });
        var items = Enumerable.Range(0, 400).Select(i =>
        {
            var item = Item("s" + i);
            item.Tags = Enumerable.Repeat(new string('x', 200), 60).ToList();
            return item;
        }).ToList();
        var res = await client.UpsertAsync(items);
        Assert.Equal(400, res.Accepted);
        Assert.True(handler.Calls.Count > 1);
        Assert.All(handler.Calls, c => Assert.True(Encoding.UTF8.GetByteCount(c.Body!) <= WhisperrCatalogClient.MaxBodyBytes));
    }

    [Fact]
    public async Task TimesAreUtcAndNullsAreLeftOut()
    {
        var (client, handler, _) = Client((_, _) => Ok(1));
        var item = Item("a");
        item.UpdatedAt = DateTimeOffset.Parse("2026-10-23T09:00:00.120+02:00", System.Globalization.CultureInfo.InvariantCulture);
        item.Price = new CatalogPrice { AmountMinor = WhisperrMoney.ToMinorUnits(19.99m, "USD"), Currency = "USD" };
        item.Extras = new Dictionary<string, string> { ["age_group"] = "adult" };
        await client.UpsertAsync(new[] { item });
        var sent = JsonDocument.Parse(handler.Calls[0].Body!).RootElement.GetProperty("items")[0];
        Assert.Equal("2026-10-23T07:00:00.120Z", sent.GetProperty("updated_at").GetString());
        Assert.False(sent.TryGetProperty("url", out _));
        Assert.False(sent.GetProperty("price").TryGetProperty("meaning", out _));
        Assert.Equal(1999, sent.GetProperty("price").GetProperty("amount_minor").GetInt64());
        Assert.Equal("adult", sent.GetProperty("extras").GetProperty("age_group").GetString());
    }

    [Fact]
    public async Task DeleteUsesOneDeletedAtAcrossChunks()
    {
        var (client, handler, _) = Client((body, _) =>
        {
            using var doc = JsonDocument.Parse(body!);
            return new CatalogReply(200, "{\"deleted\":" + doc.RootElement.GetProperty("items").GetArrayLength() + ",\"ignored_older\":0,\"not_found\":0,\"rejected\":[]}");
        });
        var res = await client.DeleteAsync(Enumerable.Range(0, 1500).Select(i => "s" + i));
        Assert.Equal(1500, res.Deleted);
        Assert.Equal(2, handler.Calls.Count);
        var stamps = handler.Calls
            .SelectMany(c => JsonDocument.Parse(c.Body!).RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("deleted_at").GetString()))
            .Distinct();
        Assert.Single(stamps);
    }

    [Fact]
    public async Task CountsAreCheckedLocally()
    {
        var (client, _, _) = Client((_, _) => Ok(1));
        await Assert.ThrowsAsync<WhisperrConfigurationException>(() => client.HeartbeatAsync(-1));
        await Assert.ThrowsAsync<WhisperrConfigurationException>(() => client.CompleteSnapshotAsync("snap/1", 1));
    }

    [Fact]
    public void MinorUnitsFromDecimals()
    {
        Assert.Equal(1999, WhisperrMoney.ToMinorUnits(19.99m, "USD"));
        Assert.Equal(1500, WhisperrMoney.ToMinorUnits(1500m, "JPY"));
        Assert.Equal(1234, WhisperrMoney.ToMinorUnits(1.234m, "kwd"));
        Assert.Throws<ArgumentException>(() => WhisperrMoney.ToMinorUnits(9.999m, "USD"));
        Assert.Throws<ArgumentException>(() => WhisperrMoney.ToMinorUnits(-1m, "USD"));
        Assert.Throws<ArgumentException>(() => WhisperrMoney.ToMinorUnits(decimal.MaxValue, "USD"));
    }
}
