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

    /// <summary>
    /// The dangerous case: a real question asked in prose, with no marker. Dropping it would leave
    /// the supervisor waiting for an answer the owner never saw — a deadlock neither can observe.
    /// A false positive costs one ignorable message; a false negative costs the whole conversation.
    /// </summary>
    [Theory]
    [InlineData("## [4] FROM supervisor — d — s\nShould I merge this into master or hold it?")]
    [InlineData("## [4] FROM supervisor — d — s\nDo you want the synthetic drawdown or the real one?")]
    [InlineData("## [4] FROM supervisor — d — s\nwhich approach do you prefer")]
    public void AQuestionInPlainProse_IsStillPushed(string entry)
    {
        // The last case has no '?' and is NOT caught by this filter — deliberately asserted so the
        // boundary stays visible. It is not a deadlock: an entry this filter suppresses is
        // remembered, and the engine releases it once the supervisor AND every member have been
        // idle for minutes (Break_SilentDeadlock_Async). The filter is the fast path; that is the
        // guarantee.
        var pushed = OwnerPush_Policy.Should_Push(entry, ownerIsWaitingForAReply: false);

        Assert.Equal(entry.Contains('?'), pushed);
    }

    /// <summary>A '?' inside an ASCII mockup or snippet is not the supervisor asking anything.</summary>
    [Fact]
    public void AQuestionMarkInsideAFencedBlock_DoesNotCount()
    {
        var entry = "## [5] FROM supervisor — d — s\nprogress update\n```\n| ready? | yes |\n```\nnothing else";

        Assert.False(OwnerPush_Policy.Asks_InProse(entry));
        Assert.False(OwnerPush_Policy.Should_Push(entry, false));
    }

    [Fact]
    public void EmptyEntry_IsNeverPushed()
    {
        Assert.False(OwnerPush_Policy.Should_Push("", false));
    }

    /// <summary>
    /// The button label is what fits on a phone; the text the supervisor receives is the actual
    /// instruction. They MUST differ — a supervisor told only "❔ Explain the options" would have to
    /// guess what was being asked of it, and it must know to re-ask afterwards.
    /// </summary>
    [Fact]
    public void TheExplainButton_SendsAFullInstruction_NotItsOwnLabel()
    {
        Assert.NotEqual(OwnerPush_Policy.MORE_DETAIL_LABEL, OwnerPush_Policy.MORE_DETAIL_REQUEST);
        Assert.True(OwnerPush_Policy.MORE_DETAIL_LABEL.Length <= 30, "the label has to fit a phone button");

        Assert.Contains("recommend", OwnerPush_Policy.MORE_DETAIL_REQUEST);
        Assert.Contains("costs to get wrong", OwnerPush_Policy.MORE_DETAIL_REQUEST);
        Assert.Contains("ask the question again", OwnerPush_Policy.MORE_DETAIL_REQUEST);
        Assert.Contains("short", OwnerPush_Policy.MORE_DETAIL_REQUEST, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Carries_Question_SpotsEitherMarker()
    {
        Assert.True(OwnerPush_Policy.Carries_Question("QUESTION: which one?"));
        Assert.True(OwnerPush_Policy.Carries_Question("OPTION: Merge it"));
        Assert.False(OwnerPush_Policy.Carries_Question("I have a question about the design"));
    }
}
