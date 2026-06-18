using System;
using Microsoft.AspNetCore.Http;

namespace Whisperr.AspNetCore;

/// <summary>
/// Convenience accessors for the request-scoped Whisperr helper.
/// </summary>
public static class HttpContextWhisperrExtensions
{
    /// <summary>
    /// Returns the <see cref="RequestWhisperr"/> attached by <c>UseWhisperr</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the Whisperr middleware has not been added to the pipeline.
    /// </exception>
    public static RequestWhisperr Whisperr(this HttpContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Items.TryGetValue(WhisperrMiddleware.ItemKey, out var value) && value is RequestWhisperr request)
        {
            return request;
        }

        throw new InvalidOperationException(
            "Whisperr request helper is unavailable. Call app.UseWhisperr() to add the middleware.");
    }
}
