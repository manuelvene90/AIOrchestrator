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
/// The client HOLDS the second edit, the delivery receipt does not retry, and the owner is left with
/// "✓ ⏸ Wait ▶ Send now" under a message that was delivered — the double tick they asked to keep,
/// lost to the button they asked for. What remains is the one case the rewrite exists for: a hold
/// receipt that would otherwise say "⏸ holding" after the hold has ended.
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
    /// Whether the tapped receipt is rewritten. A HOLD always is — the ✓ becomes the hold receipt. A GO is
    /// only when it ended a hold AND no delivery is about to edit that message: <paramref name="pendingAfterTheTap"/>
    /// is what the flush that follows the tap will deliver, and <paramref name="deliveryWillEditTheTappedMessage"/>
    /// says the delivery receipt is this very message.
    /// </summary>
    public static bool Should_RewriteTappedMessage(
        HoldButtonActions action, bool holdingBeforeTheTap, int pendingAfterTheTap, bool deliveryWillEditTheTappedMessage)
    {
        if (action == HoldButtonActions.Hold)
            return true;

        if (!holdingBeforeTheTap)
            return false;

        return !(pendingAfterTheTap > 0 && deliveryWillEditTheTappedMessage);
    }
}
