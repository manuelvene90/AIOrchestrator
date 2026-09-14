namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// WHAT IS WAITING TO BE SAID. Jobs keep their order; slots keep only their latest value.
///
/// <para>
/// NOT PERSISTED, and that is a design choice rather than an omission. The durable record of the
/// conversation is the channel files, and the tailer's offset advances only on a pump-CONFIRMED
/// delivery (see <see cref="OutboundOutcome"/>), so a process that dies with a full queue re-emits
/// exactly what did not go out. A second durable queue could only disagree with the first.
/// </para>
/// <para>
/// SNAPSHOT OFFERS ONLY WHAT IS ELIGIBLE — each order key's HEAD, plus every slot — so head-of-line
/// blocking is a property of this class and not a rule the scheduler has to remember. A retrying
/// message blocks its own channel and no other.
/// </para>
/// <para>
/// LOCKED: the mirror tick, the inbound loop and the pump all touch this at once.
/// </para>
/// </summary>
public sealed class OutboundQueue
{
    readonly Lock _lock = new();
    readonly Dictionary<string, List<OutboundIntent>> _jobsByOrderKey = [];
    readonly Dictionary<string, OutboundIntent> _slotsByKey = [];

    int _droppedBySupersession;

    public void Publish(OutboundIntent intent)
    {
        lock (_lock)
        {
            if (intent.Kind == OutboundKinds.Slot)
            {
                var slotKey = intent.SlotKey
                    ?? throw new ArgumentException("a slot must carry a slot key", nameof(intent));

                if (_slotsByKey.ContainsKey(slotKey))
                    _droppedBySupersession++;

                _slotsByKey[slotKey] = intent;

                return;
            }

            var orderKey = intent.OrderKey
                ?? throw new ArgumentException("a job must carry an order key", nameof(intent));

            if (!_jobsByOrderKey.TryGetValue(orderKey, out var queued))
                _jobsByOrderKey[orderKey] = queued = [];

            queued.Add(intent);
        }
    }

    /// <summary>Every intent that could go next: each order key's head, and every slot.</summary>
    public IReadOnlyList<OutboundIntent> Snapshot()
    {
        lock (_lock)
        {
            List<OutboundIntent> eligible = [.. _slotsByKey.Values];

            foreach (var queued in _jobsByOrderKey.Values)
            {
                if (queued.Count > 0)
                    eligible.Add(queued[0]);
            }

            return eligible;
        }
    }

    /// <summary>
    /// Drops an intent the pump has finished with. FALSE is not an error: it means a slot was
    /// overwritten while it was on the wire, so a newer value won and must not be deleted with it.
    /// </summary>
    public bool Remove(OutboundIntent intent)
    {
        lock (_lock)
        {
            if (intent.Kind == OutboundKinds.Slot)
            {
                var slotKey = intent.SlotKey!;

                // REFERENCE EQUALITY, NOT VALUE EQUALITY. Two repaints of an unchanged surface are
                // equal records, so a value comparison here would delete a newer value that has
                // never been sent. What is being removed is THIS intent — the one that went out.
                if (_slotsByKey.TryGetValue(slotKey, out var current) && ReferenceEquals(current, intent))
                {
                    _slotsByKey.Remove(slotKey);

                    return true;
                }

                return false;
            }

            var orderKey = intent.OrderKey!;

            if (_jobsByOrderKey.TryGetValue(orderKey, out var queued)
                && queued.Count > 0
                && ReferenceEquals(queued[0], intent))
            {
                queued.RemoveAt(0);

                if (queued.Count == 0)
                    _jobsByOrderKey.Remove(orderKey);

                return true;
            }

            return false;
        }
    }

    public int Count_Waiting()
    {
        lock (_lock)
            return _slotsByKey.Count + _jobsByOrderKey.Values.Sum(queued => queued.Count);
    }

    /// <summary>
    /// How many repaints were replaced before they ever went out — the number that proves coalescing
    /// is doing its job, and the one to watch if the cosmetic band ever looks slow.
    /// </summary>
    public int Count_Dropped_BySupersession()
    {
        lock (_lock)
            return _droppedBySupersession;
    }
}
