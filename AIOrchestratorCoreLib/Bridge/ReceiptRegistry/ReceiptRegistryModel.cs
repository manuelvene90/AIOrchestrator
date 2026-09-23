namespace AIOrchestratorCoreLib.Bridge.ReceiptRegistry;

/// <inheritdoc cref="IReceiptRegistry"/>
internal sealed class ReceiptRegistryModel : IReceiptRegistry
{
    /// <summary>The key a thread id is stored under — the General topic has none, and is 0.</summary>
    const long GENERAL_TOPIC_KEY = 0;

    readonly Lock _gate = new();
    readonly Dictionary<long, long> _tickMessageIdByThread = [];
    readonly Dictionary<long, long> _reactedOwnerMessageIdByThread = [];

    public void Remember_Tick(long? messageThreadId, long tickMessageId)
    {
        lock (_gate)
            _tickMessageIdByThread[messageThreadId ?? GENERAL_TOPIC_KEY] = tickMessageId;
    }

    public void Remember_Reaction(long? messageThreadId, long ownerMessageId)
    {
        lock (_gate)
            _reactedOwnerMessageIdByThread[messageThreadId ?? GENERAL_TOPIC_KEY] = ownerMessageId;
    }

    public long? Take_Tick_OrNull(long? messageThreadId)
    {
        lock (_gate)
            return Take_OrNull(_tickMessageIdByThread, messageThreadId);
    }

    public long? Take_Reaction_OrNull(long? messageThreadId)
    {
        lock (_gate)
            return Take_OrNull(_reactedOwnerMessageIdByThread, messageThreadId);
    }

    public bool Is_TheTickTheDeliveryWillEdit(long? messageThreadId, long messageId)
    {
        var key = messageThreadId ?? GENERAL_TOPIC_KEY;

        lock (_gate)
        {
            return !_reactedOwnerMessageIdByThread.ContainsKey(key)
                && _tickMessageIdByThread.TryGetValue(key, out var tickMessageId)
                && tickMessageId == messageId;
        }
    }

    static long? Take_OrNull(Dictionary<long, long> byThread, long? messageThreadId)
    {
        var key = messageThreadId ?? GENERAL_TOPIC_KEY;

        if (!byThread.TryGetValue(key, out var messageId))
            return null;

        byThread.Remove(key);
        return messageId;
    }
}
