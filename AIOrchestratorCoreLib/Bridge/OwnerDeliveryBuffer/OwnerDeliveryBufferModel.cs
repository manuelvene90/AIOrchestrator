namespace AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;

internal sealed class OwnerDeliveryBufferModel(int aggregationSeconds) : IOwnerDeliveryBuffer
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
    readonly Dictionary<string, PendingDelivery> _pending = [];

    /// <summary>
    /// WHEN EACH TARGET'S HOLD LAST SAW TRAFFIC — the clock the cap is measured against, and the
    /// reason the hold is no longer a flag on PendingDelivery.
    ///
    /// HONEST ABOUT WHAT THIS DID AND DID NOT FIX. Moving the flag out was first justified as a bug
    /// — "a PendingDelivery is removed when it is taken, so the hold goes with it" — and a mutation
    /// test refuted that: `Hold` calls `Get_OrCreate`, so an entry always exists while a hold does,
    /// and the two readings agreed in every reachable sequence. The claim was wrong and is recorded
    /// here rather than quietly deleted.
    ///
    /// What was REAL is the cap: a hold ended after sixty idle seconds and said nothing, so the
    /// receipt reverted to delivered with no explanation. That is what the owner saw and reported as
    /// "it should never become tick tick" (2026-08-20).
    ///
    /// The state lives here anyway, because a hold IS a property of the conversation rather than of
    /// whichever delivery happened to be buffered when it started — and because a lapse can only be
    /// reported once from somewhere that outlives the entry.
    /// </summary>
    readonly Dictionary<string, DateTime> _heldSinceUtc = [];
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

            // THE HOLD CLOCK IS AN IDLE CLOCK, exactly as Is_Ready's cap always was: a hold lapses
            // because it was FORGOTTEN, and someone still typing has plainly not forgotten it.
            // Measuring from when the hold began instead would end it mid-conversation, which is a
            // new way to do the very thing being fixed.
            if (_heldSinceUtc.ContainsKey(targetKey))
                _heldSinceUtc[targetKey] = nowUtc;
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

            // Re-pressing WAIT does NOT restart the cap: the clock measures how long this hold has
            // been forgotten, and a second press is not evidence that it was remembered.
            _heldSinceUtc.TryAdd(targetKey, nowUtc);

            // A WAIT with nothing buffered yet still starts the idle clock, so a forgotten hold
            // cannot sit there forever waiting for a first message that never comes.
            delivery.LastArrivalUtc = nowUtc;
        }
    }

    public void Release(string targetKey)
    {
        lock (_lock)
        {
            // Cleared FIRST and unconditionally: a GO must end the hold even when nothing is
            // buffered, which is precisely the case the old early-return dropped on the floor.
            _heldSinceUtc.Remove(targetKey);

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
            return _heldSinceUtc.ContainsKey(targetKey);
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
                if (!Is_Ready(pair.Key, pair.Value, nowUtc))
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

    bool Is_Ready(string targetKey, PendingDelivery delivery, DateTime nowUtc)
    {
        // GO: deliver now, no window — the owner has said they are done typing.
        if (delivery.ReleaseRequested)
            return true;

        var idleSeconds = (nowUtc - delivery.LastArrivalUtc).TotalSeconds;

        // Held: nothing goes out until GO, EXCEPT after a long silence. Without that cap a
        // forgotten WAIT would swallow the owner's messages indefinitely, and the session would sit
        // idle waiting for traffic it can never receive.
        //
        // A HOLD ENDS ONLY WITH GO. It used to lapse after sixty idle seconds, and it lapsed in
        // SILENCE — the receipt reverted to delivered with no explanation, which is what the owner
        // reported as "it should never become tick tick" (2026-08-20).
        //
        // Their earlier comment defended the cap: "a forgotten WAIT must not swallow the owner's
        // messages forever". They overruled it themselves once the real behaviour was clear, and the
        // reason it is now safe is that a hold is VISIBLE: the receipt says ⏸ holding for as long as
        // it lasts, so a forgotten hold is something they can see and end, rather than a silence
        // they have to deduce. A timer that guesses when they stopped caring is not needed when the
        // state is in front of them.
        if (_heldSinceUtc.ContainsKey(targetKey))
            return false;

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
