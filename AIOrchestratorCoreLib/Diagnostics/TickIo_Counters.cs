namespace AIOrchestratorCoreLib.Diagnostics;

/// <summary>
/// HOW MANY TIMES THE TICK TOUCHED THE DISK. Every counter here is incremented at a real syscall —
/// the read of a session.json, the read of a channel file, the write of the bridge cursor, the
/// append of a turn-log line — and nowhere else.
///
/// <para>
/// IT IS A SEAM FOR TESTS, NOT TELEMETRY, and the distinction is the reason it exists at all. The
/// waste this measures is repetition inside one 2-second tick: the same file read twenty-four times
/// because twenty-four callers each asked the disk. That is invisible to every assertion a test can
/// otherwise make — the ANSWERS are identical whether they came from one read or from twenty-four —
/// so a test that pins "read once per tick" has to count the reads. A log line would not do: it
/// cannot be asserted on, and per CLAUDE.md decision 15 nobody can act on it either.
/// </para>
/// <para>
/// COUNTING IS NOT FREE OF MEANING WHEN IT IS WRONG. A counter incremented at a call site that does
/// not actually open a file, or missing from one that does, turns a green test into a false clearance
/// for the exact defect it claims to pin. So each counter names ONE syscall and is incremented
/// immediately beside it.
/// </para>
/// <para>
/// THE COUNTS ARE PER-FLOW, NOT PROCESS-WIDE, and that is not a refinement — a process-wide counter
/// is unusable for the assertions this exists for. xUnit runs test collections in parallel, and half
/// the suite reads channel files; an absolute count taken from a static would carry every other
/// class's reads and the assertion would fail for reasons that have nothing to do with its subject
/// (the same defect <c>CHANNEL_LOCK_COLLECTION</c> documents for the lock's process-wide sink).
/// <see cref="Begin_Scope"/> opens a counter on the CALLING async flow; the scope object is inherited
/// by every task and thread started under it — which is how a scope opened by a test before
/// <c>Run_Async</c> counts the syscalls a mirror tick makes on its own loop — and is invisible to any
/// flow that did not open one.
/// </para>
/// <para>
/// AN UNSCOPED FLOW IS COUNTED NOWHERE. There is deliberately no process-wide total behind these:
/// nothing in the app reads a count, so keeping one would be a number no one can act on and one more
/// thing to keep true. In production every increment here is a null check and a branch not taken.
/// </para>
/// </summary>
public static class TickIo_Counters
{
    /// <summary>
    /// The counts one <see cref="Begin_Scope"/> collected. Mutable and shared BY REFERENCE with every
    /// flow started under the scope, which is what makes a background loop's syscalls visible to the
    /// test that opened it; the interlocked increments are what make that safe.
    /// </summary>
    public sealed class Counts
    {
        long _sessionFileReads;
        long _textFileReads;
        long _bridgeStateWrites;
        long _turnLogFileSyscalls;
        long _ticksEntered;

        /// <summary>Reads of a <c>session.json</c> — the file behind every <c>Load_All</c>.</summary>
        public long SessionFileReads => Interlocked.Read(ref _sessionFileReads);

        /// <summary>
        /// Reads of a shared-read text file — a channel (live or archive), a PLAN.md, a .usage.json.
        /// Counted at the two <c>Read_Text_Safe</c> implementations, which is every one of them, so the
        /// number is comparable across a change that removes reads rather than moving them.
        /// </summary>
        public long TextFileReads => Interlocked.Read(ref _textFileReads);

        /// <summary>Writes of <c>.bridge-state.json</c>.</summary>
        public long BridgeStateWrites => Interlocked.Read(ref _bridgeStateWrites);

        /// <summary>Filesystem calls made while appending turn-log lines (mkdir, stat, append).</summary>
        public long TurnLogFileSyscalls => Interlocked.Read(ref _turnLogFileSyscalls);

        /// <summary>
        /// Mirror ticks begun. THE DENOMINATOR OF EVERY OTHER COUNTER HERE, and it earned its place on
        /// 2026-09-09: the tick's inter-tick wait stopped being a fixed 2 s delay (ChannelChangeWaker
        /// ends it on a filesystem event too), so a reading taken over a wall-clock window no longer
        /// covers one tick. The cost test compared 78 reads to 176 and reported the cache broken; the
        /// 176 were three ticks. A per-window count is only a per-tick count while the tick period is
        /// a constant, and it no longer is.
        /// </summary>
        public long TicksEntered => Interlocked.Read(ref _ticksEntered);

        internal void Add_SessionFileRead() => Interlocked.Increment(ref _sessionFileReads);

        internal void Add_TextFileRead() => Interlocked.Increment(ref _textFileReads);

        internal void Add_BridgeStateWrite() => Interlocked.Increment(ref _bridgeStateWrites);

        internal void Add_TurnLogFileSyscall() => Interlocked.Increment(ref _turnLogFileSyscalls);

        internal void Add_TickEntered() => Interlocked.Increment(ref _ticksEntered);

        /// <summary>Zeroes every count, so one scope can measure a second tick after a first.</summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _sessionFileReads, 0);
            Interlocked.Exchange(ref _textFileReads, 0);
            Interlocked.Exchange(ref _bridgeStateWrites, 0);
            Interlocked.Exchange(ref _turnLogFileSyscalls, 0);
            Interlocked.Exchange(ref _ticksEntered, 0);
        }

        public override string ToString()
        {
            return $"ticks={TicksEntered}, session.json reads={SessionFileReads}, "
                + $"text-file reads={TextFileReads}, bridge-state writes={BridgeStateWrites}, "
                + $"turn-log syscalls={TurnLogFileSyscalls}";
        }
    }

    static readonly AsyncLocal<Counts?> _scope = new();

    /// <summary>The counts being collected on THIS flow, or null when nobody is counting here.</summary>
    public static Counts? Current => _scope.Value;

    /// <summary>
    /// Starts counting on this async flow and every flow started under it. Disposing stops it.
    /// Nesting replaces the inner scope for its lifetime and restores the outer one after.
    /// </summary>
    public static Scope Begin_Scope()
    {
        var previous = _scope.Value;
        var counts = new Counts();

        _scope.Value = counts;

        return new Scope(counts, previous);
    }

    public static void Count_SessionFileRead() => _scope.Value?.Add_SessionFileRead();

    public static void Count_TextFileRead() => _scope.Value?.Add_TextFileRead();

    public static void Count_BridgeStateWrite() => _scope.Value?.Add_BridgeStateWrite();

    public static void Count_TurnLogFileSyscall() => _scope.Value?.Add_TurnLogFileSyscall();

    /// <summary>Counted once at the top of a mirror tick, so every other count can be read per tick.</summary>
    public static void Count_TickEntered() => _scope.Value?.Add_TickEntered();

    /// <summary>The live counts of one <see cref="Begin_Scope"/>, readable while it is open.</summary>
    public sealed class Scope(Counts counts, Counts? previous) : IDisposable
    {
        public Counts Counts { get; } = counts;

        public void Dispose()
        {
            _scope.Value = previous;
        }
    }
}
