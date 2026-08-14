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
public interface IOwnerDeliveryBuffer
{
    void Add_Segment(string targetKey, string segment, DateTime nowUtc);

    /// <summary>
    /// Puts a segment back at the FRONT of its key, for a delivery that was taken and then failed.
    /// <para>
    /// WHY THE FRONT. <c>Take_ReadyDeliveries</c> removes the key and the delivery is then awaited for
    /// SECONDS — a translator subprocess and Telegram calls — during which the inbound loop can buffer
    /// a NEW segment B for the same key. Appending the failed original A would produce [B, A] and the
    /// supervisor would read the owner's later message ABOVE their earlier one, joined into one entry:
    /// "actually carry on" above "stop what you're doing", acted on last line first. Reordered
    /// actively misleads where late merely delays.
    /// </para>
    /// <para>
    /// SAFE BECAUSE AT MOST ONE PUT-BACK PER KEY CAN BE IN FLIGHT — two would land in reverse order
    /// against each other, which would make this worse than appending. The key is REMOVED by
    /// <c>Take_ReadyDeliveries</c> (pinned by its own case), so nothing else can take it while the
    /// delivery is out, and the mirror loop awaits each tick before starting the next, so two flushes
    /// never overlap. If either of those ever stops holding, this method stops being safe.
    /// </para>
    /// <para>
    /// Unlike <c>Add_Segment</c> this does NOT refresh <c>LastArrivalUtc</c>: the owner already served
    /// one aggregation window for this text and must not serve another for a failure they know nothing
    /// about.
    /// </para>
    /// </summary>
    void Prepend_Segment(string targetKey, string segment);

    /// <summary>Targets ready to deliver: key → aggregated text. Removes them from the buffer.</summary>
    IReadOnlyDictionary<string, string> Take_ReadyDeliveries(DateTime nowUtc);

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
