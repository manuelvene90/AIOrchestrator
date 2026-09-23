using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHAT A ⏸/▶ TAP SAYS, AND WHICH RECEIPT A WAIT IS ABOUT (plan 03 Tasks 6b and 6c). The tap itself — hold,
/// or release and flush — is the engine's; ▶ Send now added a second way to send GO, and with it cases the
/// handler used to meet only rarely.
///
/// <para>
/// THE ANSWER IS COMPUTED BEFORE THE TAP ACTS, because it is sent first (an unanswered callback spins
/// on the phone). A GO with nothing buffered and no hold answers "already sent": the message left
/// before the tap, and a bare ✓ over the button reads as "sent now" — a claim about this tap that is
/// false.
/// </para>
/// <para>
/// ONE HOLD, ONE RECEIPT (Task 6c fix round 1). A WAIT — typed, or ⏸ tapped on any ✓ — while the hold already
/// has a receipt is about THAT receipt (<see cref="Resolve_WaitReceipt"/>). Before, a second typed WAIT sent a
/// second "⏸ holding" and a ⏸ on an older ✓ turned it into one, and either way the first hold receipt was
/// stranded saying "holding" for ever. What becomes of a hold receipt when the hold ENDS is decided with the
/// tick it may become, in one lock — <see cref="ReceiptRegistry.IReceiptRegistry.Finish_Hold"/> — which
/// replaced the two GO rules this class used to hold (rewrite-or-not, adopt-or-not): they read registry
/// state from outside its lock, and that is how a flush could land between the halves (the review's m6).
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
    /// THE RECEIPT A WAIT IS ABOUT, AND WHETHER IT IS REDRAWN. With a hold receipt already standing, that one —
    /// whatever was tapped, and for a typed WAIT too — redrawn only if its count moved: the text is a function
    /// of the count, and an identical edit is refused by Telegram with a 400 that still spends the message's
    /// slot in the per-message gap. With none, the tapped ✓ (<paramref name="tappedReceiptId"/>; null for a typed
    /// WAIT, or a tap on the PULSE bar, which is never a receipt) becomes the hold receipt and is drawn as one.
    /// A null id means the caller has no receipt yet: a typed WAIT makes one, a tap on the bar has none.
    /// </summary>
    public static (long? ReceiptId, bool Redraw) Resolve_WaitReceipt(long? tappedReceiptId, (long MessageId, int HeldCount)? holdReceipt, int heldCount)
    {
        if (holdReceipt != null)
            return (holdReceipt.Value.MessageId, holdReceipt.Value.HeldCount != heldCount);

        return (tappedReceiptId, tappedReceiptId != null);
    }
}
