namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// Whether a guard-not-in-force marker is worth reporting AGAIN.
///
/// `hook-log.sh` overwrites one marker file rather than appending, and its own comment says why:
/// "repeating the same inability a hundred times says nothing the first one did not, and the app is
/// where any judgement about repetition belongs". The app had no such judgement. Every marker it
/// found became an entry — three in twelve minutes on 2026-08-13, identical but for nothing at all.
///
/// The key is the marker's CONTENT, which is the closest thing to a cause this system has: same hook,
/// same predicate, same reason. It deliberately does NOT embed a duration or a timestamp — that is
/// the mistake that ran for six hours in the idle flag, where a rendered clock inside the key meant
/// the key never matched itself.
///
/// AND IT RE-ARMS. A permanent latch would be the budget alert's defect, which fires once per
/// orchestration per app lifetime and is silent forever after — a guard that stops working, gets
/// reported, gets fixed and stops working again a day later is news the second time too.
/// </summary>
public static class GuardReport_Decider
{
    /// <summary>
    /// How long the same inability stays old news. Public because the repeat interval is the thing
    /// worth asserting, and a private const cannot be named by a test.
    ///
    /// Thirty minutes: this reports a CONDITION rather than an event — while a guard is degraded
    /// every call trips it — so the interval is about how often a standing fact is worth restating,
    /// not about how fast it is detected.
    /// </summary>
    public const int REPEAT_AFTER_MINUTES = 30;

    public static bool Should_Report(string markerText, string? lastReportedText, DateTime? lastReportedAt, DateTime now)
    {
        if (lastReportedText == null || lastReportedAt == null)
            return true;

        if (Build_Key(markerText) != Build_Key(lastReportedText))
            return true;

        // A stamp in the FUTURE means the clock cannot be trusted, and an untrusted clock must never
        // be the reason an alarm stays silent — it would suppress this until real time caught up.
        // The repo's own rule, from the stamp that blanked a member's time-on-task: an unreadable
        // clock never argues for the quieter outcome.
        if (lastReportedAt.Value > now)
            return true;

        return (now - lastReportedAt.Value).TotalMinutes >= REPEAT_AFTER_MINUTES;
    }

    /// <summary>
    /// Line endings are normalised away before comparing. The same fact reaches this file written by
    /// different writers — bash writes \n, and python3 on this machine silently adds \r — and two
    /// spellings of one inability must not read as two inabilities.
    /// </summary>
    public static string Build_Key(string markerText)
    {
        return markerText.Replace("\r", "").Trim();
    }
}
