using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Whisperr.Tests;

public sealed class EnvironmentContextTests
{
    [Fact]
    public async Task EveryEventCarriesSdkNameAndVersionAndNoAppVersionByDefault()
    {
        using var context = await SendOneAsync(appVersion: null);
        var root = context.RootElement;

        Assert.Equal("whisperr-dotnet", root.GetProperty("sdk_name").GetString());
        Assert.Equal(SdkInfo.Version, root.GetProperty("sdk_version").GetString());
        Assert.False(root.TryGetProperty("app_version", out _));
        Assert.True(root.TryGetProperty("$message_id", out _));
    }

    [Fact]
    public async Task ConfiguredAppVersionIsSent()
    {
        using var context = await SendOneAsync(appVersion: "2026.10.1");

        Assert.Equal("2026.10.1", context.RootElement.GetProperty("app_version").GetString());
    }

    [Fact]
    public async Task BlankAppVersionSendsNoKey()
    {
        using var context = await SendOneAsync(appVersion: "  ");

        Assert.False(context.RootElement.TryGetProperty("app_version", out _));
    }

    [Fact]
    public async Task CallerContextKeyWinsButMessageIdStaysTheSdkValue()
    {
        using var context = await SendOneAsync(
            appVersion: "1.0.0",
            callerContext: new Dictionary<string, object?>
            {
                ["app_version"] = "relay-9",
                ["plan"] = "pro",
                ["$message_id"] = "caller"
            });
        var root = context.RootElement;

        Assert.Equal("relay-9", root.GetProperty("app_version").GetString());
        Assert.Equal("pro", root.GetProperty("plan").GetString());
        Assert.Equal("whisperr-dotnet", root.GetProperty("sdk_name").GetString());
        Assert.NotEqual("caller", root.GetProperty("$message_id").GetString());
    }

    [Fact]
    public void SdkVersionMatchesTheProjectVersion()
    {
        var csproj = Path.Combine(FindRepositoryRoot(), "src", "Whisperr", "Whisperr.csproj");
        var match = Regex.Match(File.ReadAllText(csproj), "<Version>([^<]+)</Version>");

        Assert.True(match.Success, "Whisperr.csproj has no <Version>");
        Assert.Equal(match.Groups[1].Value, SdkInfo.Version);
    }

    [Fact]
    public void BillingEventNamesMatchTheSpec()
    {
        var names = typeof(WhisperrBillingEvents)
            .GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "payment_failed",
                "purchase_completed",
                "subscription_cancelled",
                "subscription_renewed",
                "subscription_started",
                "trial_started"
            },
            names);
    }

    private static async Task<JsonDocument> SendOneAsync(
        string? appVersion,
        IReadOnlyDictionary<string, object?>? callerContext = null)
    {
        var handler = new TestHttpMessageHandler();
        using var http = new HttpClient(handler);
        using var client = new WhisperrClient(new WhisperrOptions
        {
            ApiKey = "wrk_test",
            FlushInterval = TimeSpan.Zero,
            HttpClient = http,
            AppVersion = appVersion
        });

        client.Track("user_1", "feature_used", context: callerContext);
        await client.FlushAsync();

        var request = Assert.Single(handler.Requests);
        using var body = JsonDocument.Parse(request.Body);
        return JsonDocument.Parse(body.RootElement.GetProperty("events")[0].GetProperty("context").GetRawText());
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Whisperr.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Whisperr.sln not found above " + AppContext.BaseDirectory);
    }
}
