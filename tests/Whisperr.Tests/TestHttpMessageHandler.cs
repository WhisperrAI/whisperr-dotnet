using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Whisperr.Tests;

internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    public int StatusCode { get; set; } = 202;

    public TimeSpan DelayBeforeResponse { get; set; } = TimeSpan.Zero;

    public List<CapturedRequest> Requests { get; } = new List<CapturedRequest>();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Requests.Add(new CapturedRequest(request.RequestUri?.AbsolutePath ?? string.Empty, body));
        if (DelayBeforeResponse > TimeSpan.Zero)
        {
            await Task.Delay(DelayBeforeResponse, cancellationToken).ConfigureAwait(false);
        }

        return new HttpResponseMessage((HttpStatusCode)StatusCode);
    }
}

internal sealed class CapturedRequest
{
    public CapturedRequest(string path, string body)
    {
        Path = path;
        Body = body;
    }

    public string Path { get; }

    public string Body { get; }
}
