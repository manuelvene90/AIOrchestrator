namespace AIOrchestratorCoreLib.Bridge.ReceiptRegistry;

/// <inheritdoc cref="IReceiptRegistry"/>
internal sealed class ReceiptRegistryModel : IReceiptRegistry
{
    /// <summary>The key a thread id is stored under — the General topic has none, and is 0.</summary>
    const long GENERAL_TOPIC_KEY = 0;

    readonly Lock _gate = new();
    readonly Dictionary<long, long> _tickMessageIdByThread = [];
    readonly Dictionary<long, long> _reactedOwnerMessageIdByThread = [];

    /// <summary>
    /// Keyed by MESSAGE, not by thread: the slot belongs to the screen the owner is looking at, and an old
    /// receipt still waiting for its door must not be confused with the next exchange's ✓.
    /// </summary>
    readonly Dictionary<long, PendingEdit> _pendingEditByMessageId = [];
    long _lastEditVersion;

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

    public bool Adopt_Tick(long? messageThreadId, long messageId)
    {
        var key = messageThreadId ?? GENERAL_TOPIC_KEY;

        lock (_gate)
        {
            if (_reactedOwnerMessageIdByThread.ContainsKey(key))
                return false;

            _tickMessageIdByThread[key] = messageId;
            return true;
        }
    }

    public (long MessageId, long Version, string Text, IReadOnlyList<(string Data, string Label)>? Buttons, int AttemptNumber, string LogScope)? Stage_Edit_OrNull(
        long messageId, string text, IReadOnlyList<(string Data, string Label)>? buttons, string logScope, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (!_pendingEditByMessageId.TryGetValue(messageId, out var pending))
            {
                pending = new PendingEdit { NotBeforeUtc = DateTime.MinValue };
                _pendingEditByMessageId[messageId] = pending;
            }

            pending.Text = text;
            pending.Buttons = buttons;
            pending.LogScope = logScope;
            pending.Version = ++_lastEditVersion;

            // A NEW TEXT IS A NEW EDIT, with the retry policy's whole allowance. The door is the MESSAGE'S and
            // is kept: a newer text does not open a door the older one was turned back from.
            pending.AttemptsMade = 0;

            if (pending.InFlight || pending.NotBeforeUtc > nowUtc)
                return null;

            return Begin_Attempt(messageId, pending);
        }
    }

    public IReadOnlyList<(long MessageId, long Version, string Text, IReadOnlyList<(string Data, string Label)>? Buttons, int AttemptNumber, string LogScope)> Take_DueEdits(DateTime nowUtc)
    {
        lock (_gate)
        {
            if (_pendingEditByMessageId.Count == 0)
                return [];

            List<(long, long, string, IReadOnlyList<(string Data, string Label)>?, int, string)> due = [];

            foreach (var (messageId, pending) in _pendingEditByMessageId)
            {
                if (!pending.InFlight && pending.NotBeforeUtc <= nowUtc)
                    due.Add(Begin_Attempt(messageId, pending));
            }

            return due;
        }
    }

    public void Settle_EditLanded(long messageId, long version)
    {
        lock (_gate)
        {
            if (!_pendingEditByMessageId.TryGetValue(messageId, out var pending))
                return;

            if (pending.Version == version)
            {
                _pendingEditByMessageId.Remove(messageId);
                return;
            }

            // NEWER TEXT ARRIVED WHILE THIS ONE WAS ON THE WIRE, and the message has just been edited, so the
            // gap now holds it. Due at once anyway: the next attempt is refused BEFORE any call is made and
            // comes back with the exact door, which is cheaper than this store guessing the gap.
            pending.InFlight = false;
            pending.NotBeforeUtc = DateTime.MinValue;
        }
    }

    public void Settle_EditDeferred(long messageId, DateTime notBeforeUtc)
    {
        lock (_gate)
        {
            if (!_pendingEditByMessageId.TryGetValue(messageId, out var pending))
                return;

            pending.InFlight = false;
            pending.NotBeforeUtc = notBeforeUtc;
        }
    }

    public void Settle_EditAbandoned(long messageId, long version)
    {
        lock (_gate)
        {
            if (!_pendingEditByMessageId.TryGetValue(messageId, out var pending))
                return;

            if (pending.Version == version)
            {
                _pendingEditByMessageId.Remove(messageId);
                return;
            }

            pending.InFlight = false;
        }
    }

    public bool Has_PendingEdit(long messageId)
    {
        lock (_gate)
            return _pendingEditByMessageId.ContainsKey(messageId);
    }

    /// <summary>Marks the slot in flight and counts the attempt. Called under <c>_gate</c>.</summary>
    static (long MessageId, long Version, string Text, IReadOnlyList<(string Data, string Label)>? Buttons, int AttemptNumber, string LogScope) Begin_Attempt(
        long messageId, PendingEdit pending)
    {
        pending.InFlight = true;
        pending.AttemptsMade++;

        return (messageId, pending.Version, pending.Text, pending.Buttons, pending.AttemptsMade, pending.LogScope);
    }

    /// <summary>
    /// One message's wanted state. MUTABLE AND PRIVATE, like the engine's own per-exchange trackers: it is
    /// rewritten in place by every writer of the message and never leaves this class except as a tuple copy.
    /// </summary>
    sealed class PendingEdit
    {
        public string Text = "";
        public IReadOnlyList<(string Data, string Label)>? Buttons;
        public string LogScope = "";

        /// <summary>Which staging this is — a settle for an older one must not clear a newer text.</summary>
        public long Version;

        /// <summary>When the message's door opens; <see cref="DateTime.MinValue"/> is "now".</summary>
        public DateTime NotBeforeUtc;

        /// <summary>One attempt at a time per message, so two loops never race the same door.</summary>
        public bool InFlight;

        /// <summary>Attempts at the CURRENT text, the one in flight included — what the retry policy counts.</summary>
        public int AttemptsMade;
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
