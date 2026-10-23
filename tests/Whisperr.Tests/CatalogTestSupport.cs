using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Whisperr.Tests;

internal sealed class CatalogCall
{
    public CatalogCall(string method, string path, string url, Dictionary<string, string> headers, string? body)
    {
        Method = method;
        Path = path;
        Url = url;
        Headers = headers;
        Body = body;
    }

    public string Method { get; }

    public string Path { get; }

    public string Url { get; }

    public Dictionary<string, string> Headers { get; }

    public string? Body { get; }
}

internal sealed class CatalogReply
{
    public CatalogReply(int status, string? body = null, Dictionary<string, string>? headers = null, Exception? throws = null)
    {
        Status = status;
        Body = body;
        Headers = headers ?? new Dictionary<string, string>();
        Throws = throws;
    }

    public int Status { get; }

    public string? Body { get; }

    public Dictionary<string, string> Headers { get; }

    public Exception? Throws { get; }
}

internal sealed class ScriptedCatalogHandler : HttpMessageHandler
{
    private readonly Func<string?, int, CatalogReply> _reply;

    public ScriptedCatalogHandler(Func<string?, int, CatalogReply> reply)
    {
        _reply = reply;
    }

    public List<CatalogCall> Calls { get; } = new List<CatalogCall>();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in request.Headers)
        {
            headers[h.Key] = string.Join(",", h.Value);
        }

        if (request.Content != null)
        {
            body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            foreach (var h in request.Content.Headers)
            {
                headers[h.Key] = string.Join(",", h.Value);
            }
        }

        Calls.Add(new CatalogCall(request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.ToString(), headers, body));
        var reply = _reply(body, Calls.Count - 1);
        if (reply.Throws != null)
        {
            throw reply.Throws;
        }

        var response = new HttpResponseMessage((HttpStatusCode)reply.Status)
        {
            Content = new StringContent(reply.Body ?? string.Empty, Encoding.UTF8, "application/json"),
        };
        foreach (var h in reply.Headers)
        {
            response.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        return response;
    }
}

internal static class CanonicalJson
{
    /// <summary>Sorted keys, nulls dropped, so two documents compare by content.</summary>
    public static string Of(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Of(doc.RootElement);
    }

    public static string Of(JsonElement element)
    {
        var sb = new StringBuilder();
        Write(element, sb);
        return sb.ToString();
    }

    private static void Write(JsonElement e, StringBuilder sb)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append('{');
                var first = true;
                foreach (var p in e.EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Null).OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    sb.Append(JsonSerializer.Serialize(p.Name)).Append(':');
                    Write(p.Value, sb);
                }

                sb.Append('}');
                break;
            case JsonValueKind.Array:
                sb.Append('[');
                var i = 0;
                foreach (var x in e.EnumerateArray())
                {
                    if (i++ > 0)
                    {
                        sb.Append(',');
                    }

                    Write(x, sb);
                }

                sb.Append(']');
                break;
            case JsonValueKind.Number:
                sb.Append(e.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                sb.Append(e.GetRawText());
                break;
        }
    }
}

internal static class CatalogFixture
{
    public static string Load()
    {
        var explicitPath = Environment.GetEnvironmentVariable("WHISPERR_CATALOG_SPEC_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return File.ReadAllText(explicitPath);
        }

        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalog_push.json"));
    }
}
