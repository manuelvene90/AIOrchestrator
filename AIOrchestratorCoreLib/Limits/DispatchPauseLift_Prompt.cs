namespace AIOrchestratorCoreLib.Limits;

/// <summary>
/// The words around the owner's lever on the dispatch pause: the offer under the pause alert, the
/// offer an agent's <c>clear-dispatch-pause</c> request raises, and what the message becomes after
/// the tap. Pure, so the engine only wires it.
///
/// <para>
/// A lift is also a statement about the ACCOUNT: the owner is saying "the reading this pause was
/// decided on is not my account's reading any more" — so a lift sets a cutoff, and every probe file
/// written before it stops being evidence (see <c>LimitProbeCutoffUtc</c>). The brake is not
/// bypassed: the next tick re-decides from the readings written after the lift, and if the account
/// really is over, it pauses again within the minute and says so.
/// </para>
/// </summary>
public static class DispatchPauseLift_Prompt
{
    public const string LIFT_LABEL = "Lift the pause";
    public const string KEEP_LABEL = "Keep it";

    /// <summary>An offer older than this is a stale question; the tap is refused and the owner is pointed at the command.</summary>
    public const int OFFER_EXPIRY_HOURS = 12;

    public const string COMMAND_HINT = "/resume_dispatch does the same without a button.";

    /// <summary>The line under the pause alert itself.</summary>
    public static string Build_AlertOffer()
    {
        return $"Account swapped, or the reading is wrong? Tap {LIFT_LABEL}: readings written before the tap stop counting, and the next tick re-decides from the live ones. {COMMAND_HINT}";
    }

    /// <summary>The message an agent's request raises.</summary>
    public static string Build_RequestOffer(string requester, string reason, string resumeInstantText)
    {
        return $"⏸ {requester} asks to lift the dispatch pause (resuming {resumeInstantText}) — \"{reason}\".\n\n{Build_AlertOffer()}";
    }

    public static string Describe_Lifted(string cutoffInstantText)
    {
        return $"▶ Pause lifted by the owner. Readings written before {cutoffInstantText} are ignored; the next tick re-decides from live ones.";
    }

    public static string Describe_Kept()
    {
        return "⏸ Pause kept by the owner.";
    }

    public static string Describe_ExpiredOrUnknownTap()
    {
        return $"This button is from an earlier pause or a restart — {COMMAND_HINT}";
    }

    public static bool Is_Expired(DateTime askedUtc, DateTime nowUtc)
    {
        return nowUtc - askedUtc > TimeSpan.FromHours(OFFER_EXPIRY_HOURS);
    }
}
