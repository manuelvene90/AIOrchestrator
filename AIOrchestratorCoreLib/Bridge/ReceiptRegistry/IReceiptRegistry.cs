namespace AIOrchestratorCoreLib.Bridge.ReceiptRegistry;

/// <summary>
/// THE RECEIPT EACH TOPIC IS WAITING TO EVOLVE — the ✓ message to edit into ✓✓, or the owner's own
/// message wearing 👀 to turn 👌 — keyed by thread, with 0 for the General topic (no thread id). Moved out
/// of <c>BridgeEngineModel</c> by plan 03 Task 6b, when ▶ Send now needed to ask whether the ✓ it was
/// tapped on is the one the delivery is about to edit.
///
/// <para>
/// TWO MAPS, NOT ONE, because the two hold DIFFERENT message ids and mean different things: a tick is a
/// bot message, a reaction's id is the OWNER'S message. A single map would hold an id whose meaning
/// depends on which path wrote it, and the pickup would then edit the owner's own message instead of
/// reacting to it (brief D, 2026-09-09).
/// </para>
/// <para>
/// CONSUMED ON READ: the next batch gets its own receipt rather than rewriting one that has already
/// been answered. Only <see cref="Is_TheTickTheDeliveryWillEdit"/> reads without taking.
/// </para>
/// <para>
/// ITS OWN LOCK: the inbound loop remembers (a receipt goes out as a message lands) while the mirror
/// loop takes (a delivery), and a lock this store owns cannot be forgotten by a caller.
/// </para>
/// <para>
/// AND THE TEXT EACH RECEIPT MESSAGE IS WAITING TO SHOW (plan 03 Task 6c, 2026-09-23). Telegram allows one
/// edit of a message per <see cref="Telegram.TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE"/>,
/// and one receipt is edited by many writers — the ✓→✓✓ delivery, the typed and tapped ⏸/▶ rewrites, the hold
/// count-up, the busy narration, the stale-receipt nudge and the turn-end line. The ✓✓ after a ⏸ Wait was
/// that message's second edit, the client held
/// it, and nothing tried again: "⏸ holding" stayed on a delivered message for ever. So every writer stages
/// here, per MESSAGE and last writer wins, and a held edit waits in its slot for the door rather than being
/// dropped. NOT a detached retry holding a copy of its text: a receipt's text is not final, and a captured
/// copy landing late would overwrite a newer edit of the same message.
/// </para>
/// </summary>
public interface IReceiptRegistry
{
    /// <summary>The ✓ just sent in this thread; the newest one wins — the earlier ✓s of a batch stay ✓.</summary>
    void Remember_Tick(long? messageThreadId, long tickMessageId);

    /// <summary>The owner message that has just been given 👀 in this thread.</summary>
    void Remember_Reaction(long? messageThreadId, long ownerMessageId);

    long? Take_Tick_OrNull(long? messageThreadId);

    long? Take_Reaction_OrNull(long? messageThreadId);

    /// <summary>
    /// Whether a delivery in this thread would edit <paramref name="messageId"/> into ✓✓: it is the
    /// registered tick, and no 👀 is waiting beside it (the delivery turns that 👌 and edits nothing).
    /// A peek — nothing is consumed.
    /// </summary>
    bool Is_TheTickTheDeliveryWillEdit(long? messageThreadId, long messageId);

    /// <summary>
    /// MAKES A HOLD RECEIPT THE ✓ THE DELIVERY WILL EDIT — only when NOTHING ELSE is registered for the thread
    /// (plan 03 Task 6c). A typed WAIT takes the ✓ out of this registry to turn it into "⏸ holding", so without
    /// this the delivery had no tick to edit and the batch never showed ✓✓. Refused (false) while a 👀 waits:
    /// the delivery turns that 👌 and never takes the tick, and a tick left behind would be edited by a LATER
    /// delivery that has nothing to do with it. Refused while a DIFFERENT ✓ is registered (fix round 1, the
    /// review's m1): that ✓ is newer — it sits under the owner's last message — and adopting over it left it
    /// showing its buttons on a delivered batch. Accepted when the registered ✓ IS this message.
    /// </summary>
    bool Adopt_Tick(long? messageThreadId, long messageId);

