using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A BUSY MEMBER IS NOT A DEAD ONE.
///
/// <para>
/// Every case here is taken from the fincanva-1 run of 2026-09-07, which produced 19 ORPHANED events
/// in three hours across five members — all of them false positives, all of them acted on. The
/// decision they came from had no test of any kind: it was the one branch in the system that touched
/// a process, and the only branch with no automated coverage.
/// </para>
/// <para>
/// The old escalation could not be tested at all — two <c>const int</c> windows read against a bare
/// <c>DateTime.UtcNow</c>, so a test would have had to wait six real minutes. The decision is a pure
/// function now, and the windows are arguments, so these run in microseconds.
/// </para>
/// </summary>
public class OrphanEscalationDeciderTests
{
    static readonly TimeSpan CONFIRM_WINDOW = TimeSpan.FromMinutes(6);
    static readonly DateTime NUDGED_AT = new(2026, 9, 7, 11, 15, 0, DateTimeKind.Utc);

    static OrphanEscalations Decide(
        bool bridgeDriven = false,
        double minutesSinceNudge = 6,
        bool midTurn = false,
        DateTime? lastActivityUtc = null)
    {
        return OrphanEscalation_Decider.Decide(
            bridgeDriven, TimeSpan.FromMinutes(minutesSinceNudge), CONFIRM_WINDOW,
            midTurn, lastActivityUtc, NUDGED_AT);
    }

    [Fact]
    public void BeforeTheWindowCloses_NothingIsDecided()
    {
        var escalation = Decide(minutesSinceNudge: 5.9);

        Assert.Equal(OrphanEscalations.NotDue, escalation);
        Assert.False(OrphanEscalation_Decider.Clears_TheClock(escalation));
        Assert.False(OrphanEscalation_Decider.Reports(escalation));
    }

    /// <summary>
    /// THE FIX FOR THE 19. A bridge-driven session has no in-session monitor for this path to find
    /// dead, and no status line to write the evidence the old test demanded — so it was condemned on
    /// an absence that could never be anything else.
    /// </summary>
    [Fact]
    public void ABridgeDrivenMember_IsNeverEscalated()
    {
        var escalation = Decide(bridgeDriven: true);

        Assert.Equal(OrphanEscalations.LeaveAlone_BridgeDriven, escalation);
        Assert.False(OrphanEscalation_Decider.Reports(escalation));
    }

    /// <summary>Asked before any evidence is weighed, so a host that cannot produce it never gets there.</summary>
    [Theory]
    [InlineData(true, null)]
    [InlineData(false, null)]
    [InlineData(false, "2026-09-07T11:00:00Z")]
    public void BridgeDriven_WinsOverEveryOtherReading(bool midTurn, string? lastActivity)
    {
        var activity = lastActivity == null ? (DateTime?)null : DateTime.Parse(lastActivity).ToUniversalTime();

        Assert.Equal(
            OrphanEscalations.LeaveAlone_BridgeDriven,
            Decide(bridgeDriven: true, midTurn: midTurn, lastActivityUtc: activity));
    }

    /// <summary>
    /// THE HEADLINE FALSE POSITIVE. imp-1 was declared ORPHANED at 11:21:01 and its turn ended
    /// successfully at 11:21:19 — eighteen seconds later, after 205 seconds of work. It was inside a
    /// long turn the whole time, which is precisely why it had not consumed its channel.
    /// </summary>
    [Fact]
    public void AMemberInsideALongTurn_IsWorking_NotDeaf()
    {
        var escalation = Decide(midTurn: true);

        Assert.Equal(OrphanEscalations.LeaveAlone_Working, escalation);
        Assert.False(OrphanEscalation_Decider.Reports(escalation));
    }

    /// <summary>
    /// NOT KNOWING IS NOT DEATH. This is the rule the probe documents for its own callers and that
    /// the old escalation broke: a null reading took the same branch as a corpse.
    /// </summary>
    [Fact]
    public void AnUnreadableMember_IsNotDeclaredDeaf()
    {
        var escalation = Decide(lastActivityUtc: null);

        Assert.Equal(OrphanEscalations.LeaveAlone_Unknown, escalation);
        Assert.False(OrphanEscalation_Decider.Reports(escalation));
    }

    [Fact]
    public void ActivityAfterTheNudge_IsWorking()
    {
        var escalation = Decide(lastActivityUtc: NUDGED_AT.AddMinutes(2));

        Assert.Equal(OrphanEscalations.LeaveAlone_Working, escalation);
    }

