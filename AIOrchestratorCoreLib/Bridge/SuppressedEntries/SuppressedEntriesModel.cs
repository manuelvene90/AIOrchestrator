using AIOrchestratorCoreLib.Logging.OrchestrationLog;

namespace AIOrchestratorCoreLib.Bridge.SuppressedEntries;

/// <inheritdoc cref="ISuppressedEntries"/>
internal sealed class SuppressedEntriesModel(IOrchestrationLog log) : ISuppressedEntries
{
    readonly IOrchestrationLog _log = log;

    // ITS OWN LOCK, not the engine's _ownerStateLock. The mirror files from its loop, the reply
    // resolver drains from the same loop, but Forget arrives from the INBOUND loop (an owner message
    // landing) and from a close — so the list is touched from more than one thread, and a lock this
    // store owns cannot be forgotten by a caller.
    readonly Lock _gate = new();
    readonly Dictionary<string, List<(string? Subject, string Text)>> _byOrchId = [];

    /// <summary>
    /// The orchestrations whose CURRENT fill has already been said to overflow. Cleared with the list by
    /// Drain and Forget, so it means "this fill", never "ever".
    /// </summary>
    readonly HashSet<string> _overflowSaid = [];

    public void File(string orchId, string? subject, string text)
    {
        (string? Subject, string Text)? firstDropOfThisFill = null;

        lock (_gate)
        {
            if (!_byOrchId.TryGetValue(orchId, out var held))
            {
                held = [];
                _byOrchId[orchId] = held;
            }

            held.Add((subject, text));

            if (held.Count > ISuppressedEntries.MAX_ENTRIES_PER_ORCHESTRATION)
            {
                var dropped = held[0];
                held.RemoveAt(0);

                if (_overflowSaid.Add(orchId))
                    firstDropOfThisFill = dropped;
            }
        }

        // Logged outside the gate: a log sink is someone else's I/O, and holding this lock across it
        // would let a slow log stall the mirror's next filing.
        if (firstDropOfThisFill != null)
        {
            _log.Log_Info(
                orchId,
                $"The turn-end digest is full at {ISuppressedEntries.MAX_ENTRIES_PER_ORCHESTRATION} held entries — dropping the oldest from it, starting with '{firstDropOfThisFill.Value.Subject}'. "
                + "Said once until the digest is handed over or forgotten; every entry is still in the channel file");
        }
    }

    public IReadOnlyList<(string? Subject, string Text)> Drain(string orchId)
    {
        lock (_gate)
        {
            _overflowSaid.Remove(orchId);

            if (!_byOrchId.Remove(orchId, out var held))
                return [];

            return held;
        }
    }

    public void Forget(string orchId)
    {
        lock (_gate)
        {
            _overflowSaid.Remove(orchId);
            _byOrchId.Remove(orchId);
        }
    }
}
