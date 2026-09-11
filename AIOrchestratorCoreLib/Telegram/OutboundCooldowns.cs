namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// WHAT TELEGRAM HAS ALREADY TOLD US, remembered where every caller can read it — the note on the
/// door. <see cref="OutboundCooldown_Gate"/> is the arithmetic; this is the book, and it is the
/// only stateful half.
///
/// <para>
/// THE DEFECT IT CLOSES. Before this, a 429 was an exception handed to whichever call site happened
/// to receive it, and each of the ~40 of them had its own opinion about what to do next: some back
/// off, some are one-shot, and three (the crash-loop, stall and usage-limit alerts) have no
/// back-off at all — they set their "alerted" flag only on a confirmed send, so a failing send
/// re-fires every 2 s for ever. None of them can learn what another was told. This is the shared
/// answer they were missing, placed on the road they all already travel.
/// </para>
/// <para>
/// PER TARGET, NOT PER CHAT — and that is a narrowing of the design doc, made deliberately.
/// python-telegram-bot halts EVERY request on a 429 and that is defensible, but priority bands do
/// not exist yet: a chat-wide hold would put the owner's tap behind a status line's punishment,
/// which is precisely the inversion this work exists to remove. The refused target is the one thing
/// Telegram actually told us about; the chat-wide hold arrives with the bands, in the queue.
/// </para>
/// <para>
/// A 429 STORM MUST NOT BECOME A LOG STORM. 388 lines in an hour said the same thing 194 times per
/// topic. <see cref="Note_RateLimited"/> returns whether the window is NEWS — true when it opens,
/// false while it merely counts down — so the caller speaks twice per window instead of once per
/// attempt.
/// </para>
/// <para>
/// LOCKED, because the mirror tick, the inbound loop and the owner's own taps all run concurrently
/// and all pass through the one client this book belongs to.
/// </para>
/// </summary>
public sealed class OutboundCooldowns
{
    readonly object _lock = new();
    readonly Dictionary<string, DateTime> _deadlineByTarget = [];

    /// <summary>
    /// Records Telegram's own wait for <paramref name="target"/>, and says whether the window is
    /// NEW — i.e. whether this is worth a log line.
    ///
    /// <para>
    /// A LATER, SMALLER NUMBER NEVER SHORTENS THE WINDOW. Two surfaces can be refused on the same
    /// message inside one window — at 21:11:40 the rewrite after a tap and the keyboard-removal
    /// fallback were both refused with <c>retry after 24</c> — and Telegram's countdown means the
    /// second number is the SAME window seen later. Honouring it would re-open the door early,
    /// which is how the storm sustained itself.
    /// </para>
    /// </summary>
    public bool Note_RateLimited(string target, int? retryAfterSeconds, DateTime nowUtc)
    {
        var deadline = OutboundCooldown_Gate.Deadline_From_RetryAfter(retryAfterSeconds, nowUtc);

        lock (_lock)
        {
            Prune_Expired(nowUtc);

            var wasAlreadyHeld = _deadlineByTarget.TryGetValue(target, out var existing)
                && OutboundCooldown_Gate.Is_Held(existing, nowUtc);

            if (!wasAlreadyHeld || deadline > existing)
                _deadlineByTarget[target] = deadline;

            return !wasAlreadyHeld;
        }
    }

    /// <summary>
    /// Whether <paramref name="target"/> may be attempted now. <paramref name="notBeforeUtc"/>
    /// carries the deadline so a caller that is WORTH the wait — the owner's tap — can wait exactly
    /// as long as Telegram asked instead of guessing.
    /// </summary>
    public bool Is_Held(string target, DateTime nowUtc, out DateTime notBeforeUtc)
    {
        lock (_lock)
        {
            if (_deadlineByTarget.TryGetValue(target, out var deadline)
                && OutboundCooldown_Gate.Is_Held(deadline, nowUtc))
            {
                notBeforeUtc = deadline;

                return true;
            }

            notBeforeUtc = default;

            return false;
        }
    }

    /// <summary>How many doors are shut right now. For the tests and for a diagnostic line.</summary>
    public int Count_OpenWindows(DateTime nowUtc)
    {
        lock (_lock)
        {
            Prune_Expired(nowUtc);

            return _deadlineByTarget.Count;
        }
    }

    /// <summary>
    /// Drops windows that can no longer hold anything back. A daemon that runs for weeks edits a
    /// great many messages, so the book is bounded by the windows still OPEN rather than by
    /// everything it has ever been refused. Called under the lock, from the methods that take it.
    /// </summary>
    void Prune_Expired(DateTime nowUtc)
    {
        if (_deadlineByTarget.Count == 0)
            return;

        var expired = _deadlineByTarget
            .Where(entry => !OutboundCooldown_Gate.Is_Held(entry.Value, nowUtc))
            .Select(entry => entry.Key)
            .ToList();

        foreach (var target in expired)
            _deadlineByTarget.Remove(target);
    }
}
