using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Status.SessionContextUsage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// WHO gets their context percentage shown, and at what. The owner gave the rule with the request
/// on 2026-08-21: the session they talk to always, the ones they do not talk to only when nearly
/// full — 90% on the Telegram status line, 80% in the half-hourly digest.
///
/// These tests exist because the rule was inline at three call sites first, and the first draft had
/// already drifted: one site spelled the solo case out as two id literals, and the other two did not
/// have it at all — so a solo's context vanished from the digest the moment it dropped below 80%.
/// </summary>
public class ContextVisibilityPolicyTests
{
    static readonly DateTime PROBED = new(2026, 8, 21, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheSupervisorIsAlwaysShown_EvenNearlyEmpty()
    {
        Assert.True(ContextVisibility_Policy.Show_Supervisor(Reading(3)));
    }

    /// <summary>Null is UNKNOWN, and unknown shows nothing — never a 0% that reads as an empty window.</summary>
    [Fact]
    public void NoReadingIsNeverShown_ForAnyone()
    {
        Assert.False(ContextVisibility_Policy.Show_Supervisor(null));
        Assert.False(ContextVisibility_Policy.Show_Member_OnStatusLine("solo-1", null));
        Assert.False(ContextVisibility_Policy.Show_Member_OnStatusLine("imp-1", null));
        Assert.False(ContextVisibility_Policy.Show_Member_InPeriodicDigest("imp-1", null));
    }

    /// <summary>
    /// THE SOLO IS NOT A MEMBER LIKE THE OTHERS — it is the session the owner is talking to, so it
    /// takes the supervisor's always-shown rule on BOTH surfaces and never the threshold. This is
    /// the case the first draft got wrong.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(52)]
    [InlineData(99)]
    public void ASoloIsAlwaysShownOnEverySurface(double percent)
    {
        Assert.True(ContextVisibility_Policy.Show_Member_OnStatusLine("solo-1", Reading(percent)));
        Assert.True(ContextVisibility_Policy.Show_Member_InPeriodicDigest("solo-1", Reading(percent)));
    }

    [Theory]
    [InlineData("imp-1")]
    [InlineData("rev-2")]
    public void AnImplementerOrReviewerIsQuietUntilItIsNearlyFull(string memberId)
    {
        Assert.False(ContextVisibility_Policy.Show_Member_OnStatusLine(memberId, Reading(89)));
        Assert.True(ContextVisibility_Policy.Show_Member_OnStatusLine(memberId, Reading(90)));
        Assert.True(ContextVisibility_Policy.Show_Member_OnStatusLine(memberId, Reading(97)));
    }

    /// <summary>
    /// The digest starts EARLIER than the status line, and the gap is the point: it is the surface
    /// with room to warn while the owner can still do something about it. A member at 85% belongs in
    /// one and not the other — if this test ever reads the same on both, the two thresholds have
    /// been collapsed into one and the early warning is gone.
    /// </summary>
    [Fact]
    public void TheDigestWarnsEarlierThanTheStatusLine()
    {
        Assert.True(ContextVisibility_Policy.Show_Member_InPeriodicDigest("imp-1", Reading(85)));
        Assert.False(ContextVisibility_Policy.Show_Member_OnStatusLine("imp-1", Reading(85)));
    }

    /// <summary>
    /// The thresholds are compared INCLUSIVELY, and that is a decision rather than an accident: the
    /// probe reports whole percentages, so 90 is a number sessions really sit at, and the owner
    /// naming "above 90%" meant the point where it matters and not the integer after it.
    /// </summary>
    [Fact]
    public void TheThresholdItselfCounts()
    {
        Assert.True(ContextVisibility_Policy.Show_Member_OnStatusLine("imp-1", Reading(ContextVisibility_Policy.STATUS_LINE_MEMBER_PERCENT)));
        Assert.True(ContextVisibility_Policy.Show_Member_InPeriodicDigest("imp-1", Reading(ContextVisibility_Policy.PERIODIC_DIGEST_MEMBER_PERCENT)));
    }

    static ISessionContextUsage Reading(double percent)
    {
        return SessionContextUsage_Factory.Create(percent, PROBED);
    }
}
