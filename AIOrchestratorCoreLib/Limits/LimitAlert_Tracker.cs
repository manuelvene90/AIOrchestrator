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

    public static bool Should_ResetWindow(double currentPercent, double lastAlertedThreshold)
    {
        return lastAlertedThreshold > 0 && currentPercent < RESET_FLOOR_PERCENT;
    }
}
