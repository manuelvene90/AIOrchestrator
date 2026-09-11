namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>What a tap on an option button turned out to be.</summary>
public enum TapOutcomes
{
    /// <summary>Not a question option at all — it belongs to another button family, or to nothing.</summary>
    NotOurs,

    /// <summary>Well-formed, but no decision is holding this nonce: already consumed, or long gone.</summary>
    Unknown,

    /// <summary>Found, and past its expiry. Refused, and said out loud.</summary>
    Expired,

    /// <summary>Found and live: this is the owner's answer.</summary>
    Accepted,

    /// <summary>Found, live, and high risk: the code has to be typed back before anything happens.</summary>
    NeedsConfirmation,
}

/// <summary>
/// Whether a tap arriving now may take the decision it points at.
///
/// <para>
/// FOUR REFUSALS, ALL NAMED. The old handler had one — a lookup miss answered "expired — please type
/// your choice" whether the button had been consumed a second earlier, evicted by the registry cap,
/// or invented. That message is the only thing the owner sees, so a payload nobody ever issued and a
/// question they answered thirty seconds ago read identically to them and identically in the log.
/// Telling them apart is what makes a REUSED token something the app can report as a security event
/// rather than a shrug.
/// </para>
/// <para>
/// EXPIRY IS CHECKED BEFORE RISK. A high-risk button past its deadline must be refused, not escalated
/// to a code prompt: offering the second gesture on a lapsed decision would let a stale keyboard
/// restart an approval flow for an operation whose moment has passed.
/// </para>
/// </summary>
public static class PendingDecision_Gate
{
    /// <summary>
    /// <paramref name="found"/> is whether the nonce is currently registered. The record's expiry
    /// and risk are passed rather than the record itself so this stays a pure rule with no reference
    /// to the engine's storage.
    /// </summary>
    public static TapOutcomes Classify(
        string? callbackData,
        bool found,
        DateTime expiresUtc,
        bool isHighRisk,
        DateTime nowUtc)
    {
        if (Telegram.CallbackToken.Parse_OrNull(callbackData) == null)
            return TapOutcomes.NotOurs;

        if (!found)
            return TapOutcomes.Unknown;

        // >= so the button dies at its own instant. See TelegramAttempt_Gate.Is_AttemptDue.
        if (nowUtc >= expiresUtc)
            return TapOutcomes.Expired;

        return isHighRisk ? TapOutcomes.NeedsConfirmation : TapOutcomes.Accepted;
    }

    /// <summary>
    /// What the owner is told in the callback toast — the ten-second answer Telegram requires before
    /// any work is done, or the spinner hangs on their phone.
    /// </summary>
    public static string Describe_ForOwner(TapOutcomes outcome)
    {
        return outcome switch
        {
            TapOutcomes.Accepted => "✓",
            TapOutcomes.NeedsConfirmation => "🔐 type the code shown",
            TapOutcomes.Expired => "expired — please type your choice",
            _ => "no longer open — please type your choice",
        };
    }
}
