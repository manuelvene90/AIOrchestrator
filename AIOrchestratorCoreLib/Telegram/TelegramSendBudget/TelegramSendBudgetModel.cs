namespace AIOrchestratorCoreLib.Telegram.TelegramSendBudget;

internal sealed class TelegramSendBudgetModel : ITelegramSendBudget
{
    /// <summary>
    /// One lock for both buckets. Sends arrive from the mirror tick and the inbound batch at the
    /// same time, and a bucket read-modify-written from two threads hands the same token out twice —
    /// which is the burst it exists to prevent. Two locks would buy nothing: neither critical
    /// section does I/O.
    /// </summary>
    readonly Lock _lock = new();

    double _sendTokens;
    DateTime _sendRefilledUtc;

    // EMPTY AT EVERY START, unlike the send bucket, and it is not an oversight. Nothing carries it
    // over because nothing needs to: the app's edit traffic is a steady drip with occasional
    // bursts, so the only thing a full start would buy is the right to fire thirty edits in the
    // first second after a crash loop — the exact runaway the bucket was added to stop. The price
    // is that the first edit of a run waits two seconds.
    double _controlTokens;
    DateTime _controlRefilledUtc = DateTime.UtcNow;

    /// <summary>
    /// WHEN EACH MESSAGE WAS LAST EDITED — a per-message gate, because Telegram throttles edits of
    /// one message far harder than calls to the group (measured 2026-09-10; see
    /// <see cref="TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE"/>).
    ///
    /// <para>
    /// BOUNDED BY PRUNING, not by a cap on count. The app edits a handful of long-lived messages —
    /// one PULSE per topic, one dashboard, the receipts — so the natural size is small; what would
    /// grow it without limit is a long run through many closed topics. An entry older than the gate
    /// can never hold anything back, so it is dropped when the map is next touched, which keeps this
    /// bounded by the number of messages edited in the last thirty seconds rather than ever.
    /// </para>
    /// </summary>
    readonly Dictionary<long, DateTime> _lastEditUtcByMessageId = [];

    /// <summary>
    /// The per-message gap this budget enforces — <see cref="TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE"/>
    /// from every production factory; shorter only through <see cref="TelegramSendBudget_Factory.Create_WithEditGap"/>,
    /// the test seam that lets an engine test watch a held edit land without waiting thirty seconds.
    /// </summary>
    readonly TimeSpan _editGap;

    internal TelegramSendBudgetModel(double sendTokens, DateTime sendRefilledUtc, TimeSpan editGap)
    {
        _sendTokens = sendTokens;
        _sendRefilledUtc = sendRefilledUtc;
        _editGap = editGap;
    }

    /// <summary>
    /// THE ONE MESSAGE THE PER-MESSAGE GAP DOES NOT HOLD — the live <c>/settings</c> menu (plan 04 D7,
    /// 2026-09-23), or null.
    ///
    /// <para>
    /// TWO TRAFFIC SHAPES, AND THE GAP WAS MEASURED AGAINST ONLY ONE. The 30 s floor
    /// (<see cref="TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE"/>) comes from the 2026-09-10
    /// 429 storm on the VPS: 376 of 388 HTTP 429 in one hour were two topic status lines re-edited by an
    /// APP-DRIVEN loop at the 2 s tick against a <c>retry_after</c> of 20-34 s. That is a machine editing
    /// every open topic's message on a timer, unbounded by anything but the tick. A settings menu is the
    /// other shape: ONE message, edited only when a human taps it, bounded by human speed — category, page
    /// two, a setting, a value, back is five edits in five seconds, and under the floor taps two to five
    /// would each be refused (<see cref="MessageEditSlot_Gate"/> throws rather than sleeps), so the menu
    /// could not work at all as the spec describes it.
    /// </para>
    /// <para>
    /// WHAT STILL GOVERNS IT: the control bucket (<see cref="TokenBucket_Gate.CONTROL_CAPACITY"/>, 60 a
    /// minute) is spent by every edit of this message exactly as by any other, and Telegram's own
    /// <c>retry_after</c> for it is still honoured by the client's cooldown note, which is checked BEFORE
    /// this gate. Only the per-message floor is lifted.
    /// </para>
    /// <para>
    /// NARROW BY CONSTRUCTION: one id, not a set. Exempting the next menu releases the previous, the menu
    /// releases it when it is closed or replaced, and an edit of the exempt message still stamps its time —
    /// so the moment it is released it owes the gap from its last edit like any other message. Pinned by
    /// <c>TelegramSendBudgetTests.TheLiveSettingsMenu_IsNeverHeld_ButAnyOtherMessageStillGetsTheThirtySecondGap</c>.
    /// </para>
    /// </summary>
    long? _editGapExemptMessageId;

