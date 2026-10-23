# Changelog

## 1.2.0 - 2026-10-23

- New `WhisperrClient.Catalog` for catalog push (whisperr-go catalog v2):
  `UpsertAsync`, `DeleteAsync` (by ids with one `deletedAt`, or by
  `CatalogDeletion`), `BeginSnapshotAsync`, `CompleteSnapshotAsync`,
  `HeartbeatAsync` and `StatusAsync`. Server key only: a `wpk_` key throws
  `WhisperrConfigurationException` before any request. Calls are sent right
  away, not queued.
- Upserts are chunked at 500 items and 2 MiB, deletes at 1000. Rejected items
  come back with their index in the list you passed. A 413 splits the chunk.
- 429, 5xx, timeouts and network errors are retried with backoff, honoring
  `Retry-After`. Retries resend the same body. Refusals and exhausted retries
  throw `WhisperrCatalogException` (`Status`, `Code`, `Retryable`, `Partial`).
- `CatalogItem` and related types with the wire names; times are sent as UTC
  RFC 3339. `WhisperrCatalogClient` for a catalog client on its own. New
  options `CatalogMaxRetries` and `CatalogRequestTimeout`.
- `WhisperrMoney.ToMinorUnits(amount, currency)` and `CurrencyExponent` with
  the ISO 4217 exponents the server uses. They throw instead of rounding.
- Tests run `Fixtures/catalog_push.json`, a byte-identical copy of
  whisperr-spec `conformance/catalog_push.json` (`WHISPERR_CATALOG_SPEC_PATH`
  runs a spec checkout's file and checks the copy has not drifted). It pins
  error bodies as the server sends them (`{"error":{"code","message"}}`), the
  409 `snapshot_open` heartbeat refusal, the 404 feature-detect, the 413
  split, and `item_too_large` as the one client-side reject code.

## 1.1.0 - 2026-10-12

- Every event now carries `context.sdk_name` (`whisperr-dotnet`) and
  `context.sdk_version` (the package version).
- New `WhisperrOptions.AppVersion` (configuration key `Whisperr:AppVersion`).
  When set, every event carries it as `context.app_version`. Not set: no key.
- A key that the caller passes in `Track` context wins over these SDK keys.
  `$message_id` is always the SDK value.
- New `WhisperrBillingEvents` constants for the reserved billing fallback
  events (playbook engine spec section 3.8): `trial_started`,
  `trial_converted`, `trial_auto_renew_disabled`, `membership_started`,
  `renewal_succeeded`, `auto_renew_disabled`, `auto_renew_enabled`,
  `membership_ended`, `billing_issue_started`, `billing_recovered`,
  `refund_granted`. Money properties: `plan_id`, `price`, `currency`,
  `plan_period`, `transaction_id`, `store`.
- README: the `app_language` trait, the context keys, and billing events.

## 1.0.0

- First stable release.
- `Identify` now forwards to the server like the other Whisperr SDKs instead of
  discarding the whole call when a channel is invalid: only an unrepresentable
  channel (undefined type or missing address) or `preferred_channel` is dropped,
  and valid traits/channels still send.
- Adds the `Whisperr.AspNetCore` companion package: `AddWhisperr(...)` DI
  registration with configuration/`WHISPERR_API_KEY` binding, plus optional
  `UseWhisperr()` request-scoped tracking.
- Adds SourceLink and a symbol (`.snupkg`) package for debugging.
- CI now runs the conformance suite on both `net8.0` (Linux) and `net472`
  (Windows) to exercise the `netstandard2.0` consumer surface.
- Release version is derived from the published `v*.*.*` tag.

## 0.1.0

- Initial .NET SDK for Whisperr ingestion.
- Adds server-side `WhisperrClient` with in-memory queueing, batching, retry,
  auth retention, permanent-error drops, and stable `$message_id` idempotency.
- Adds spec-driven wire and behavior conformance tests against `whisperr-spec`.
- Adds NuGet package metadata and GitHub Actions for CI/package publishing.
