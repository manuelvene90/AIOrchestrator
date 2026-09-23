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
}
