# Changelog

## 0.1.0

- Initial .NET SDK for Whisperr ingestion.
- Adds server-side `WhisperrClient` with in-memory queueing, batching, retry,
  auth retention, permanent-error drops, and stable `$message_id` idempotency.
- Adds spec-driven wire and behavior conformance tests against `whisperr-spec`.
- Adds NuGet package metadata and GitHub Actions for CI/package publishing.
