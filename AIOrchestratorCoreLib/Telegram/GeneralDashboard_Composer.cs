namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The text of the ONE message the General topic keeps, edited in place: every open orchestration at
/// a glance, so the owner can see the whole machine without asking and without a notification per
/// update. A repeat that notifies is a waterfall, which is the thing this system exists to prevent
/// (decision 14) — the per-topic status line already works this way, and this is the same idea one
/// level up.
///
/// The BODY is not built here on purpose: it is <c>Build_ProgressReportText(null)</c>, the exact text
/// /progress already prints in General. A dashboard that formatted its own counts would be a second
/// spelling of the owner's progress bar, free to disagree with the command they check it against.
///
/// WHETHER to write is <see cref="TopicStatusLine_Decider"/>'s decision, shared unchanged with the
/// per-topic line rather than copied.
/// </summary>
public static class GeneralDashboard_Composer
{
    /// <summary>Names the message so the owner can tell it from the app's other lines at a glance.</summary>
    public const string HEADING = "📋 ALL ORCHESTRATIONS";

    /// <summary>
    /// NO CLOCK IN THE TEXT, and the omission is the design. The decider writes only when the text
    /// changed, so a timestamp would make every single tick a change: an edit every two seconds
    /// against a rate limit already on the ledger, saying nothing new. The owner reads freshness from
    /// the content, which is what actually moves.
    /// </summary>
    public static string Compose(string progressReportText)
    {
        return $"{HEADING}\n{progressReportText}";
    }
}
