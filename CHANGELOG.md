# Changelog

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
