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

    public void File(string orchId, string? subject, string text)
    {
        (string? Subject, string Text)? dropped = null;

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
                dropped = held[0];
                held.RemoveAt(0);
            }
        }

        // Logged outside the gate: a log sink is someone else's I/O, and holding this lock across it
        // would let a slow log stall the mirror's next filing.
        if (dropped != null)
        {
            _log.Log_Warning(
                orchId,
                $"The turn-end digest is full at {ISuppressedEntries.MAX_ENTRIES_PER_ORCHESTRATION} held entries — dropped the oldest, '{dropped.Value.Subject}', from it; the entry is still in the channel file");
        }
    }

    public IReadOnlyList<(string? Subject, string Text)> Drain(string orchId)
    {
        lock (_gate)
        {
            if (!_byOrchId.Remove(orchId, out var held))
                return [];

            return held;
        }
    }

    public void Forget(string orchId)
    {
        lock (_gate)
        {
            _byOrchId.Remove(orchId);
        }
    }
}
