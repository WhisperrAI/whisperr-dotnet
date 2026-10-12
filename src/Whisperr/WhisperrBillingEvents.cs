namespace Whisperr;

/// <summary>
/// Event names the Whisperr spec reserves for membership billing (the
/// playbook engine spec, section 3.8). Send them from your backend with
/// <see cref="WhisperrClient.Track"/> when Whisperr has no billing source
/// connected (for example a store webhook handler). Never send them from the front end. Today the server stores these
/// events only after the app registers them.
/// </summary>
/// <remarks>
/// Money properties, where they apply: <c>plan_id</c>, <c>price</c> (number,
/// major units, for example <c>9.99</c>), <c>currency</c> (ISO 4217, upper case),
/// <c>plan_period</c> (ISO 8601, for example <c>P1M</c>), <c>transaction_id</c>,
/// <c>store</c>.
/// </remarks>
public static class WhisperrBillingEvents
{
    /// <summary>A free trial starts.</summary>
    public const string TrialStarted = "trial_started";

    /// <summary>The trial converts to the first paid period.</summary>
    public const string TrialConverted = "trial_converted";

    /// <summary>The user cancels during the trial.</summary>
    public const string TrialAutoRenewDisabled = "trial_auto_renew_disabled";

    /// <summary>A paid membership starts (never a renewal or a plan change).</summary>
    public const string MembershipStarted = "membership_started";

    /// <summary>A paid period renews.</summary>
    public const string RenewalSucceeded = "renewal_succeeded";

    /// <summary>The user cancels; access continues to the period end.</summary>
    public const string AutoRenewDisabled = "auto_renew_disabled";

    /// <summary>The user turns auto-renew back on.</summary>
    public const string AutoRenewEnabled = "auto_renew_enabled";

    /// <summary>Access ends.</summary>
    public const string MembershipEnded = "membership_ended";

    /// <summary>A payment fails and a billing issue starts.</summary>
    public const string BillingIssueStarted = "billing_issue_started";

    /// <summary>The billing issue is resolved.</summary>
    public const string BillingRecovered = "billing_recovered";

    /// <summary>The store grants a refund.</summary>
    public const string RefundGranted = "refund_granted";
}
