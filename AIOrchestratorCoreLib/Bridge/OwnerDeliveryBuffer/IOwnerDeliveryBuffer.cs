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
    /// TWO PUT-BACKS FOR ONE KEY WOULD LAND IN REVERSE ORDER AGAINST EACH OTHER, so it matters how
    /// nearly that is excluded — and it is NOT excluded structurally. The two halves of the argument
    /// are different in kind and this comment used to assert both as structural, which was false:
    /// <list type="bullet">
    /// <item>
    /// HOLDS BY CONSTRUCTION — <c>Take_ReadyDeliveries</c> removes every returned key atomically under
    /// the one lock, so no second caller can take a key while its delivery is out.
    /// </item>
    /// <item>
    /// DOES NOT HOLD — "only the mirror loop flushes". There is a SECOND entry point:
    /// <c>Apply_HoldControlWord_Async</c>'s GO branch flushes on the INBOUND loop, deliberately, so the
    /// owner's GO is not left waiting up to 2 s for the next tick. It RELEASES first, which makes a
    /// newly-arrived segment immediately takeable. So when the owner types GO while a delivery is in
    /// flight, and both fail on the same correlated cause, two put-backs are reachable and the later
    /// one wins the front.
    /// </item>
    /// </list>
    /// </para>
    /// <para>
    /// THAT WINDOW IS KNOWN, ACCEPTED AND NARROW, not impossible. It needs a GO inside a failing
    /// delivery; the single-put-back case is overwhelmingly the common one and this method is a
    /// straight improvement to it. Appending is not the safer fallback — it is wrong in the mirror
    /// case AND wrong in the common single case, which is the defect this replaced.
    /// </para>
    /// <para>
    /// WHAT WOULD MAKE IT STRUCTURAL: serialising the two flush entry points. That is deliberately not
    /// done — it puts a lock on the owner's delivery path and would make GO wait behind an in-flight
    /// mirror flush holding a translator subprocess, defeating the feature to fix the ordering. It is
    /// recorded as post-merge work rather than left for the next reader to re-derive.
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
