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

    /// <summary>How long a channel write waits for both levels before giving up.</summary>
    public static readonly TimeSpan DEFAULT_BUDGET = TimeSpan.FromSeconds(5);

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

        if (!gate.TryEnter(budget))
        {
            waited = DateTime.UtcNow - startedUtc;
            return false;
        }

        try
        {
            var remaining = budget - (DateTime.UtcNow - startedUtc);

            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            var acquired = ChannelFile_Lock.Try_Run_WithLock(channelFilePath, remaining, write, out _);

            waited = DateTime.UtcNow - startedUtc;
            return acquired;
        }
        finally
        {
            gate.Exit();
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
