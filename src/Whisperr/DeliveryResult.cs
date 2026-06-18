namespace Whisperr;

internal enum DeliveryResult
{
    Ok,
    Retry,
    Auth,
    Drop,
    RetryExhausted
}