    /// <summary>
    /// AND THE OTHER DIRECTION STILL WORKS. Narrowing a destructive check is only honest if the case
    /// it exists for still fires — otherwise it should be deleted, not disguised.
    /// </summary>
    [Fact]
    public void AMemberThatTookNoTurnAfterItsNudge_IsReported()
    {
        var escalation = Decide(lastActivityUtc: NUDGED_AT.AddMinutes(-3));

        Assert.Equal(OrphanEscalations.ReportDeaf, escalation);
        Assert.True(OrphanEscalation_Decider.Reports(escalation));
    }

    /// <summary>A turn in flight outranks a stale timestamp: the member is demonstrably busy now.</summary>
    [Fact]
    public void AToolCallInFlight_BeatsAStaleActivityStamp()
    {
        Assert.Equal(
            OrphanEscalations.LeaveAlone_Working,
            Decide(midTurn: true, lastActivityUtc: NUDGED_AT.AddMinutes(-30)));
    }

    /// <summary>
    /// Every decided outcome retires the clock. The old loop re-weighed a member every tick, which is
    /// how it re-fed itself; only "not due yet" may leave the clock running.
    /// </summary>
    [Theory]
    [InlineData(OrphanEscalations.LeaveAlone_BridgeDriven)]
    [InlineData(OrphanEscalations.LeaveAlone_Working)]
    [InlineData(OrphanEscalations.LeaveAlone_Unknown)]
    [InlineData(OrphanEscalations.ReportDeaf)]
    public void EveryDecidedOutcome_ClearsTheClock(OrphanEscalations escalation)
    {
        Assert.True(OrphanEscalation_Decider.Clears_TheClock(escalation));
    }

    [Fact]
    public void NotDue_LeavesTheClockRunning()
    {
        Assert.False(OrphanEscalation_Decider.Clears_TheClock(OrphanEscalations.NotDue));
    }

    /// <summary>
    /// ONLY ONE OUTCOME MAY ACT. Pinned as an enumeration rather than a single case so that adding a
    /// fifth outcome later cannot quietly make it actionable.
    /// </summary>
    [Theory]
    [InlineData(OrphanEscalations.NotDue)]
    [InlineData(OrphanEscalations.LeaveAlone_BridgeDriven)]
    [InlineData(OrphanEscalations.LeaveAlone_Working)]
    [InlineData(OrphanEscalations.LeaveAlone_Unknown)]
    public void NothingButReportDeaf_EverActs(OrphanEscalations escalation)
    {
        Assert.False(OrphanEscalation_Decider.Reports(escalation));
    }

    /// <summary>
    /// After this change the common outcome is silence toward the owner. Silence with no record is
    /// indistinguishable from a detector somebody switched off, so every outcome must say something.
    /// </summary>
    [Theory]
    [InlineData(OrphanEscalations.NotDue)]
    [InlineData(OrphanEscalations.LeaveAlone_BridgeDriven)]
    [InlineData(OrphanEscalations.LeaveAlone_Working)]
    [InlineData(OrphanEscalations.LeaveAlone_Unknown)]
    [InlineData(OrphanEscalations.ReportDeaf)]
    public void EveryOutcome_NamesTheMember_AndSaysSomething(OrphanEscalations escalation)
    {
        var described = OrphanEscalation_Decider.Describe(escalation, "imp-1");

        Assert.False(string.IsNullOrWhiteSpace(described));
        Assert.Contains("imp-1", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE WHOLE INCIDENT, REPLAYED. The five real shapes from 2026-09-07, each with the outcome the
    /// app produced then and the outcome it produces now.
    /// </summary>
    [Theory]
    // imp-1 11:21:01 — mid-turn, finished successfully 18 seconds later.
    [InlineData(true, false, null)]
    // rev-2 12:00:03 — filed a coherent STANDING BY report 11 seconds later.
    [InlineData(true, false, null)]
    // rev-3 12:23:01 — correctly standing by, dependency named, nothing to say.
    [InlineData(true, false, null)]
    // imp-2 12:56:01 — inside a 660-second turn.
    [InlineData(true, true, null)]
    // rev-4 13:12:02 — turn ended successfully 92 seconds earlier; no usage file to prove it.
    [InlineData(true, false, null)]
    public void TheNineteenFalsePositives_AreAllLeftAlone(bool bridgeDriven, bool midTurn, string? lastActivity)
    {
        var activity = lastActivity == null ? (DateTime?)null : DateTime.Parse(lastActivity).ToUniversalTime();

        Assert.False(OrphanEscalation_Decider.Reports(
            Decide(bridgeDriven: bridgeDriven, midTurn: midTurn, lastActivityUtc: activity)));
    }
}
