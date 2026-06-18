using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Whisperr;

namespace Whisperr.AspNetCore;

/// <summary>
/// Dependency-injection helpers for registering a Whisperr client.
/// </summary>
public static class WhisperrServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="WhisperrClient"/> configured from the
    /// <c>Whisperr</c> configuration section, falling back to the
    /// <c>WHISPERR_API_KEY</c> environment variable for the API key. The host
    /// disposes the singleton on shutdown, which flushes any buffered events.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration to bind from.</param>
    /// <param name="configure">Optional hook to adjust options after binding.</param>
    public static IServiceCollection AddWhisperr(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<WhisperrOptions>? configure = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        services.AddSingleton(_ => new WhisperrClient(BuildOptions(configuration, configure)));
        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="WhisperrClient"/> configured purely in
    /// code. The host disposes the singleton on shutdown, which flushes any
    /// buffered events.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the client options.</param>
    public static IServiceCollection AddWhisperr(
        this IServiceCollection services,
        Action<WhisperrOptions> configure)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        services.AddSingleton(_ =>
        {
            var options = new WhisperrOptions();
            configure(options);
            return new WhisperrClient(options);
        });
        return services;
    }

    private static WhisperrOptions BuildOptions(IConfiguration configuration, Action<WhisperrOptions>? configure)
    {
        var options = new WhisperrOptions();
        configuration.GetSection("Whisperr").Bind(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            options.ApiKey = Environment.GetEnvironmentVariable("WHISPERR_API_KEY");
        }

        configure?.Invoke(options);
        return options;
    }
}
