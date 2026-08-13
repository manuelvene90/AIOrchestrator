namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Serialises this process's writes to one channel file, so a read-then-append and a whole-file
/// rewrite cannot overlap on the same channel.
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
/// WHAT THIS DOES NOT COVER, stated plainly because the opposite reading is the dangerous one: it
/// is an in-process lock, and it protects writes made by THIS process only. Every agent session is
/// a separate OS process appending to the same files with its own tooling, and no in-process lock
/// can reach one. Session-vs-app and session-vs-session collisions are untouched by this class;
/// closing those needs a protocol both sides execute (a lockfile every writer takes), which this
/// is not. Do not describe channel appends as atomic on the strength of this type.
/// </para>
/// </summary>
public static class ChannelWrite_Lock
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lock> GATES_BY_CHANNEL_PATH =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs <paramref name="write"/> with exclusive access to <paramref name="channelFilePath"/>
    /// against other callers of this method in this process. Whatever the action throws propagates
    /// unchanged — deciding what a failed write means belongs to the caller, not to the gate.
    /// </summary>
    public static void Run_Serialised(string channelFilePath, Action write)
    {
        var gate = Get_Gate(channelFilePath);

        lock (gate)
            write();
    }

    /// <summary>
    /// Same gate, for a write that reports something back — compaction returns the rewritten
    /// file's length, which the caller uses to re-anchor the tailer's byte offset.
    /// </summary>
    public static T Run_Serialised<T>(string channelFilePath, Func<T> write)
    {
        var gate = Get_Gate(channelFilePath);

        lock (gate)
            return write();
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
