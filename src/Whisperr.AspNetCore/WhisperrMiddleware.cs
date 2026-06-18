using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Whisperr;

namespace Whisperr.AspNetCore;

/// <summary>
/// Attaches a <see cref="RequestWhisperr"/> to <see cref="HttpContext.Items"/>
/// for each request, bound to the user id resolved from the request.
/// </summary>
public sealed class WhisperrMiddleware
{
    internal const string ItemKey = "whisperr.request";

    private readonly RequestDelegate _next;
    private readonly WhisperrClient _client;
    private readonly Func<HttpContext, string?> _resolveUser;

    /// <summary>
    /// Creates the middleware. The <paramref name="client"/> is supplied by DI;
    /// the resolver is supplied by <c>UseWhisperr</c>.
    /// </summary>
    public WhisperrMiddleware(RequestDelegate next, WhisperrClient client, Func<HttpContext, string?> resolveUser)
    {
        _next = next;
        _client = client;
        _resolveUser = resolveUser;
    }

    /// <summary>
    /// Resolves the user id and stores a request helper on the context.
    /// </summary>
    public Task InvokeAsync(HttpContext context)
    {
        var userId = _resolveUser(context);
        context.Items[ItemKey] = new RequestWhisperr(_client, string.IsNullOrEmpty(userId) ? null : userId);
        return _next(context);
    }

    internal static string? ResolveUserFromClaims(HttpContext context)
    {
        var user = context.User;
        if (user?.Identity == null || !user.Identity.IsAuthenticated)
        {
            return null;
        }

        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    }
}
