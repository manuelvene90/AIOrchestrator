namespace AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;

internal sealed class OwnerDeliveryBufferModel(int aggregationSeconds, int holdCapSeconds) : IOwnerDeliveryBuffer
{
    /// <summary>
    /// One buffered segment and the ORDINAL it arrived with. The ordinal is what makes ordering
    /// survive a put-back: position cannot, because position is the thing under contention.
    /// </summary>
    readonly record struct Segment(long Ordinal, string Text);

    sealed class PendingDelivery
    {
        public readonly List<Segment> Segments = [];
        public DateTime LastArrivalUtc;
        public bool Held;
        public bool ReleaseRequested;
    }

    readonly int _aggregationSeconds = aggregationSeconds;
    readonly int _holdCapSeconds = holdCapSeconds;
    readonly Dictionary<string, PendingDelivery> _pending = [];
    readonly Lock _lock = new();

    /// <summary>
    /// Assigned to every segment on arrival and never reused. Monotonic under <c>_lock</c>, so two
    /// segments buffered from different loops still receive a total order.
    /// </summary>
    long _nextOrdinal;

    public void Add_Segment(string targetKey, string segment, DateTime nowUtc)
    {
        lock (_lock)
        {
            var delivery = Get_OrCreate(targetKey);

            delivery.Segments.Add(new Segment(_nextOrdinal++, segment));
            delivery.LastArrivalUtc = nowUtc;
        }
    }

    public void Restore_Segment(string targetKey, string segment, long ordinal)
    {
        lock (_lock)
        {
            // LastArrivalUtc deliberately untouched — see the interface. Refreshing it would make the
            // owner serve a second aggregation window for a failure they know nothing about, which is
            // the same reason the callers pair this with Release.
            //
            // Appended, NOT inserted at the front: the ORDINAL decides the order now, so where it
            // lands in the list is irrelevant. Inserting at index 0 was the position-based attempt
            // this replaces, and it inverted whenever two put-backs were in flight.
            Get_OrCreate(targetKey).Segments.Add(new Segment(ordinal, segment));
        }
    }

    public void Hold(string targetKey, DateTime nowUtc)
    {
        lock (_lock)
        {
            var delivery = Get_OrCreate(targetKey);

            delivery.Held = true;
            delivery.ReleaseRequested = false;

            // A WAIT with nothing buffered yet still starts the idle clock, so a forgotten hold
            // cannot sit there forever waiting for a first message that never comes.
            delivery.LastArrivalUtc = nowUtc;
        }
    }

    public void Release(string targetKey)
    {
        lock (_lock)
        {
            if (!_pending.TryGetValue(targetKey, out var delivery))
                return;

            delivery.Held = false;
            delivery.ReleaseRequested = true;
        }
    }

    public bool Is_Holding(string targetKey)
    {
        lock (_lock)
        {
            return _pending.TryGetValue(targetKey, out var delivery) && delivery.Held;
        }
    }

    public int Count_Pending(string targetKey)
    {
        lock (_lock)
        {
            return _pending.TryGetValue(targetKey, out var delivery) ? delivery.Segments.Count : 0;
        }
    }

    public IReadOnlyDictionary<string, IReadyDelivery> Take_ReadyDeliveries(DateTime nowUtc)
    {
        Dictionary<string, IReadyDelivery> ready = [];

        lock (_lock)
        {
            List<string> readyKeys = [];
            List<string> emptyKeys = [];

            foreach (var pair in _pending)
            {
                if (!Is_Ready(pair.Value, nowUtc))
                    continue;

                // A hold that only ever received WAIT (or WAIT then GO) has nothing to deliver —
                // drop the entry rather than sending the session an empty entry.
                if (pair.Value.Segments.Count == 0)
                {
                    emptyKeys.Add(pair.Key);
                    continue;
                }

                readyKeys.Add(pair.Key);
            }

            foreach (var key in emptyKeys)
                _pending.Remove(key);

            foreach (var key in readyKeys)
            {
                // BY ORDINAL, not by list position: a restored segment is appended, so list order is
                // arrival-of-the-put-back rather than arrival-of-the-message.
                var ordered = _pending[key].Segments.OrderBy(segment => segment.Ordinal).ToList();

                ready[key] = new ReadyDelivery(
                    string.Join("\n\n", ordered.Select(segment => segment.Text)),
                    ordered[0].Ordinal);

                _pending.Remove(key);
            }
        }

        return ready;
    }

    bool Is_Ready(PendingDelivery delivery, DateTime nowUtc)
    {
        // GO: deliver now, no window — the owner has said they are done typing.
        if (delivery.ReleaseRequested)
            return true;

        var idleSeconds = (nowUtc - delivery.LastArrivalUtc).TotalSeconds;

        // Held: nothing goes out until GO, EXCEPT after a long silence. Without that cap a
        // forgotten WAIT would swallow the owner's messages indefinitely, and the session would sit
        // idle waiting for traffic it can never receive.
        if (delivery.Held)
            return idleSeconds >= _holdCapSeconds;

        return idleSeconds >= _aggregationSeconds;
    }

    PendingDelivery Get_OrCreate(string targetKey)
    {
        if (_pending.TryGetValue(targetKey, out var existing))
            return existing;

        var created = new PendingDelivery();
        _pending[targetKey] = created;
        return created;
    }

    public bool Has_PendingDeliveries()
    {
        lock (_lock)
        {
            return _pending.Count > 0;
        }
    }
}