    public void Exempt_FromEditGap(long messageId)
    {
        lock (_lock)
            _editGapExemptMessageId = messageId;
    }

    public void Release_EditGapExemption(long messageId)
    {
        lock (_lock)
        {
            if (_editGapExemptMessageId == messageId)
                _editGapExemptMessageId = null;
        }
    }

    public TimeSpan Reserve_MessageEdit(long messageId, DateTime nowUtc)
    {
        lock (_lock)
        {
            var gap = _editGap;

            Prune_StaleEdits(nowUtc, gap);

            // THE LIVE SETTINGS MENU (D7): stamped like any other edit, so a release puts it straight
            // back under the gap measured from this edit — and never held while it is the live one.
            if (_editGapExemptMessageId == messageId)
            {
                _lastEditUtcByMessageId[messageId] = nowUtc;

                return TimeSpan.Zero;
            }

            if (!_lastEditUtcByMessageId.TryGetValue(messageId, out var lastEdit))
            {
                // FIRST EDIT OF THIS MESSAGE GOES STRAIGHT OUT. The gate is about a REPEATED edit
                // of the same message; making the first one wait would delay every status line by
                // half a minute after every restart for nothing.
                _lastEditUtcByMessageId[messageId] = nowUtc;

                return TimeSpan.Zero;
            }

            var elapsed = nowUtc - lastEdit;

            if (elapsed >= gap)
            {
                _lastEditUtcByMessageId[messageId] = nowUtc;

                return TimeSpan.Zero;
            }

            // THE STAMP IS LEFT ALONE. The previous version moved it to `lastEdit + gap` because it
            // was about to SLEEP until then, and two sleepers had to be kept from waking on the same
            // instant. Nobody sleeps here any more, so advancing it would push the door away by a
            // further gap every time a surface asked and was turned back — a tick-rate caller would
            // never be let through at all.
            return gap - elapsed;
        }
    }

    /// <summary>
    /// Drops entries that can no longer hold anything back. Called under the lock, from the one
    /// method that touches the map.
    /// </summary>
    void Prune_StaleEdits(DateTime now, TimeSpan gap)
    {
        if (_lastEditUtcByMessageId.Count == 0)
            return;

        List<long>? expired = null;

        foreach (var (messageId, lastEdit) in _lastEditUtcByMessageId)
        {
            if (now - lastEdit >= gap)
                (expired ??= []).Add(messageId);
        }

        if (expired == null)
            return;

        foreach (var messageId in expired)
            _lastEditUtcByMessageId.Remove(messageId);
    }

    public Task Wait_ForSend_Async(CancellationToken cancellationToken)
    {
        return Wait_Async(
            () =>
            {
                var (tokens, refilledUtc, wait) = TokenBucket_Gate.Take(_sendTokens, _sendRefilledUtc, DateTime.UtcNow);
                _sendTokens = tokens;
                _sendRefilledUtc = refilledUtc;
                return wait;
            },
            cancellationToken);
    }

    public Task Wait_ForControl_Async(CancellationToken cancellationToken)
    {
        return Wait_Async(
            () =>
            {
                var (tokens, refilledUtc, wait) = TokenBucket_Gate.Take(
                    _controlTokens, _controlRefilledUtc, DateTime.UtcNow,
                    TokenBucket_Gate.CONTROL_CAPACITY, TokenBucket_Gate.DEFAULT_REFILL_SECONDS);

                _controlTokens = tokens;
                _controlRefilledUtc = refilledUtc;
                return wait;
            },
            cancellationToken);
    }

    public (double Tokens, DateTime RefilledUtc) Read_SendState()
    {
        lock (_lock)
            return (_sendTokens, _sendRefilledUtc);
    }

    /// <summary>
    /// Recomputed after each sleep rather than sleeping the whole predicted wait in one go: several
    /// senders race here, and the one that wakes first should take the token that actually became
    /// available, not the one that was promised to it a second ago.
    /// </summary>
    async Task Wait_Async(Func<TimeSpan> take, CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;

            lock (_lock)
                wait = take();

            if (wait <= TimeSpan.Zero)
                return;

            await Task.Delay(wait, cancellationToken);
        }
    }
}
