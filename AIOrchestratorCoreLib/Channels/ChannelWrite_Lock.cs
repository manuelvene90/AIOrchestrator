namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Serialises writes to one channel file — this process's threads against each other, and this
/// process against the agent sessions, so a read-then-append and a whole-file rewrite cannot
/// overlap on the same channel.
/// <para>
/// Why it exists: the bridge appends from ~35 call sites spread across the mirror loop and the
/// inbound loop, which <c>Run_Async</c> starts together and awaits as a pair, and compaction
/// rewrites the same files from the mirror loop. Nothing serialised any of it. Two consequences
/// were live: <c>ChannelAppender</c> picks its index from a read that is not atomic with its
/// append, so two appenders can pick the same one; and <c>File.AppendAllText</c> opens the target
/// deny-write, so a second concurrent appender does not interleave — it THROWS, and the entry it
/// carried is never written.
/// </para>
/// <para>
/// WHAT THIS DOES NOT COVER, stated plainly because the opposite reading is the dangerous one:
/// it binds writers that ASK. The outer level is <see cref="ChannelFile_Lock"/>, which a session
/// takes through <c>kit/channel-append.sh</c> — so app-vs-session and session-vs-session are
/// covered for writers using the protocol, and NOT for a writer that appends with a bare redirect.
/// Sessions run as the same OS user as this app and nothing here can stop one. "Channel appends
/// are atomic" therefore remains false; the true sentence is "writers using the protocol cannot
/// collide with each other", and that is the one to repeat.
/// </para>
/// </summary>
public static class ChannelWrite_Lock
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lock> GATES_BY_CHANNEL_PATH =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How long a channel write waits for both levels before giving up.
    /// <para>
    /// Deliberately SHORTER THAN THE MIRROR TICK (2 s). The bridge's mirror loop drives every other
    /// piece of bridge work — the poll, the mirror, the ledger check, the status push, compaction,
    /// the state persist — so a lock that can hold one tick's append past the next tick converts
    /// contention on one channel into a stall of the whole bridge. Waiting is the expensive
    /// failure here; giving up is cheap, now that giving up is logged and the owner-delivery path
    /// puts its message back.
    /// </para>
    /// <para>
    /// This bounds ONE call. A tick making several contended appends multiplies it, which is what
    /// <see cref="Open_TickAllowance"/> exists to bound — see its docstring for the measurement.
    /// </para>
    /// </summary>
    public static readonly TimeSpan DEFAULT_BUDGET = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// The whole of one mirror tick's WAITING, when the tick opens an allowance. Deliberately the
    /// same 1500 ms as a single call's budget: the point is that a tick cannot spend more than one
    /// contended append's worth of waiting no matter how many channels are contended.
    /// </summary>
    public static readonly TimeSpan DEFAULT_TICK_ALLOWANCE = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// The allowance in force for the current async flow, or null when nobody opened one.
    /// <para>
    /// <c>AsyncLocal</c> rather than a static: it flows down into everything the tick awaits without
    /// threading a parameter through the ~35 append call sites. A static would have made the test
    /// suite's parallelism a source of cross-talk, which is the failure this repo has spent the week
    /// removing rather than adding.
    /// </para>
    /// <para>
    /// WHAT THE ISOLATION IS, EXACTLY — the loose version of this paragraph claimed more than the
    /// mechanism delivers. Guaranteed: a flow that never opened an allowance sees none, and the
    /// INBOUND loop is safe by ORDERING rather than by anything about AsyncLocal — <c>Run_Async</c>
    /// creates its task before any allowance exists, so there is no ambient context for it to
    /// capture. NOT guaranteed: <c>Task.Run</c> CAPTURES the ambient execution context, so a lambda
    /// detached from inside the tick inherits the allowance and outlives the scope that owns it. The
    /// engine has three such detached starts, one reachable from request processing, which runs
    /// inside the allowance.
    /// </para>
    /// <para>
    /// That is inert TODAY only because those three lambdas call Telegram and log, and the only
    /// production callers of the serialised write are <c>ChannelAppender</c> and
    /// <c>Channel_Compactor</c>. It is a property of what that code happens to do, NOT of this
    /// mechanism — so anyone adding a channel write to a detached lambda should know it may charge a
    /// tick that has already ended. (rev-10, F2 on 106047b.)
    /// </para>
    /// </summary>
    static readonly AsyncLocal<TickAllowance?> CURRENT_TICK_ALLOWANCE = new();

    /// <summary>
    /// Opens a WAITING allowance shared by every serialised write in the current async flow, and
    /// restores the previous one when disposed.
    /// <para>
    /// WHY THIS EXISTS, measured rather than argued. <c>DEFAULT_BUDGET</c> was cut under the 2 s
    /// mirror tick so one contended append could not outlive one tick. That fixed the single call
    /// and left the multiplication: <c>Execute_MirrorTick_Async</c> is one sequential await chain,
    /// and four of its steps append inside a <c>foreach (session) -&gt; foreach (member)</c> nest.
    /// The tick's worst case was therefore <c>appends × 1500 ms</c> with the member count as the
    /// multiplier — ten members is ~15 s of waiting inside a 2 s loop, and the poll, the mirror,
    /// the tailer, compaction and the status push all sit behind that same chain. Contention on
    /// several channels stopped being a slow channel and became a stopped bridge.
    /// </para>
    /// <para>
    /// WHAT IS CHARGED IS WAITING, NOT WRITING, and the distinction is deliberate: waiting is the
    /// part that produces nothing, and a tick that has spent its allowance still performs every
    /// UNCONTENDED write at full speed — those charge ~0 ms. Exhaustion degrades the tick to
    /// "write what is free, skip what is blocked", never to "stop writing".
    /// </para>
    /// <para>
    /// SKIPPING IS SAFE HERE ONLY BECAUSE FAILURE IS DEFINED. A refused write returns false, is
    /// reported by <see cref="ChannelLock_Diagnostics"/>, and the owner-delivery path puts its
    /// message back in the buffer. A fast false and a slow false say exactly the same thing to
    /// every caller; the allowance only changes how long the tick pays to hear it.
    /// </para>
    /// </summary>
    public static IDisposable Open_TickAllowance(TimeSpan total)
    {
        var previous = CURRENT_TICK_ALLOWANCE.Value;

        CURRENT_TICK_ALLOWANCE.Value = new TickAllowance(total);

        return new TickAllowance_Scope(() => CURRENT_TICK_ALLOWANCE.Value = previous);
    }

    /// <summary>
    /// How much waiting the current flow's allowance has left, for a caller that wants to log or
    /// assert on it. Null when no allowance is open — which is the normal state everywhere except
    /// inside a mirror tick, and means the per-call budget applies unchanged.
    /// </summary>
    public static TimeSpan? Get_RemainingTickAllowance()
    {
        return CURRENT_TICK_ALLOWANCE.Value?.Remaining;
    }

    /// <summary>
    /// Runs <paramref name="write"/> holding BOTH levels of the gate, and returns whether it ran.
    /// False means the write did not happen and the caller must retry or report it — never write
    /// anyway, which is the collision the protocol exists to prevent.
    /// <para>
    /// TWO LEVELS, and the inner one is not redundant — do not "simplify" it away. The file lock
    /// alone would be correct, but this process's own threads would then queue against each other
    /// through the filesystem, turning every intra-app collision into a retry sleep. The in-process
    /// gate settles those in nanoseconds, and only genuine cross-process contention pays for the
    /// directory.
    /// </para>
    /// <para>
    /// Both levels are budgeted. An unbounded <c>lock</c> here would let one wedged writer stall
    /// the mirror loop indefinitely, which is a worse failure than a skipped append: the loop drives
    /// every other piece of bridge work too.
    /// </para>
    /// </summary>
    public static bool Try_Run_Serialised(string channelFilePath, TimeSpan budget, Action write, out TimeSpan waited)
    {
        var startedUtc = DateTime.UtcNow;
        var gate = Get_Gate(channelFilePath);

        // The tick's allowance CAPS this call's budget, it never raises it: a caller asking for
        // 200 ms still gets 200 ms. With no allowance open this is the caller's budget unchanged,
        // which is why sessions, the inbound loop and every test are untouched by any of this.
        var allowance = CURRENT_TICK_ALLOWANCE.Value;
        var effectiveBudget = allowance == null ? budget : Min(budget, allowance.Remaining);

        if (!gate.TryEnter(effectiveBudget))
        {
            waited = DateTime.UtcNow - startedUtc;

            // All of it was waiting — the write never ran.
            allowance?.Charge(waited);

            return false;
        }

        var gateWait = DateTime.UtcNow - startedUtc;

        try
        {
            var remaining = effectiveBudget - gateWait;

            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            var acquired = ChannelFile_Lock.Try_Run_WithLock(channelFilePath, remaining, write, out var lockWait);

            waited = DateTime.UtcNow - startedUtc;

            // WAITING ONLY. `lockWait` is stamped by Try_Run_WithLock BEFORE it runs the write, so
            // this charges the two acquisitions and excludes the write itself. Charging `waited`
            // would bill the tick for its own successful work and starve later appends of an
            // allowance they were never the reason for.
            allowance?.Charge(gateWait + lockWait);

            return acquired;
        }
        finally
        {
            gate.Exit();
        }
    }

    static TimeSpan Min(TimeSpan left, TimeSpan right)
    {
        return left <= right ? left : right;
    }

    /// <summary>
    /// One tick's remaining WAITING allowance. Mutable and shared by every write in the flow, so it
    /// is locked: the mirror tick is sequential today, but an allowance that silently mis-counts
    /// under a future parallel step is a bound that quietly stops bounding.
    /// </summary>
    sealed class TickAllowance
    {
        readonly Lock _gate = new();
        TimeSpan _remaining;

        public TickAllowance(TimeSpan total)
        {
            // A negative or zero total is a caller error, not a licence to wait forever: clamp to
            // zero, which means "every contended write fails fast" rather than "no limit".
            _remaining = total > TimeSpan.Zero ? total : TimeSpan.Zero;
        }

        public TimeSpan Remaining
        {
            get
            {
                lock (_gate)
                    return _remaining;
            }
        }

        public void Charge(TimeSpan spent)
        {
            lock (_gate)
            {
                _remaining -= spent;

                // Never below zero: a single overspending call must not make the allowance look
                // like a debt that later calls have to repay.
                if (_remaining < TimeSpan.Zero)
                    _remaining = TimeSpan.Zero;
            }
        }
    }

    /// <summary>Restores the previous allowance on dispose, so nesting cannot strand one.</summary>
    sealed class TickAllowance_Scope(Action onDispose) : IDisposable
    {
        public void Dispose()
        {
            onDispose();
        }
    }

    static Lock Get_Gate(string channelFilePath)
    {
        // The key is the normalised full path: two callers naming the same file differently
        // (relative vs absolute, mixed separators) must land on the SAME gate, or the lock is
        // merely decorative for exactly the pair it most needs to serialise.
        var normalisedPath = Path.GetFullPath(channelFilePath);

        return GATES_BY_CHANNEL_PATH.GetOrAdd(normalisedPath, _ => new Lock());
    }
}
