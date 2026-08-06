namespace AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;

/// <summary>
/// Debounces the owner's inbound texts: messages arriving in quick succession (the owner often
/// sends several in a row) are aggregated per target channel and delivered as ONE entry once the
/// stream has been quiet for the aggregation window.
/// </summary>
public interface IOwnerDeliveryBuffer
{
    void Add_Segment(string targetKey, string segment, DateTime nowUtc);

    /// <summary>Targets quiet for at least the aggregation window: key → aggregated text. Removes them from the buffer.</summary>
    IReadOnlyDictionary<string, string> Take_ReadyDeliveries(DateTime nowUtc);

    bool Has_PendingDeliveries();
}
