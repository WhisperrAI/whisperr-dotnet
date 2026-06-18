#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Whisperr.AspNetCore;
using Xunit;

namespace Whisperr.Tests;

public sealed class AspNetCoreTests
{
    [Fact]
    public void AddWhisperrBindsConfigurationAndRegistersSingleton()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Whisperr:ApiKey"] = "wrk_di",
                ["Whisperr:FlushInterval"] = "00:00:00",
                ["Whisperr:FlushAt"] = "42"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddWhisperr(configuration);
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<WhisperrClient>();
        var second = provider.GetRequiredService<WhisperrClient>();

        Assert.Same(first, second);
        Assert.True(first.Ready);
    }

    [Fact]
    public void AddWhisperrFallsBackToEnvironmentApiKey()
    {
        var previous = Environment.GetEnvironmentVariable("WHISPERR_API_KEY");
        Environment.SetEnvironmentVariable("WHISPERR_API_KEY", "wrk_env");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Whisperr:FlushInterval"] = "00:00:00"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddWhisperr(configuration);
            using var provider = services.BuildServiceProvider();

            var client = provider.GetRequiredService<WhisperrClient>();
            Assert.True(client.Ready);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WHISPERR_API_KEY", previous);
        }
    }

    [Fact]
    public async Task MiddlewareNoOpsWithoutResolvedUser()
    {
        using var client = new WhisperrClient(new WhisperrOptions
        {
            ApiKey = "wrk_test",
            FlushInterval = TimeSpan.Zero
        });

        var middleware = new WhisperrMiddleware(_ => Task.CompletedTask, client, _ => null);
        var context = new DefaultHttpContext();
        await middleware.InvokeAsync(context);

        var request = context.Whisperr();
        Assert.Null(request.UserId);
        Assert.False(request.Track("feature_used"));
        Assert.Equal(0, client.PendingCount);
    }

    [Fact]
    public async Task MiddlewareBindsResolvedUserToRequestHelper()
    {
        using var client = new WhisperrClient(new WhisperrOptions
        {
            ApiKey = "wrk_test",
            FlushInterval = TimeSpan.Zero
        });

        var middleware = new WhisperrMiddleware(_ => Task.CompletedTask, client, _ => "user_1");
        var context = new DefaultHttpContext();
        await middleware.InvokeAsync(context);

        var request = context.Whisperr();
        Assert.Equal("user_1", request.UserId);
        Assert.True(request.Track("feature_used"));
        Assert.Equal(1, client.PendingCount);
    }

    [Fact]
    public void HttpContextWhisperrThrowsWhenMiddlewareMissing()
    {
        var context = new DefaultHttpContext();
        Assert.Throws<InvalidOperationException>(() => context.Whisperr());
    }
}
#endif
