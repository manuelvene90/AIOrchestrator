using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// Owner: "I answer the sup a question, and then the sup doesn't disturb me anymore unless it has
/// another question. A brief every 30 minutes about how the work is going is fine, but not the
/// waterfall of messages I get now."
///
/// Nothing is deleted by this — every entry still lands in the channel and the app. It decides only
/// what becomes a NOTIFICATION.
/// </summary>
public class OwnerPushPolicyTests
{
    [Fact]
    public void AQuestion_IsPushed()
    {
        var entry = "## [7] FROM supervisor — d — s\nQUESTION: merge now or hold?\nOPTION: Merge\nOPTION: Hold";

        Assert.True(OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: false));
    }

    [Fact]
    public void BlockedOnOwner_IsPushed_BecauseOnlyTheyCanRestartIt()
    {
        Assert.True(OwnerPush_Policy.Should_Push("## [3] FROM supervisor — d — s\nBLOCKED ON OWNER: need the token", false));
    }

    /// <summary>The reply to something they asked must always get through — they are waiting for it.</summary>
    [Fact]
    public void AnAnswer_IsPushed_EvenWithNoQuestionInIt()
    {
        Assert.True(OwnerPush_Policy.Should_Push("## [9] FROM supervisor — d — s\nYes, the daily DD is feasible.", ownerIsWaitingForAReply: true));
    }

    /// <summary>
    /// The waterfall. Every one of these is real narration from the transcript that prompted this —
    /// useful in the channel, noise on a phone.
    /// </summary>
    [Theory]
    [InlineData("## [11] FROM supervisor — d — s\nimp-1 is pricing both options now, still read-only.")]
    [InlineData("## [12] FROM supervisor — d — s\nConfirmed: the preliminary simulation is the mechanism.")]
    [InlineData("## [13] FROM supervisor — d — s\nAccepted imp-3's Task 6; the ledger is updated.")]
    public void ProgressNarration_IsNotPushed(string entry)
    {
        Assert.False(OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: false));
    }

    [Fact]
    public void EmptyEntry_IsNeverPushed()
    {
        Assert.False(OwnerPush_Policy.Should_Push("", false));
    }

    [Fact]
    public void Carries_Question_SpotsEitherMarker()
    {
        Assert.True(OwnerPush_Policy.Carries_Question("QUESTION: which one?"));
        Assert.True(OwnerPush_Policy.Carries_Question("OPTION: Merge it"));
        Assert.False(OwnerPush_Policy.Carries_Question("I have a question about the design"));
    }
}
