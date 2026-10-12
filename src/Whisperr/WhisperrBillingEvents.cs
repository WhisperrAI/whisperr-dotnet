namespace Whisperr;

/// <summary>
/// Event names the Whisperr spec reserves for subscription billing. Send them
/// with <see cref="WhisperrClient.Track"/> when your own backend sees the billing
/// change (for example a store webhook) and Whisperr has no billing source
/// connected. Today the server stores these events only after the app
/// registers them.
/// </summary>
/// <remarks>
/// Properties the spec defines (all optional): <c>product_id</c>,
/// <c>price</c> (number, major units, for example <c>9.99</c>),
/// <c>currency</c> (ISO 4217, upper case), <c>period</c> (ISO 8601, for example
/// <c>P1M</c>), <c>transaction_id</c>, <c>store</c>.
/// </remarks>
public static class WhisperrBillingEvents
{
    /// <summary>A free trial started.</summary>
    public const string TrialStarted = "trial_started";

    /// <summary>A paid subscription started.</summary>
    public const string SubscriptionStarted = "subscription_started";

    /// <summary>A subscription renewed for a new period.</summary>
    public const string SubscriptionRenewed = "subscription_renewed";

    /// <summary>A payment failed.</summary>
    public const string PaymentFailed = "payment_failed";

    /// <summary>A subscription was cancelled.</summary>
    public const string SubscriptionCancelled = "subscription_cancelled";

    /// <summary>A one-time purchase completed.</summary>
    public const string PurchaseCompleted = "purchase_completed";
}
