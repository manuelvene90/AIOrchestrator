namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Where the channel lock reports its failures, so they stop being invisible.
/// <para>
/// Why this is a static sink rather than a logger threaded through the callers, stated because the
/// shape is a deviation and deserves one: the lock is reached from ~24 call sites across
/// <c>BridgeEngineModel</c>, and 23 of them discard the bool that says the write did not happen. A
/// signal nobody reads is indistinguishable from no signal, so the report has to come from the lock
/// ITSELF — it is the only place that knows a failure occurred regardless of what the caller does
/// with the result. Threading a logger through those call sites would work and is the tidier shape,
/// but it is a mechanical edit across a file other branches are editing, and it would still leave
/// every future caller free to drop the signal again.
/// </para>
/// <para>
/// Failures only. An uncontended write says nothing: a diagnostic channel that reports success is a
/// firehose, and the real failures drown in it.
/// </para>
/// </summary>
public static class ChannelLock_Diagnostics
{
    static Action<string>? _sink;

    /// <summary>
    /// Wired once, at bridge startup, to the orchestration log. Until it is wired the lock is silent
    /// — which is the pre-existing behaviour, not a regression, and keeps the core lib usable from
    /// tests and tools that have no log.
    /// </summary>
    public static void Set_Sink(Action<string> sink)
    {
        _sink = sink;
    }

    public static void Clear_Sink()
    {
        _sink = null;
    }

    /// <summary>
    /// Reports one failure. Never throws: a diagnostic that can break the write it is describing is
    /// worse than no diagnostic, and this runs inside the lock's own error paths.
    /// </summary>
    public static void Report(string message)
    {
        var sink = _sink;

        if (sink == null)
            return;

        try
        {
            sink(message);
        }
        catch
        {
            // Swallowed deliberately. The caller is mid-failure already; replacing its problem with
            // a logging problem would hide the thing this exists to surface.
        }
    }
}
