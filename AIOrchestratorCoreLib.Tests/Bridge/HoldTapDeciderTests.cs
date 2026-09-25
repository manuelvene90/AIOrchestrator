using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// What a ⏸/▶ tap SAYS (the toast over the button) and whether it REWRITES the receipt it is about
/// (plan 03 Task 6b) — and, since Task 6c, WHICH receipt that is and whether a GO makes it the ✓. The engine
/// half — that the tap delivers at once and the ✓ still becomes ✓✓ — is <see cref="SendNowSkipsTheWindowTests"/>
/// and, over the real per-message edit gap, <see cref="TheDoubleTickSurvivesTheEditGapTests"/>.
/// </summary>
public class HoldTapDeciderTests
{
    [Fact]
    public void ATapInATopicWithNoOrchestration_SaysSo()
    {
        Assert.Equal(
            HoldTap_Decider.NO_ORCHESTRATION_ANSWER,
            HoldTap_Decider.Describe_Answer(topicHasOrchestration: false, HoldButtonActions.Go, pendingBeforeTheTap: 0, holdingBeforeTheTap: false));
    }

    /// <summary>
    /// SEND NOW ON A MESSAGE THAT ALREADY LEFT: a bare ✓ over the button reads as "sent now" — a claim
    /// about this tap that is false; the message went before it.
    /// </summary>
    [Fact]
    public void AGoWithNothingBufferedAndNoHold_AnswersAlreadySent()
    {
        Assert.Equal(
            HoldTap_Decider.ALREADY_SENT_ANSWER,
            HoldTap_Decider.Describe_Answer(topicHasOrchestration: true, HoldButtonActions.Go, pendingBeforeTheTap: 0, holdingBeforeTheTap: false));
    }

    [Theory]
    [InlineData(HoldButtonActions.Go, 1, false)]  // Send now with a message in the window — it goes now
    [InlineData(HoldButtonActions.Go, 0, true)]   // GO that ends an empty hold — the hold ends now
    [InlineData(HoldButtonActions.Go, 2, true)]   // GO that releases a hold — what is held goes now
    [InlineData(HoldButtonActions.Hold, 0, false)] // WAIT with nothing typed yet — the hold starts now
    [InlineData(HoldButtonActions.Hold, 1, false)]
    public void ATapThatChangesSomething_AnswersTheTick(HoldButtonActions action, int pendingBeforeTheTap, bool holdingBeforeTheTap)
    {
        Assert.Equal(
            HoldTap_Decider.DONE_ANSWER,
            HoldTap_Decider.Describe_Answer(topicHasOrchestration: true, action, pendingBeforeTheTap, holdingBeforeTheTap));
    }

    // ---------------------------------------------------------------------------------------
    // ONE HOLD, ONE RECEIPT (Task 6c fix round 1). The GO rules this class held until then — rewrite the
    // tapped message or not, adopt the hold receipt or not — moved into IReceiptRegistry.Finish_Hold, where
    // they are decided in the same lock as the tick they read; ReceiptRegistryTests pins them there:
    //   * a GO that ends no hold rewrites nothing           -> AGoWithNoHoldReceipt_FinishesNothing
    //   * a GO releasing a hold the delivery will edit      -> AHoldReceipt_BeingDelivered_IsAdopted_WhenNothingElseIsRegistered
    //   * a GO ending an EMPTY hold goes back to a ✓        -> AnEmptyHold_IsRewritten_NeverAdopted
    //   * a hold receipt another receipt supersedes, too    -> AHoldReceipt_BesideAnotherTickOrAReaction_IsRewritten
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A WAIT WHILE THE HOLD HAS A RECEIPT IS ABOUT THAT RECEIPT — whatever was tapped, an older ✓ included, and
    /// for a typed WAIT too. It is redrawn only if its count moved: an identical edit is a 400 that still spends
    /// the message's slot.
    /// </summary>
    [Theory]
    [InlineData(4001L)]
    [InlineData(null)]
    public void AWaitDuringAHoldWithAReceipt_IsAboutThatReceipt(long? tappedReceiptId)
    {
        Assert.Equal(((long?)4003, false), HoldTap_Decider.Resolve_WaitReceipt(tappedReceiptId, holdReceipt: (4003, 2), heldCount: 2));
        Assert.Equal(((long?)4003, true), HoldTap_Decider.Resolve_WaitReceipt(tappedReceiptId, holdReceipt: (4003, 2), heldCount: 3));
    }

    /// <summary>With no hold receipt yet, the tapped ✓ becomes it, and is drawn as one.</summary>
    [Fact]
    public void AWaitWithNoHoldReceipt_MakesTheTappedTickTheReceipt()
    {
        Assert.Equal(((long?)4001, true), HoldTap_Decider.Resolve_WaitReceipt(tappedReceiptId: 4001, holdReceipt: null, heldCount: 1));
    }

    /// <summary>
    /// Nothing to be about: a typed WAIT with no hold receipt makes one itself, and a ⏸ on the PULSE bar — which
    /// is never a receipt — has none.
    /// </summary>
    [Fact]
    public void AWaitWithNoReceiptAnywhere_IsAboutNothing()
    {
        Assert.Equal(((long?)null, false), HoldTap_Decider.Resolve_WaitReceipt(tappedReceiptId: null, holdReceipt: null, heldCount: 1));
    }
}
