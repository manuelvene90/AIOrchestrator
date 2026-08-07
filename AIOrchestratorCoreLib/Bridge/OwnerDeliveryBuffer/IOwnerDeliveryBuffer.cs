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

    /// <summary>Targets ready to deliver: key → aggregated text. Removes them from the buffer.</summary>
    IReadOnlyDictionary<string, string> Take_ReadyDeliveries(DateTime nowUtc);

    bool Has_PendingDeliveries();

    /// <summary>WAIT: hold everything for this target until <see cref="Release"/> or the idle cap.</summary>
    void Hold(string targetKey, DateTime nowUtc);

    /// <summary>GO: deliver what has accumulated on the next take, without waiting out the window.</summary>
    void Release(string targetKey);

    /// <summary>True while held — the caller suppresses per-message receipts so the phone stays quiet.</summary>
    bool Is_Holding(string targetKey);
}
