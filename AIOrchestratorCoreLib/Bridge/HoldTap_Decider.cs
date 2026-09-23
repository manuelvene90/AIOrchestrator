using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHAT A ⏸/▶ TAP SAYS, AND WHETHER IT REWRITES THE MESSAGE IT WAS TAPPED ON (plan 03 Task 6b). The tap
/// itself — hold, or release and flush — is the engine's, unchanged; ▶ Send now added a second way to
/// send GO, and with it two cases the handler used to meet only rarely.
///
/// <para>
/// THE ANSWER IS COMPUTED BEFORE THE TAP ACTS, because it is sent first (an unanswered callback spins
/// on the phone). A GO with nothing buffered and no hold answers "already sent": the message left
/// before the tap, and a bare ✓ over the button reads as "sent now" — a claim about this tap that is
/// false.
/// </para>
/// <para>
/// A GO REWRITES ONLY A HOLD RECEIPT NO DELIVERY WILL EDIT. The rewrite used to follow every tap, which
/// was safe while GO could only be tapped on a hold receipt. Send now is tapped on the ✓ itself, and
/// the delivery the tap triggers edits that same ✓ to ✓✓ a moment later — so a rewrite first is an
/// edit of a message that is about to be edited again, and Telegram throttles edits of ONE message to
/// one per <see cref="TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE"/> (measured 2026-09-10).
/// The client HOLDS the second edit. Until Task 6c the delivery receipt did not retry and the owner was
/// left with "✓ ⏸ Wait ▶ Send now" under a message that was delivered; it now waits for the door
/// (<see cref="ReceiptRegistry.ReceiptEdit_Sender"/>), but the ✓✓ would still arrive a gap late for an
/// edit that changed nothing. What remains is the one case the rewrite exists for: a hold receipt that
/// would otherwise say "⏸ holding" after the hold has ended.
/// </para>
/// <para>
/// AND WHICH RECEIPT (plan 03 Task 6c): a GO is about the HOLD'S receipt, not the ✓ it was tapped on
/// (<see cref="Resolve_Receipt"/>), and a GO that releases held messages makes that receipt the ✓ the
/// delivery edits (<see cref="Should_AdoptHoldReceipt"/>).
/// </para>
/// </summary>
public static class HoldTap_Decider
{
    public const string NO_ORCHESTRATION_ANSWER = "no orchestration in this topic";
    public const string ALREADY_SENT_ANSWER = "already sent";
    public const string DONE_ANSWER = "✓";

    /// <summary>The toast over the tapped button — read from the buffer BEFORE the tap is applied.</summary>
    public static string Describe_Answer(bool topicHasOrchestration, HoldButtonActions action, int pendingBeforeTheTap, bool holdingBeforeTheTap)
    {
        if (!topicHasOrchestration)
            return NO_ORCHESTRATION_ANSWER;

        return action == HoldButtonActions.Go && pendingBeforeTheTap == 0 && !holdingBeforeTheTap
            ? ALREADY_SENT_ANSWER
            : DONE_ANSWER;
    }

    /// <summary>
    /// Whether the receipt the tap is about (<see cref="Resolve_Receipt"/>) is rewritten. A HOLD always is —
    /// the ✓ becomes the hold receipt. A GO is only when it ended a hold AND no delivery is about to edit that
    /// message: <paramref name="pendingAfterTheTap"/> is what the flush that follows the tap will deliver, and
    /// <paramref name="deliveryWillEditTheReceipt"/> says the delivery receipt is this very message.
    /// </summary>
    public static bool Should_RewriteReceipt(
        HoldButtonActions action, bool holdingBeforeTheTap, int pendingAfterTheTap, bool deliveryWillEditTheReceipt)
    {
        if (action == HoldButtonActions.Hold)
            return true;

        if (!holdingBeforeTheTap)
            return false;

        return !(pendingAfterTheTap > 0 && deliveryWillEditTheReceipt);
    }

    /// <summary>
    /// THE RECEIPT A TAP IS ABOUT. A HOLD makes the tapped ✓ the hold receipt. A GO finishes the HOLD'S OWN
    /// receipt when there is one, whichever ✓ it was tapped on (plan 03 Task 6c — the Task 6/6b review's
    /// Minor 1): the older ✓s of a batch still carry ▶ Send now, and a Send now tapped on one of them while a
    /// typed WAIT holds used to rewrite the tapped ✓ with the content it already had — a no-op edit Telegram
    /// refuses with a 400 and still counts against that message's gap — while the real hold receipt went on
    /// saying "⏸ holding" after the delivery.
    /// </summary>
    public static long Resolve_Receipt(HoldButtonActions action, long tappedMessageId, long? holdReceiptMessageId)
    {
        return action == HoldButtonActions.Go && holdReceiptMessageId != null
            ? holdReceiptMessageId.Value
            : tappedMessageId;
    }

    /// <summary>
    /// Whether a GO makes the hold's receipt THE ✓ the delivery edits into ✓✓ (plan 03 Task 6c — the review's
    /// Minor 8): it releases held messages, and the hold has a receipt message. A typed WAIT takes the ✓ out
    /// of the registry to turn it into "⏸ holding", so a typed WAIT released by ▶ GO had no tick for the
    /// delivery to edit and the GO's rewrite put "✓ + both buttons" on a batch that had just gone, while a
    /// tapped Wait then GO reached ✓✓. Adopting the hold receipt gives both the double tick, with one edit.
    /// An EMPTY hold is not adopted: nothing will be delivered to finish it, so the rewrite does.
    /// </summary>
    public static bool Should_AdoptHoldReceipt(HoldButtonActions action, int pendingAfterTheTap, long? holdReceiptMessageId)
    {
        return action == HoldButtonActions.Go && pendingAfterTheTap > 0 && holdReceiptMessageId != null;
    }
}
