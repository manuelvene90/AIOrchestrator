using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// The repetition judgement `hook-log.sh` says belongs to the app, and which the app did not have:
/// three identical guard alerts in twelve minutes on 2026-08-13, one per marker, no comparison of any
/// kind between them.
///
/// Every case below is a rule that had to survive the fix for the OTHER alert in the same branch: the
/// key must not embed anything that moves on its own, and the suppression must re-arm.
/// </summary>
public class GuardReportDeciderTests
{
    static readonly DateTime NOW = new(2026, 8, 13, 18, 30, 0);

    const string MARKER = "reviewer-readonly-check.sh\nwhether this command mutates anything\na redirect target this scanner cannot resolve\n";

    /// <summary>Nothing reported yet is always news — the first marker is the whole point.</summary>
    [Fact]
    public void AFirstMarkerIsAlwaysReported()
    {
        Assert.True(GuardReport_Decider.Should_Report(MARKER, lastReportedText: null, lastReportedAt: null, NOW));
    }

    /// <summary>The defect: the same inability, a minute later, is not a second fact.</summary>
    [Fact]
    public void TheSameInabilityAMinuteLaterIsNotReportedAgain()
    {
        Assert.False(GuardReport_Decider.Should_Report(MARKER, MARKER, NOW.AddMinutes(-1), NOW));
    }

    /// <summary>
    /// A DIFFERENT inability is news immediately, however soon it arrives. This is what separates the
    /// fix from "one alert per orchestration": a second guard failing for a second reason must not be
    /// swallowed by the first one's cooldown.
    /// </summary>
    [Fact]
    public void ADifferentInabilityIsReportedImmediately()
    {
        var other = "supervisor-awaiting-answer-check.sh\nwhich tool is being called\nno tool name could be extracted from the payload\n";

        Assert.True(GuardReport_Decider.Should_Report(other, MARKER, NOW.AddSeconds(-5), NOW));
    }

    /// <summary>
    /// It RE-ARMS. A permanent latch is the budget alert's defect — once per app lifetime, silent
    /// forever after — and a guard that degrades again tomorrow is news again tomorrow.
    /// </summary>
    [Fact]
    public void TheSameInabilityIsReportedAgainAfterTheInterval()
    {
        Assert.True(GuardReport_Decider.Should_Report(
            MARKER, MARKER, NOW.AddMinutes(-GuardReport_Decider.REPEAT_AFTER_MINUTES), NOW));
    }

    /// <summary>
    /// A stamp in the future means the clock is untrustworthy, and an untrusted clock must never be
    /// the reason an alarm stays quiet — the alternative is silence until real time catches up.
    /// </summary>
    [Fact]
    public void AFutureStampNeverBuysSilence()
    {
        Assert.True(GuardReport_Decider.Should_Report(MARKER, MARKER, NOW.AddHours(2), NOW));
    }

    /// <summary>
    /// Two spellings of one inability are one inability. bash writes \n; python3 on this machine adds
    /// \r silently. Without this the same fact from two writers reads as two facts and defeats the
    /// dedup exactly as the rendered duration did in the idle flag.
    /// </summary>
    [Fact]
    public void LineEndingsDoNotMakeANewFact()
    {
        var withCarriageReturns = MARKER.Replace("\n", "\r\n");

        Assert.False(GuardReport_Decider.Should_Report(withCarriageReturns, MARKER, NOW.AddMinutes(-1), NOW));
    }

    /// <summary>
    /// The key carries the hook, the predicate AND the reason — all three, so a second hook failing on
    /// the same predicate is still a distinct fact. Asserted positively rather than by "the keys
    /// differ", which any hashing of any subset would satisfy.
    /// </summary>
    [Fact]
    public void TheKeyCarriesHookPredicateAndReason()
    {
        var key = GuardReport_Decider.Build_Key(MARKER);

        Assert.Contains("reviewer-readonly-check.sh", key);
        Assert.Contains("whether this command mutates anything", key);
        Assert.Contains("a redirect target this scanner cannot resolve", key);
    }
}
