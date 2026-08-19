using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE NIGHT OF 2026-08-18/19, pinned. Away mode is the machinery that exists to stop the owner
/// being talked at, and it was the thing doing the talking: the 30-minute digest is appended to the
/// owner CHANNEL, an append is what a session's watcher fires on, and the woken session's
/// STANDING BY re-armed both alert paths. `strategy-lab-4` locked to an exact 30-minute period and
/// the owner woke to about 33 messages.
///
/// Their ruling, asked as a two-option question and answered 2026-08-19: while away, a digest goes
/// out ONLY when something changed.
/// </summary>
public class AwayDigestDeciderTests
{
    /// <summary>
    /// THE REGRESSION ITSELF, in the owner's own overnight text. A solo session with nothing to do
    /// renders the same line every slot; sending it is what woke the session that drove the loop.
    /// </summary>
    [Fact]
    public void TheSameDigestTwiceIsNotSentAgain()
    {
        Assert.False(
            AwayDigest_Decider.Should_Send("🌙 solo-1: inactive", "🌙 solo-1: inactive"),
            "an unchanged away digest was sent again — this is the append that wakes the session and drives the 30-minute loop");
    }

    [Fact]
    public void ADigestThatSaysSomethingNewIsSent()
    {
        Assert.True(AwayDigest_Decider.Should_Send("🌙 solo-1: inactive", "🌙 solo-1: writing window open"));
    }

    /// <summary>
    /// The FIRST digest of an away spell. AWAY MODE ON has just promised them a 3-line update, so
    /// one snapshot of what they are walking away from is the thing that notice owes them.
    /// </summary>
    [Fact]
    public void TheFirstDigestOfAnAwaySpellIsAlwaysSent()
    {
        Assert.True(
            AwayDigest_Decider.Should_Send(null, "🌙 solo-1: inactive"),
            "the first digest of an away spell was withheld — AWAY MODE ON promises the owner exactly one of these");
    }

    /// <summary>
    /// Ordinal, not trimmed or normalised: the digest IS the text the owner reads, so a difference
    /// they would see on their phone is a difference worth a message.
    /// </summary>
    [Fact]
    public void ADigestDifferingOnlyInAMemberNameIsSent()
    {
        Assert.True(AwayDigest_Decider.Should_Send("🌙 imp-1: inactive", "🌙 imp-2: inactive"));
    }

    /// <summary>
    /// An orchestration that goes idle, is woken, and settles back to the SAME rendering must not
    /// pay for the round trip with a message — this is the oscillation the loop actually rode.
    /// </summary>
    [Fact]
    public void ReturningToAPreviouslySentRenderingIsStillJudgedAgainstTheLastOneSent()
    {
        const string idle = "🌙 solo-1: inactive";

        Assert.True(AwayDigest_Decider.Should_Send(idle, "🌙 solo-1: working"));
        Assert.True(AwayDigest_Decider.Should_Send("🌙 solo-1: working", idle));
        Assert.False(AwayDigest_Decider.Should_Send(idle, idle));
    }
}
