namespace AIOrchestratorCoreLib.Bridge.ChannelChangeWaker;

internal sealed class ChannelChangeWakerModel : IChannelChangeWaker
{
    readonly string _supervisionRoot;
    readonly Action<string> _logLine;

    readonly object _gate = new();

    /// <summary>
    /// The latch a write completes. REPLACED after every wake rather than reset, because a
    /// <see cref="TaskCompletionSource"/> cannot be un-completed and a waiter reading the old one
    /// while a new one is installed would miss the very write it is waiting for.
    /// </summary>
    TaskCompletionSource _signal = New_Signal();

    /// <summary>
    /// EVERY FIELD BELOW IS GUARDED BY <see cref="_gate"/>, including the ones only the error handler
    /// touches. That handler runs on a thread pool thread the bridge does not own, so a plain bool read
    /// and written from there and from the loop is not the "once" its own comment promises — nothing
    /// orders the two, and a torn or stale read publishes the line twice or never. The gate is
    /// uncontended here (a wake takes it for microseconds), so the honest fix is the cheap one.
    /// </summary>
    FileSystemWatcher? _watcher;

    int _settlingPulsesOwed;
    bool _reportedAnError;
    bool _reportedARearm;
    bool _rearmRequested;

    /// <summary>
    /// Whether the last validity check found the supervision root GONE. A watch is a handle on the
    /// folder that existed then, so a root that disappears and comes back is a watch pointing at
    /// nothing — see <see cref="Check_WatchStillValid"/>.
    /// </summary>
    bool _rootWasMissing;

    /// <summary>The root's creation stamp when the current watcher was armed, where the filesystem reports one.</summary>
    DateTime? _armedOverCreationUtc;

    DateTime _lastValidityCheckUtc = DateTime.MinValue;

    internal ChannelChangeWakerModel(string supervisionRoot, Action<string> logLine)
    {
        _supervisionRoot = supervisionRoot;
        _logLine = logLine;

        lock (_gate)
            Arm_Watcher();
    }

    public async Task Wait_ForChangeOrTick_Async(int tickMilliseconds, CancellationToken cancellationToken)
    {
        if (Take_SettlingPulse())
        {
            // Never LONGER than the tick it stands in for: a caller ticking faster than the settling
            // gap must not be slowed down by the thing that exists to speed it up.
            await Task.Delay(Math.Min(ChannelChangeWaker_Factory.SETTLING_PULSE_MILLISECONDS, tickMilliseconds), cancellationToken);
            return;
        }

        Task? wake;

        lock (_gate)
        {
            // Between two full waits, and nowhere else: a settling pulse is the middle of a burst and a
            // stat there would run at the pulse rate for no new information.
            Check_WatchStillValid();

            // Null means there is nothing that can end this wait early — the bridge exactly as it was
            // before the waker existed. Read under the gate WITH the check above, so a re-arm the check
            // just made is the one this wait uses.
            wake = _watcher == null ? null : _signal.Task;
        }

        if (wake == null)
        {
            await Task.Delay(tickMilliseconds, cancellationToken);
            return;
        }

        using var tickCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var tick = Task.Delay(tickMilliseconds, tickCancellation.Token);

        if (await Task.WhenAny(tick, wake) == tick)
        {
            // Awaited rather than returned from: if what ended the tick was the token, this is the
            // OperationCanceledException the loop above already catches and ends on.
            await tick;
            return;
        }

        // The timer is no longer wanted, and it is OBSERVED as well as cancelled: an abandoned
        // Task.Delay faults on its way out and an unread fault is an UnobservedTaskException raised
        // from the finalizer thread, minutes later, blamed on whatever is running then.
        tickCancellation.Cancel();

        try
        {
            await tick;
        }
        catch (OperationCanceledException)
        {
            // The cancellation this method just asked for.
        }

        // THE DEBOUNCE, and it is why the latch is replaced AFTER it rather than before: every event
        // raised during this pause lands on the latch that is already set, so the whole burst — the
        // several notifications one append raises, and the several appends one crew makes — is one
        // wake. Without it the loop would run a tick per event, on its own writes as well as theirs.
        await Task.Delay(ChannelChangeWaker_Factory.DEBOUNCE_MILLISECONDS, cancellationToken);

        lock (_gate)
        {
            _signal = New_Signal();
            _settlingPulsesOwed = ChannelChangeWaker_Factory.SETTLING_PULSES;
        }
    }

    public void Dispose()
    {
        lock (_gate)
            Dispose_Watcher();
    }

    void On_ChannelWritten(object sender, FileSystemEventArgs args)
    {
        lock (_gate)
            _signal.TrySetResult();
    }

