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

Install the companion package for dependency-injection wiring:

```bash
dotnet add package Whisperr.AspNetCore
```

`AddWhisperr` binds the `Whisperr` configuration section and falls back to the
`WHISPERR_API_KEY` environment variable, then registers `WhisperrClient` as a
singleton. The host disposes the singleton on shutdown, which runs a final
flush, so you do not need to manage the lifetime yourself.

```csharp
using Whisperr.AspNetCore;

builder.Services.AddWhisperr(builder.Configuration, options =>
{
    options.OnError = error => Console.Error.WriteLine($"whisperr:{error.Code} {error.Message}");
});
```

```jsonc
// appsettings.json
{
  "Whisperr": {
    "ApiKey": "wrk_...",
    "FlushInterval": "00:00:10"
  }
}
```

Inject `WhisperrClient` wherever you have the user id. `Track`/`Identify`
enqueue and return immediately; let the background flush and host-shutdown flush
deliver them, rather than awaiting `FlushAsync` on the request path (a flush can
wait through retry backoff):

```csharp
app.MapPost("/billing/webhook", (
    BillingWebhook webhook,
    WhisperrClient whisperr) =>
{
    whisperr.Track(webhook.UserId, "payment_failed", new Dictionary<string, object?>
    {
        ["amount_cents"] = webhook.AmountCents,
        ["provider"] = "stripe"
    });

    return Results.Ok();
});
```

### Request-scoped tracking

For request handlers that already authenticate a user, add the middleware after
authentication and use `HttpContext.Whisperr()`, which binds to the user id from
the `NameIdentifier`/`sub` claim (override via `UseWhisperr(resolveUser)`):

```csharp
using Whisperr.AspNetCore;

app.UseAuthentication();
app.UseWhisperr();

app.MapPost("/plan/upgrade", (HttpContext http) =>
{
    http.Whisperr().Track("plan_upgraded", new Dictionary<string, object?> { ["plan"] = "pro" });
    return Results.Ok();
});
```

## Traits: `app_language`

`app_language` is a reserved trait. It is the language the user picked in your
product, not the device or browser locale. Use a BCP 47 tag (`de`, `pt-BR`).
Whisperr writes messages in this language. When it is not set, Whisperr uses
the event `context.locale`, then the `locale` trait.

The server SDK has no special API for it. Pass it in `Traits`:

```csharp
whisperr.Identify("user_8842", new IdentifyOptions
{
    Traits = new Dictionary<string, object?> { ["app_language"] = "pt-BR" }
});
```

The server keeps the last value you send. A server SDK never guesses the
language: the process culture is not the user's language.

## Context the SDK adds

Every event carries these keys in `context`:

| Key | Value |
|---|---|
| `sdk_name` | `whisperr-dotnet` |
| `sdk_version` | The SDK package version, for example `1.1.0` |
| `app_version` | The `AppVersion` option. Not set: no key. |

A key that you pass in the `context` argument of `Track` wins over the SDK
value. The SDK always sets `$message_id`.

```csharp
var whisperr = new WhisperrClient(new WhisperrOptions
{
    ApiKey = Environment.GetEnvironmentVariable("WHISPERR_API_KEY"),
    AppVersion = Environment.GetEnvironmentVariable("RELEASE_VERSION") // for example "2026.10.1" or a git SHA
});
```

With `AddWhisperr(builder.Configuration)`, set `Whisperr:AppVersion` in
configuration.

## Billing events

If Whisperr has no billing source for your app (for example no RevenueCat or
Stripe connection), send billing changes yourself. Use the reserved names in
`WhisperrBillingEvents`:

| Constant | Event name |
|---|---|
| `WhisperrBillingEvents.TrialStarted` | `trial_started` |
| `WhisperrBillingEvents.SubscriptionStarted` | `subscription_started` |
| `WhisperrBillingEvents.SubscriptionRenewed` | `subscription_renewed` |
| `WhisperrBillingEvents.PaymentFailed` | `payment_failed` |
| `WhisperrBillingEvents.SubscriptionCancelled` | `subscription_cancelled` |
| `WhisperrBillingEvents.PurchaseCompleted` | `purchase_completed` |

```csharp
whisperr.Track(
    "user_8842",
    WhisperrBillingEvents.SubscriptionStarted,
    new Dictionary<string, object?>
    {
        ["product_id"] = "pro_monthly",
        ["price"] = 9.99m,          // major units, not cents
        ["currency"] = "USD",       // ISO 4217, upper case
        ["period"] = "P1M",         // ISO 8601 duration
        ["transaction_id"] = "1000000912345678",
        ["store"] = "app_store"
    });
```

> **Register these names first.** Today the server stores these events only
> after your app registers them (the Whisperr MCP does this during setup). Until
> the server accepts them without registration, an unregistered name is not
> stored.

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

## Runtime support

- **Targets:** `net8.0` and `netstandard2.0` (the latter covers .NET Framework
  4.7.2+ and other netstandard2.0 hosts). The conformance suite runs on both
  `net8.0` and `net472` in CI.
- **Long-lived hosts:** hold one client for the process lifetime. In ASP.NET
  Core the DI singleton is disposed (and flushed) on shutdown.
- **Short-lived / serverless:** the background timer cannot guarantee delivery
  before the runtime freezes — `await client.ShutdownAsync()` (or
  `FlushAsync()`) before returning so buffered events aren't lost.
- **Trimming / AOT:** standard server apps (including trimmed deployments) are
  supported. The SDK uses reflection-based `System.Text.Json`, so Native AOT is
  not a v1 guarantee; prefer the JIT/ReadyToRun runtimes for now.

## Options

| Option | Default | Notes |
|---|---:|---|
| `ApiKey` | - | App ingestion key (`wrk_...`). Required unless disabled. |
| `BaseUrl` | `https://api.whisperr.net` | Whisperr ingestion API. |
| `AppVersion` | - | Your release version. Sent as `context.app_version`. |
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