    /// <summary>
    /// ONE HOLD, ONE RECEIPT (plan 03 Task 6c fix round 1): the message a hold in this thread is kept on, and the
    /// count it shows — or null when there is no hold, or it has no message (entered from the PULSE bar, or its
    /// acknowledgement could not be sent). A WAIT, typed or tapped, asks this FIRST: a hold that already has a
    /// receipt is redrawn there, never given a second "⏸ holding" that strands the first.
    /// </summary>
    (long MessageId, int HeldCount)? Find_HoldReceipt_OrNull(long? messageThreadId);

    /// <summary>The hold in this thread is kept on <paramref name="messageId"/> (null: it has no message), showing <paramref name="heldCount"/>.</summary>
    void Remember_HoldReceipt(long? messageThreadId, long? messageId, int heldCount);

    /// <summary>
    /// A message landed during the hold: its count goes up, and the receipt to redraw is returned — null when
    /// there is no hold here, or one with no message to redraw (the count still goes up).
    /// </summary>
    (long MessageId, int HeldCount)? Count_HeldMessage_OrNull(long? messageThreadId);

    /// <summary>
    /// EVERY ROUTE THAT ENDS A HOLD FINISHES ITS RECEIPT (plan 03 Task 6c fix round 1) — typed GO, a tapped GO or
    /// ▶ Send now, PULSE's ▶ GO, and a delivery that finds a hold receipt nobody finished. Forgets the hold's
    /// receipt and, in the same lock as the tick it reads, decides what becomes of it: ADOPTED as THE ✓ when held
    /// messages are being delivered and <see cref="Adopt_Tick"/> accepts it (the delivery then edits it into ✓✓),
    /// otherwise returned as the message to rewrite back to a ✓ — a hold receipt no delivery will edit must not
    /// say "⏸ holding" after the hold is over. <c>HadReceipt</c> is false when there was nothing to finish.
    /// Called BEFORE the buffer is released (the review's m6): a mirror-tick flush in between would find no ✓,
    /// and the receipt adopted after it would sit in the registry for an unrelated delivery.
    /// </summary>
    (bool HadReceipt, long? RewriteMessageId) Finish_Hold(long? messageThreadId, bool deliveringHeldMessages);

    /// <summary>
    /// THE TEXT A RECEIPT MESSAGE SHOULD SHOW — LAST WRITER WINS — staged by every writer of a receipt
    /// (plan 03 Task 6c; <see cref="ReceiptEdit_Sender"/> is the one caller). Returns the attempt to make NOW,
    /// or null when one is in flight or the message's door is already known to be shut: the newest text then
    /// waits in the slot, and <see cref="Take_DueEdits"/> hands it out when the door opens.
    /// <paramref name="buttons"/> null is a plain edit, which Telegram reads as "remove the keyboard".
    /// </summary>
    (long MessageId, long Version, string Text, IReadOnlyList<(string Data, string Label)>? Buttons, int AttemptNumber, string LogScope)? Stage_Edit_OrNull(
        long messageId, string text, IReadOnlyList<(string Data, string Label)>? buttons, string logScope, DateTime nowUtc);

    /// <summary>Every staged edit whose door has opened and that nobody is attempting — each is marked in flight.</summary>
    IReadOnlyList<(long MessageId, long Version, string Text, IReadOnlyList<(string Data, string Label)>? Buttons, int AttemptNumber, string LogScope)> Take_DueEdits(DateTime nowUtc);

    /// <summary>
    /// The attempt landed. The slot is cleared only if nothing newer was staged meanwhile; a newer text stays,
    /// due at once — the gate names the real door on its first try.
    /// </summary>
    void Settle_EditLanded(long messageId, long version);

    /// <summary>The door was shut, or Telegram asked to wait: the NEWEST staged text is tried again at <paramref name="notBeforeUtc"/>.</summary>
    void Settle_EditDeferred(long messageId, DateTime notBeforeUtc);

    /// <summary>Given up on this version (a refusal no wait will change). A newer text staged meanwhile keeps its own chance.</summary>
    void Settle_EditAbandoned(long messageId, long version);

    /// <summary>Whether an edit of <paramref name="messageId"/> is still waiting to land.</summary>
    bool Has_PendingEdit(long messageId);
}
