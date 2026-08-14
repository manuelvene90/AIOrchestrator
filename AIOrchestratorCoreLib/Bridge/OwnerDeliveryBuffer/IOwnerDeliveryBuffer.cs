namespace AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;

/// <summary>
/// Debounces the owner's inbound texts: messages arriving in quick succession (the owner often
/// sends several in a row) are aggregated per target channel and delivered as ONE entry once the
/// stream has been quiet for the aggregation window.
///
/// The window is deliberately SHORT, because most messages arrive alone and a long one makes every
/// single message feel slow. The owner covers the other case explicitly with WAIT … GO: while a
/// target is HELD nothing is delivered and no per-message receipts are sent, so a long dictated
/// thought lands on the session as one turn and on the owner's phone as one acknowledgement.
/// </summary>
/// <summary>
/// One aggregated delivery taken from the buffer, with the ORDINAL of its earliest segment so a
/// failed delivery can be restored to its place rather than merely to the buffer.
/// </summary>
public interface IReadyDelivery
{
    string Text { get; }

    /// <summary>The lowest ordinal this delivery contained — pass it back to <c>Restore_Segment</c>.</summary>
    long FirstOrdinal { get; }
}

/// <inheritdoc cref="IReadyDelivery"/>
public sealed record ReadyDelivery(string Text, long FirstOrdinal) : IReadyDelivery;

public interface IOwnerDeliveryBuffer
{
    void Add_Segment(string targetKey, string segment, DateTime nowUtc);

    /// <summary>
    /// Puts a failed delivery's text back with the ORDINAL it originally arrived with, so it returns
    /// to its place in the conversation rather than merely to the buffer.
    /// <para>
    /// WHY AN ORDINAL AND NOT A POSITION. <c>Take_ReadyDeliveries</c> removes the key and the delivery
    /// is then awaited for SECONDS — a translator subprocess and Telegram calls — during which a NEW
    /// segment can be buffered for the same key. Restoring by POSITION cannot be made correct, because
    /// position is the thing under contention: prepending inverts when the older put-back lands first,
    /// appending inverts when the newer does, and there is no exclusivity available to pick between
    /// them. TWO flush entry points exist — the mirror tick and, deliberately,
    /// <c>Apply_HoldControlWord_Async</c>'s GO branch on the INBOUND loop — so two put-backs for one
    /// key are genuinely reachable.
    /// </para>
    /// <para>
    /// An ordinal removes the contention instead of guarding it: assigned once on arrival, never
    /// reused, and the aggregation reads in ordinal order. The result is chronological regardless of
    /// which put-back lands first, how many are in flight, or which loop they came from — so this
    /// needs no invariant about callers, which is the point. The previous version documented one, and
    /// it was false.
    /// </para>
    /// <para>
    /// Unlike <c>Add_Segment</c> this does NOT refresh <c>LastArrivalUtc</c>: the owner already served
    /// one aggregation window for this text and must not serve another for a failure they know nothing
    /// about.
    /// </para>
    /// </summary>
    void Restore_Segment(string targetKey, string segment, long ordinal);

    /// <summary>Targets ready to deliver: key → aggregated text. Removes them from the buffer.</summary>
    IReadOnlyDictionary<string, IReadyDelivery> Take_ReadyDeliveries(DateTime nowUtc);

    bool Has_PendingDeliveries();

    /// <summary>WAIT: hold everything for this target until <see cref="Release"/> or the idle cap.</summary>
    void Hold(string targetKey, DateTime nowUtc);

    /// <summary>GO: deliver what has accumulated on the next take, without waiting out the window.</summary>
    void Release(string targetKey);

    /// <summary>True while held — the caller suppresses per-message receipts so the phone stays quiet.</summary>
    bool Is_Holding(string targetKey);

    /// <summary>How many messages are waiting for this target — shown on the hold receipt.</summary>
    int Count_Pending(string targetKey);
}
