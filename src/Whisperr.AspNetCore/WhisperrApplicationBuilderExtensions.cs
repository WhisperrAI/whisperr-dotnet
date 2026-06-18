using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Whisperr.AspNetCore;

/// <summary>
/// Pipeline helpers for the Whisperr request-scoped middleware.
/// </summary>
public static class WhisperrApplicationBuilderExtensions
{
    /// <summary>
    /// Adds middleware that exposes <see cref="HttpContextWhisperrExtensions.Whisperr"/>
    /// bound to the request's user. Mount it after authentication so the user is
    /// resolvable. By default the user id comes from the <c>NameIdentifier</c> or
    /// <c>sub</c> claim; pass <paramref name="resolveUser"/> to override.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="resolveUser">Optional custom user-id resolver.</param>
    public static IApplicationBuilder UseWhisperr(
        this IApplicationBuilder app,
        Func<HttpContext, string?>? resolveUser = null)
    {
        if (app == null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        Func<HttpContext, string?> resolver = resolveUser ?? WhisperrMiddleware.ResolveUserFromClaims;
        return app.UseMiddleware<WhisperrMiddleware>(resolver);
    }
}
