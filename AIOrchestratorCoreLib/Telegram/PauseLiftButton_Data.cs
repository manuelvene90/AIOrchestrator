namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The two buttons under a dispatch-pause offer — "Lift the pause" and "Keep it" — as callback
/// payloads. A NONCE, not a stateless value: lifting the pause is a one-shot decision on the bill,
/// so a tap must match an offer the app made and remembers, and a second tap on the same button
/// must find nothing. The prefixes share no head with <see cref="ModelEffortButton_Data"/>'s
/// <c>model:</c>/<c>effort:</c>, the hold's <c>hold:</c>, the bar's <c>cmd:</c>, the close pair's
/// <c>close-yes-</c>/<c>close-no-</c> or the generic <c>opt-</c>, so parse order cannot decide a meaning.
/// </summary>
public static class PauseLiftButton_Data
{
    public const string LIFT_PREFIX = "pause-lift:";
    public const string KEEP_PREFIX = "pause-keep:";

    public static string Build_Lift(string nonce) => LIFT_PREFIX + nonce;

    public static string Build_Keep(string nonce) => KEEP_PREFIX + nonce;

    /// <summary>Null for any payload that is not ours — the caller must then fall through untouched.</summary>
    public static (bool Lifts, string Nonce)? Parse_OrNull(string? callbackData)
    {
        if (string.IsNullOrEmpty(callbackData))
            return null;

        if (callbackData.StartsWith(LIFT_PREFIX, StringComparison.Ordinal))
            return (true, callbackData[LIFT_PREFIX.Length..]);

        if (callbackData.StartsWith(KEEP_PREFIX, StringComparison.Ordinal))
            return (false, callbackData[KEEP_PREFIX.Length..]);

        return null;
    }
}
