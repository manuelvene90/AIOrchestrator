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

    /// <summary>A WAIT always rewrites: the ✓ it was tapped on becomes the hold receipt, with the release.</summary>
    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(false, 1, true)]
    [InlineData(true, 3, true)]
    public void AHold_AlwaysRewritesTheTappedMessage(bool holdingBeforeTheTap, int pendingAfterTheTap, bool deliveryWillEditIt)
    {
        Assert.True(HoldTap_Decider.Should_RewriteReceipt(HoldButtonActions.Hold, holdingBeforeTheTap, pendingAfterTheTap, deliveryWillEditIt));
    }

    /// <summary>
    /// SEND NOW DOES NOT SPEND THE ✓'S EDIT. The ✓ it was tapped on already offers what a ✓ offers, and
    /// the delivery the tap triggers edits it to ✓✓ a moment later; a rewrite first would spend the one
    /// edit Telegram allows that message per 30 s (TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE)
    /// and the ✓✓ would be HELD, leaving "✓ ⏸ Wait ▶ Send now" under a message that was delivered.
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    public void AGoOnATickThatWasNotHolding_RewritesNothing(int pendingAfterTheTap, bool deliveryWillEditIt)
    {
        Assert.False(HoldTap_Decider.Should_RewriteReceipt(HoldButtonActions.Go, holdingBeforeTheTap: false, pendingAfterTheTap, deliveryWillEditIt));
    }

    /// <summary>Releasing a hold whose delivery will edit this very message: the ✓✓ replaces "⏸ holding", so no rewrite first.</summary>
    [Fact]
    public void AGoReleasingAHold_LeavesTheMessageToTheDeliveryThatWillEditIt()
    {
        Assert.False(HoldTap_Decider.Should_RewriteReceipt(HoldButtonActions.Go, holdingBeforeTheTap: true, pendingAfterTheTap: 2, deliveryWillEditTheReceipt: true));
    }

    /// <summary>
    /// A hold receipt NO delivery will edit — an empty hold, or one the delivery will not touch — would
    /// otherwise say "⏸ holding" for ever after the hold ended: it goes back to being a ✓.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(2, false)]
    public void AGoEndingAHold_RewritesAMessageNoDeliveryWillEdit(int pendingAfterTheTap, bool deliveryWillEditIt)
    {
        Assert.True(HoldTap_Decider.Should_RewriteReceipt(HoldButtonActions.Go, holdingBeforeTheTap: true, pendingAfterTheTap, deliveryWillEditIt));
    }

    /// <summary>
    /// A GO IS ABOUT THE HOLD'S OWN RECEIPT (plan 03 Task 6c, the review's Minor 1): ▶ Send now tapped on an
    /// older ✓ while a typed WAIT holds finishes the "⏸ holding" message, not the ✓ it was tapped on.
    /// </summary>
    [Fact]
    public void AGoWhileAHoldHasAReceipt_IsAboutThatReceipt()
    {
        Assert.Equal(4003, HoldTap_Decider.Resolve_Receipt(HoldButtonActions.Go, tappedMessageId: 4001, holdReceiptMessageId: 4003));
    }

    [Fact]
    public void AGoWithNoHoldReceipt_IsAboutTheTappedMessage()
    {
        Assert.Equal(4001, HoldTap_Decider.Resolve_Receipt(HoldButtonActions.Go, tappedMessageId: 4001, holdReceiptMessageId: null));
    }

    /// <summary>A WAIT makes the tapped ✓ the hold receipt, whatever was holding before.</summary>
    [Fact]
    public void AHold_IsAboutTheTappedMessage()
    {
        Assert.Equal(4001, HoldTap_Decider.Resolve_Receipt(HoldButtonActions.Hold, tappedMessageId: 4001, holdReceiptMessageId: 4003));
    }

    /// <summary>
    /// A GO THAT RELEASES HELD MESSAGES MAKES THE HOLD RECEIPT THE ✓ (the review's Minor 8), so the delivery
    /// edits it into ✓✓ — the typed WAIT had taken the ✓ out of the registry to make it.
    /// </summary>
    [Fact]
    public void AGoReleasingHeldMessages_AdoptsTheHoldReceipt()
    {
        Assert.True(HoldTap_Decider.Should_AdoptHoldReceipt(HoldButtonActions.Go, pendingAfterTheTap: 2, holdReceiptMessageId: 4003));
    }

    /// <summary>
    /// Not for an EMPTY hold (nothing will be delivered to finish it — the rewrite does), not for a hold with
    /// no receipt message, and never for a WAIT.
    /// </summary>
    [Theory]
    [InlineData(HoldButtonActions.Go, 0, 4003L)]
    [InlineData(HoldButtonActions.Go, 2, null)]
    [InlineData(HoldButtonActions.Hold, 2, 4003L)]
    public void AnythingElse_AdoptsNothing(HoldButtonActions action, int pendingAfterTheTap, long? holdReceiptMessageId)
    {
        Assert.False(HoldTap_Decider.Should_AdoptHoldReceipt(action, pendingAfterTheTap, holdReceiptMessageId));
    }
}