    /// <summary>
    /// The watcher's own failure path — an overflowed event buffer, or a watch the OS dropped. It may
    /// not throw: this runs on a thread pool thread the bridge does not own, and an exception here
    /// would take the process down rather than the loop.
    /// <para>
    /// ONCE PER WAKER. The condition that raises it persists, and a line every burst would bury the log
    /// it is written into. What follows a lost event is not a lost append — it is a late one, found by
    /// the tick.
    /// </para>
    /// <para>
    /// IT ALSO ASKS FOR A RE-ARM, because on Linux the two are the same event: an error here is how a
    /// dropped watch descriptor presents, and .NET does not re-add one.
    /// </para>
    /// </summary>
    void On_WatcherError(object sender, ErrorEventArgs args)
    {
        try
        {
            string? line = null;

            lock (_gate)
            {
                // A dropped event means the loop no longer knows the file changed, so the wake is
                // raised anyway: one extra tick costs nothing and covers whatever the watcher missed.
                _signal.TrySetResult();

                _rearmRequested = true;

                if (!_reportedAnError)
                {
                    _reportedAnError = true;

                    var exception = args.GetException();

                    line =
                        $"Channel-change watcher reported an error ({exception.GetType().Name}: {exception.Message}) — "
                            + "appends it missed are found by the 2 s mirror tick, which is the behaviour the loop had "
                            + "before the watcher existed. Said once: the condition persists and the line would repeat.";
                }
            }

            // OUTSIDE THE GATE. The log is the app's, it writes a file, and holding a lock the wake path
            // takes across somebody else's I/O is how a fast path acquires a slow one's latency.
            if (line != null)
                _logLine(line);
        }
        catch
        {
            // Nothing above may escape onto a pool thread. There is no second reporting route to try:
            // the tick is the safety net and it is already in place.
        }
    }

    /// <summary>
    /// WHETHER THE WATCH STILL POINTS AT ANYTHING — one stat, between full waits, and the reason it
    /// exists is a platform this machine is not.
    ///
    /// <para>
    /// Probed on macOS on 2026-09-09: deleting and recreating the supervision root cost the waker
    /// nothing — it was reacting again 306 ms later, with no error raised and no line logged. On Linux
    /// (which is what the VPS runs) inotify drops the watch descriptor when the watched directory goes,
    /// and .NET does not re-add it, so the wake would be dead for the life of the loop and the mirror
    /// would silently fall back to the 2 s tick for ever. THAT BEHAVIOUR IS INFERRED, NOT MEASURED HERE:
    /// the check below is written for the platform it cannot reproduce, which is why it is a stat and
    /// not a redesign.
    /// </para>
    /// <para>
    /// TWO DETECTORS, because neither is sufficient alone. A root that is missing and then present again
    /// is the delete-and-recreate case caught directly; a creation stamp that has moved catches the same
    /// thing when both halves happened between two checks. Where the filesystem reports no creation time
    /// the second detector simply never fires — a false negative, which costs the optimisation and never
    /// costs an append, since the tick is still underneath.
    /// </para>
    /// <para>
    /// Runs with <see cref="_gate"/> HELD.
    /// </para>
    /// </summary>
    void Check_WatchStillValid()
    {
        var nowUtc = DateTime.UtcNow;

        // At most once per mirror tick. Between wakes this method is reached several times a second,
        // and the folder cannot usefully be asked that often.
        if (!_rearmRequested && nowUtc - _lastValidityCheckUtc < TimeSpan.FromMilliseconds(ChannelChangeWaker_Factory.VALIDITY_CHECK_MILLISECONDS))
            return;

        _lastValidityCheckUtc = nowUtc;

        bool exists;
        DateTime? creationUtc = null;

        try
        {
            exists = Directory.Exists(_supervisionRoot);

            if (exists)
                creationUtc = Directory.GetCreationTimeUtc(_supervisionRoot);
        }
        catch (Exception ex)
        {
            // IT SAYS SO AND CARRIES ON — it does not invent a verdict it could not reach. A root this
            // process cannot stat is not evidence that the watch is dead, and re-arming on it would
            // churn a handle every tick.
            Report_Once(ref _reportedARearm,
                $"Channel-change watcher could not check whether its watch is still valid "
                    + $"({ex.GetType().Name}: {ex.Message}) — the wake may be dead and the mirror loop is on its "
                    + "2 s tick until the bridge restarts. Appends are late, never lost.");

            return;
        }

        if (!exists)
        {
            // Nothing to watch yet. Recorded, not reported: a root that is not there is the state the
            // factory already said one line about, and this runs every tick.
            _rootWasMissing = true;
            return;
        }

        var rootCameBack = _rootWasMissing;
        var rootIsANewFolder = _watcher != null && _armedOverCreationUtc != null && creationUtc != _armedOverCreationUtc;

        if (!_rearmRequested && !rootCameBack && !rootIsANewFolder)
            return;

        _rootWasMissing = false;
        _rearmRequested = false;

        Dispose_Watcher();
        Arm_Watcher();

        // "NO LONGER IN PLACE" covers both shapes: a folder that was replaced under a live watch, and one
        // that did not exist when the waker was built and does now. Saying "lost" would be a guess about
        // which, from a stat that cannot tell them apart.
        Report_Once(ref _reportedARearm,
            _watcher == null
                ? $"Channel-change watch over '{_supervisionRoot}' is no longer in place and could NOT be armed "
                    + "again — the mirror loop is on its 2 s tick, which is the behaviour it had before the watcher "
                    + "existed. Said once."
                : $"Channel-change watch over '{_supervisionRoot}' was no longer in place (the folder was replaced, "
                    + "or the OS dropped the watch) and has been armed again. Appends during the gap were late, "
                    + "never lost: the 2 s mirror tick is underneath. Said once.");
    }

