using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// "IDLE" WAS EVERY STATE THAT WAS NOT MID-TURN, and the owner caught it mid-conversation on
/// 2026-08-15: *"What does that idle imply? I know you're not idle because we're talking … especially
/// because you had just asked me a question, and I had answered you, so you were definitely
/// reasoning."*
///
/// The label was literally true — no turn inside the two-minute window — and read as ABSENT when the
/// truth was WAITING. In a conversation neither side is mid-turn most of the time, so "idle" was what
/// they saw almost whenever they looked.
/// </summary>
public class WaitingOnYouIsNotIdleTests
{
    [Fact]
    public void MidTurn_OutranksEverything()
    {
        Assert.Equal(
            "working now",
            MemberState_Descriptor.Describe_ForOwner(MemberStates.NewNoTraffic, isWorkingNow: true, ownerOwesReply: true));
    }

    [Fact]
    public void NotMidTurn_AndTheOwnerOwesAReply_SaysSo()
    {
        Assert.Equal(
            MemberState_Descriptor.WAITING_ON_OWNER,
            MemberState_Descriptor.Describe_ForOwner(MemberStates.StandingBy, isWorkingNow: false, ownerOwesReply: true));
    }

    [Fact]
    public void NotMidTurn_AndNothingOwed_KeepsTheDeclaredState()
    {
        Assert.Equal(
            MemberState_Descriptor.Describe(MemberStates.StandingBy),
            MemberState_Descriptor.Describe_ForOwner(MemberStates.StandingBy, isWorkingNow: false, ownerOwesReply: false));
    }

    /// <summary>
    /// THE EXACT CASE THE OWNER SAW. A solo whose channel carries no member traffic resolves to
    /// "new — no traffic" — which is what their card showed while they were talking to it. Waiting
    /// beats the declared state, or the fix would only cover the states nobody was looking at.
    /// </summary>
    [Fact]
    public void ASessionWithNoDeclaredTraffic_StillReadsAsWaiting_NotAsNew()
    {
        var described = MemberState_Descriptor.Describe_ForOwner(MemberStates.NewNoTraffic, isWorkingNow: false, ownerOwesReply: true);

        Assert.Equal(MemberState_Descriptor.WAITING_ON_OWNER, described);
        Assert.DoesNotContain("no traffic", described);
    }

    /// <summary>
    /// Every state, so the third one cannot be added later for some values and not others — the same
    /// guarantee MemberStateDescriptorTests gives the other two.
    /// </summary>
    [Theory]
    [InlineData(MemberStates.NewNoTraffic)]
    [InlineData(MemberStates.ImplementerWorking)]
    [InlineData(MemberStates.AwaitingSupervisorReview)]
    [InlineData(MemberStates.WritingWindowOpen)]
    [InlineData(MemberStates.BlockedOnOwner)]
    [InlineData(MemberStates.StandingBy)]
    public void EveryState_AnswersWaiting_WhenTheOwnerOwesAReply(MemberStates state)
    {
        Assert.Equal(
            MemberState_Descriptor.WAITING_ON_OWNER,
            MemberState_Descriptor.Describe_ForOwner(state, isWorkingNow: false, ownerOwesReply: true));
    }

    [Theory]
    [InlineData(MemberStates.NewNoTraffic)]
    [InlineData(MemberStates.ImplementerWorking)]
    [InlineData(MemberStates.AwaitingSupervisorReview)]
    [InlineData(MemberStates.WritingWindowOpen)]
    [InlineData(MemberStates.BlockedOnOwner)]
    [InlineData(MemberStates.StandingBy)]
    public void EveryState_IsDescribableWhileWorking(MemberStates state)
    {
        Assert.StartsWith("working now", MemberState_Descriptor.Describe_ForOwner(state, isWorkingNow: true, ownerOwesReply: false));
    }
}
