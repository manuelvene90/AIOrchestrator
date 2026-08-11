using AIOrchestratorCoreLib.Limits;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Limits;

/// <summary>
/// The alert latch is de-duplicated per limit WINDOW, and the whole defect class is getting the
/// window wrong. Observed 2026-08-11: `.limit-alerts.json` held
/// <c>{"rate_limits.seven_day.used_percentage":100}</c> with no record of WHICH window earned that
/// 100. The weekly window then sat at 89% and climbing with every threshold silenced, because
/// <see cref="LimitAlert_Tracker.Get_NewlyCrossedThreshold_OrNull"/> only looks for a threshold
/// ABOVE the latch and nothing is above 100 — and the old re-arm rule needed a drop below 50%,
/// which a window at 89% will never do before it resets.
/// </summary>
public class LimitAlertIdentityTests
{
    const double WEEKLY_WINDOW = 1786953600;
    const double NEXT_WEEKLY_WINDOW = 1787558400;

    /// <summary>
    /// THE MIGRATION CASE, and the failure it has to rule out: the old file's 100 must NOT carry
    /// forward. A latch whose window cannot be named is not evidence about the window we are
    /// looking at now, so it re-arms rather than suppressing.
    /// </summary>
    [Fact]
    public void PreIdentityLatch_ReArmsAgainstAKnownWindow_RatherThanCarryingItsThresholdForward()
    {
        var lastAlerted = LimitAlert_Tracker.Resolve_LastAlertedThreshold_ForCurrentWindow(
            storedThreshold: 100,
            storedWindowIdentity: null,
            currentWindowIdentity: WEEKLY_WINDOW,
            currentPercent: 89);

        Assert.Equal(0, lastAlerted);

        // And the owner hears NOTHING at migration: 89% has not crossed 90, so re-arming is silent.
        Assert.Null(LimitAlert_Tracker.Get_NewlyCrossedThreshold_OrNull(89, lastAlerted));
    }

    /// <summary>
    /// The point of the whole change: once re-armed, the genuine 90% crossing is reported. Under
    /// the old state this alert could never fire before the window reset on 2026-08-17.
    /// </summary>
    [Fact]
    public void AfterMigration_TheGenuineCrossingIsReported()
    {
        var lastAlerted = LimitAlert_Tracker.Resolve_LastAlertedThreshold_ForCurrentWindow(100, null, WEEKLY_WINDOW, 90);

        Assert.Equal(90, LimitAlert_Tracker.Get_NewlyCrossedThreshold_OrNull(90, lastAlerted));
    }

    /// <summary>
    /// A re-armed window already near the ceiling emits ONE line, not one per threshold — the
    /// waterfall this system exists to prevent. Get_NewlyCrossedThreshold_OrNull returns the
    /// HIGHEST crossed threshold, so 97% is a single "97%", never 90 + 95 + 97.
    /// </summary>
    [Fact]
    public void ARearmedWindowNearTheCeiling_EmitsOneAlert_NotABurst()
    {
        var lastAlerted = LimitAlert_Tracker.Resolve_LastAlertedThreshold_ForCurrentWindow(100, null, WEEKLY_WINDOW, 97);

        Assert.Equal(97, LimitAlert_Tracker.Get_NewlyCrossedThreshold_OrNull(97, lastAlerted));
    }

    /// <summary>A NEW window re-arms on identity alone — no dip below any floor is required.</summary>
    [Fact]
    public void ANewWindowReArms_OnIdentityAlone_WithoutAnyPercentageDip()
    {
        var lastAlerted = LimitAlert_Tracker.Resolve_LastAlertedThreshold_ForCurrentWindow(
            storedThreshold: 100,
            storedWindowIdentity: WEEKLY_WINDOW,
            currentWindowIdentity: NEXT_WEEKLY_WINDOW,
            currentPercent: 95);

        Assert.Equal(0, lastAlerted);
        Assert.Equal(95, LimitAlert_Tracker.Get_NewlyCrossedThreshold_OrNull(95, lastAlerted));
    }

    /// <summary>The SAME window does not re-fire what it has already said, however often it is checked.</summary>
    [Fact]
    public void TheSameWindowDoesNotReFire()
    {
        var lastAlerted = LimitAlert_Tracker.Resolve_LastAlertedThreshold_ForCurrentWindow(95, WEEKLY_WINDOW, WEEKLY_WINDOW, 96);

        Assert.Equal(95, lastAlerted);
        Assert.Null(LimitAlert_Tracker.Get_NewlyCrossedThreshold_OrNull(96, lastAlerted));
    }

    /// <summary>
    /// Within one window a reading that dips is noise, not a reset — usage inside a window is
    /// cumulative. The old floor rule must NOT re-arm here, or a 100→49 wobble would re-alert.
    /// </summary>
    [Fact]
    public void TheSameWindowDoesNotReArm_EvenOnAReadingBelowTheOldFloor()
    {
        Assert.Equal(100, LimitAlert_Tracker.Resolve_LastAlertedThreshold_ForCurrentWindow(100, WEEKLY_WINDOW, WEEKLY_WINDOW, 20));
    }

    /// <summary>
    /// DEGRADATION: a status line with no reset field anywhere leaves both windows unidentifiable.
    /// Identity cannot decide anything, so the old percentage rule still applies — the fallback is
    /// today's behaviour, never silence.
    /// </summary>
    [Fact]
    public void WithNoIdentityAnywhere_ItDegradesToTheOldPercentageRule_NotToSilence()
    {
        // Below the floor: the old rule calls that a new window, and it still does.
        Assert.Equal(0, LimitAlert_Tracker.Resolve_LastAlertedThreshold_ForCurrentWindow(100, null, null, 20));

        // Above the floor: still the same window, still latched, exactly as before.
        Assert.Equal(100, LimitAlert_Tracker.Resolve_LastAlertedThreshold_ForCurrentWindow(100, null, null, 89));
    }

    /// <summary>
    /// A payload that stops reporting the reset field must not drop a latch we legitimately hold —
    /// that direction would re-alert on every check. Only a KNOWN different window re-arms.
    /// </summary>
    [Fact]
    public void AKnownLatchSurvivesAPayloadThatStopsReportingIdentity()
    {
        Assert.Equal(95, LimitAlert_Tracker.Resolve_LastAlertedThreshold_ForCurrentWindow(95, WEEKLY_WINDOW, null, 96));
    }

    /// <summary>An unidentifiable reading never displaces an identifiable one — it cannot be shown to be current.</summary>
    [Theory]
    [InlineData(null, null, 0)]
    [InlineData(null, 5.0, -1)]
    [InlineData(5.0, null, 1)]
    [InlineData(6.0, 5.0, 1)]
    [InlineData(5.0, 6.0, -1)]
    [InlineData(5.0, 5.0, 0)]
    public void Compare_Instance_OrdersByWhichWindowIsNewer(double? candidate, double? known, int expected)
    {
        Assert.Equal(expected, WindowInstance_Order.Compare_Instance(candidate, known));
    }
}