    /// <summary>
    /// Builds and enables the watcher, or leaves <see cref="_watcher"/> null and says so ONCE. A machine
    /// that refuses a watch is not an error the bridge propagates: the loop keeps its tick and works
    /// exactly as it did before this class existed.
    /// <para>
    /// Runs with <see cref="_gate"/> HELD.
    /// </para>
    /// </summary>
    void Arm_Watcher()
    {
        FileSystemWatcher? watcher = null;

        try
        {
            _armedOverCreationUtc = Directory.Exists(_supervisionRoot)
                ? Directory.GetCreationTimeUtc(_supervisionRoot)
                : null;

            watcher = ChannelChangeWaker_Factory.Create_Watcher(_supervisionRoot);

            watcher.Changed += On_ChannelWritten;
            watcher.Created += On_ChannelWritten;
            watcher.Renamed += On_ChannelWritten;

            // Deleted too, and not for the deletion's sake: compaction MOVES a channel's older half into
            // a sibling .archive.md, and the loop reading the survivors one tick sooner is the same win.
            watcher.Deleted += On_ChannelWritten;

            watcher.Error += On_WatcherError;

            // WHERE THE REAL FAILURES LAND. Constructing the watcher only checks the folder; arming it is
            // what asks the OS for a notification handle, so an exhausted inotify budget or a filesystem
            // that cannot watch throws HERE, not above.
            watcher.EnableRaisingEvents = true;

            _watcher = watcher;
        }
        catch (Exception ex)
        {
            watcher?.Dispose();
            _watcher = null;
            _armedOverCreationUtc = null;

            Report_Once(ref _reportedAnError,
                $"Channel-change watcher could not be armed over '{_supervisionRoot}' "
                    + $"({ex.GetType().Name}: {ex.Message}) — the mirror loop keeps its tick, which is exactly the "
                    + "behaviour it had before the watcher existed. Appends are late, never lost.");
        }
    }

    /// <summary>Runs with <see cref="_gate"/> HELD.</summary>
    void Dispose_Watcher()
    {
        if (_watcher == null)
            return;

        var watcher = _watcher;
        _watcher = null;

        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        catch (Exception ex)
        {
            // A watcher that will not shut down cleanly is not worth the loop it is being disposed
            // alongside — this runs while the bridge is already stopping, or while a replacement is
            // being armed over the same folder.
            _logLine($"Channel-change watcher did not dispose cleanly ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    /// <summary>
    /// One line per condition per waker. The flag is BY REFERENCE so each condition keeps its own, which
    /// is what "once" has always meant here: an arming failure and a lost watch are two different things
    /// to say, and one must not silence the other.
    /// <para>
    /// Runs with <see cref="_gate"/> HELD, and logs inside it — unlike the error handler, every caller
    /// of this one is already on the loop's own thread, so there is no fast path to lend a slow lock to.
    /// </para>
    /// </summary>
    void Report_Once(ref bool alreadyReported, string line)
    {
        if (alreadyReported)
            return;

        alreadyReported = true;

        _logLine(line);
    }

    static TaskCompletionSource New_Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    bool Take_SettlingPulse()
    {
        lock (_gate)
        {
            if (_settlingPulsesOwed <= 0)
                return false;

            _settlingPulsesOwed--;
            return true;
        }
    }
}
