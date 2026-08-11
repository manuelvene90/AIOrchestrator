namespace AIOrchestratorCoreLib.Limits;

/// <summary>
/// Decides which alert threshold (90/95/97/98/99/100) a limit just crossed, deduplicating per
/// window: a percentage falling back below the reset floor means a new limit window started and
/// alerts re-arm. Pure decision logic — persistence and delivery live with the caller.
/// </summary>
public static class LimitAlert_Tracker
{
    public static readonly IReadOnlyList<double> ALERT_THRESHOLDS = [90, 95, 97, 98, 99, 100];

    /// <summary>Below this the previous window's alert state is cleared (limits reset).</summary>
    public const double RESET_FLOOR_PERCENT = 50;

    /// <summary>
    /// Returns the highest newly-crossed threshold, or null if nothing new. lastAlerted is the
    /// highest threshold already alerted this window (0 = none).
    /// </summary>
    public static double? Get_NewlyCrossedThreshold_OrNull(double currentPercent, double lastAlertedThreshold)
    {
        double? newlyCrossed = null;

        foreach (var threshold in ALERT_THRESHOLDS)
        {
            if (currentPercent >= threshold && threshold > lastAlertedThreshold)
                newlyCrossed = threshold;
        }

        return newlyCrossed;
    }

    /// <summary>
    /// The pre-identity re-arm rule, now a FALLBACK rather than the mechanism: it only decides
    /// anything when neither the stored nor the current window can be identified. See
    /// <see cref="Resolve_LastAlertedThreshold_ForCurrentWindow"/>.
    /// </summary>
    public static bool Should_ResetWindow(double currentPercent, double lastAlertedThreshold)
    {
        return lastAlertedThreshold > 0 && currentPercent < RESET_FLOOR_PERCENT;
    }

    /// <summary>
    /// Which threshold still counts as "already alerted" for the window being looked at now.
    ///
    /// IDENTITY decides it: a different window means the previous window's alerts are spent, so the
    /// new one starts un-alerted. That is what makes a latch stored against an UNKNOWN window — the
    /// whole pre-identity state file — re-arm on sight instead of suppressing everything beneath
    /// it. Observed 2026-08-11: a stored 100 silenced every threshold for a weekly window sitting
    /// at 89% and climbing, because <see cref="Get_NewlyCrossedThreshold_OrNull"/> can only look
    /// for something ABOVE the latch, and nothing is above 100.
    ///
    /// The re-arm is deliberately ASYMMETRIC. It fires only when the CURRENT window is positively
    /// identified: an unknown stored window loses to a known current one (that is the migration),
    /// but a payload that merely STOPS reporting identity never drops a latch we legitimately hold.
    /// Getting that backwards costs a re-alert on every check for as long as the field is missing —
    /// a waterfall, and the more expensive way to be wrong than one late alert.
    ///
    /// When the current window cannot be identified the payload carries no reset field at all, and
    /// the percentage rule is the only signal left — so it degrades to exactly the old behaviour
    /// rather than to silence.
    /// </summary>
    public static double Resolve_LastAlertedThreshold_ForCurrentWindow(
        double storedThreshold,
        DateTime? storedWindowIdentity,
        DateTime? currentWindowIdentity,
        double currentPercent)
    {
        // Identified window: identity is the ONLY re-arm signal. A dip inside one window is noise —
        // usage within a window is cumulative — so the floor below must not get a say here.
        if (currentWindowIdentity != null && WindowInstance_Order.Is_SameInstance(currentWindowIdentity, storedWindowIdentity))
            return storedThreshold;

        if (currentWindowIdentity != null)
            return 0;

        if (Should_ResetWindow(currentPercent, storedThreshold))
            return 0;

        return storedThreshold;
    }
}
