# Whisperr .NET SDK

Server-side SDK for .NET - reliable churn-signal event tracking for ASP.NET Core,
workers, serverless handlers, and any backend that knows the end-user id.

```bash
dotnet add package Whisperr
```

## Quick Start

```csharp
using Whisperr;

var whisperr = new WhisperrClient(Environment.GetEnvironmentVariable("WHISPERR_API_KEY")!);

whisperr.Track(
    "user_8842",
    "payment_failed",
    new Dictionary<string, object?>
    {
        ["amount_cents"] = 4900,
        ["reason"] = "card_declined"
    });

whisperr.Identify("user_8842", new IdentifyOptions
{
    Email = "ada@example.com",
    Traits = new Dictionary<string, object?>
    {
        ["plan"] = "pro"
    }
});

await whisperr.ShutdownAsync();
```

The user id is always explicit. Pass the same `external_user_id` that your app
uses everywhere else, and backend events land on the same Whisperr timeline as
frontend events.

## ASP.NET Core

```csharp
using Whisperr;

builder.Services.AddSingleton(_ => new WhisperrClient(new WhisperrOptions
{
    ApiKey = builder.Configuration["WHISPERR_API_KEY"],
    OnError = error => Console.Error.WriteLine($"whisperr:{error.Code} {error.Message}")
}));
```

```csharp
app.MapPost("/billing/webhook", async (
    BillingWebhook webhook,
    WhisperrClient whisperr,
    CancellationToken cancellationToken) =>
{
    whisperr.Track(webhook.UserId, "payment_failed", new Dictionary<string, object?>
    {
        ["amount_cents"] = webhook.AmountCents,
        ["provider"] = "stripe"
    });

    await whisperr.FlushAsync(cancellationToken);
    return Results.Ok();
});
```

## Behavior

- `Track` and `Identify` enqueue in memory and return immediately.
- Events flush in batches to `/v1/events/batch`; identity updates flush to
  `/v1/identify`.
- Requests use `X-API-Key`.
- `event_type` must be lowercase `snake_case`; invalid events are dropped before
  they can poison a batch.
- Each event gets a stable `$message_id` idempotency key in `context`; retries
  reuse the same id.
- `401`/`403` retain the batch and surface `auth`.
- `429`, `5xx`, timeouts, and network failures retry with bounded backoff; after
  retries are exhausted, the batch is retained and `retry_exhausted` is surfaced.
- Other `4xx` responses drop the offending batch and surface `dropped`.

The queue is in-process, not crash-durable. Call `FlushAsync` or
`ShutdownAsync` before short-lived processes exit.

## Options

| Option | Default | Notes |
|---|---:|---|
| `ApiKey` | - | App ingestion key (`wrk_...`). Required unless disabled. |
| `BaseUrl` | `https://api.whisperr.net` | Whisperr ingestion API. |
| `FlushAt` | `20` | Flush when this many operations are queued. |
| `FlushInterval` | `10s` | Background flush cadence. Use `TimeSpan.Zero` to disable. |
| `MaxQueueSize` | `10000` | Oldest operations drop on overflow. |
| `MaxBatchSize` | `500` | Events per batch; backend cap is 500. |
| `MaxRetries` | `6` | Transient retries before retaining for a later flush. |
| `RequestTimeout` | `10s` | Per-request timeout. |
| `Disabled` | `false` | No-op client for tests and local modes. |
| `Debug` | `false` | Emits diagnostic warnings to `Logger` or stderr. |
| `OnError` | - | Observability hook for `auth`, `dropped`, `retry_exhausted`. |
| `HttpClient` | private client | Pass your own for custom handlers or test capture. |

## Development

The tests consume `whisperr-spec` fixtures. In this workspace the sibling
`../whisperr-spec` repo is picked up automatically. In CI or another checkout:

```bash
export WHISPERR_SPEC_PATH=/path/to/whisperr-spec/conformance/wire.json
dotnet test
```

Build and pack:

```bash
dotnet test -c Release
dotnet pack src/Whisperr/Whisperr.csproj -c Release -o artifacts
```

Publish to NuGet. Releases are cut by pushing a `v*.*.*` tag (the workflow
derives the package version from the tag), but you can also push locally:

```bash
dotnet nuget push artifacts/Whisperr.*.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$NUGET_API_KEY" \
  --skip-duplicate
```

Whisperr - predict churn, automate interventions, recover revenue.
[whisperr.net](https://whisperr.net)
