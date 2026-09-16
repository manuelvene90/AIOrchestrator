namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// HOW a session learns it should take a turn. <see cref="Watcher"/>: the session's own bash monitor
/// fingerprints its channels and decides for itself — the shape the terminal runner has always had,
/// and a SECOND implementation of the wake policy (see the 2026-09-15 one-wake-model spec).
/// <see cref="Ticket"/>: the app decides, with the same policy the bridge uses, and says so by
/// writing a wake ticket file; the monitor only carries the message.
///
/// <para>
/// Orthogonal to <see cref="SessionRunners"/>: a bridge-driven session needs no ticket because the
/// app opens its turn directly, so this key is read only where the runner is
/// <see cref="SessionRunners.Terminal"/>. It is a separate key rather than a third runner word so the
/// migration can be per role and reversible in one edit.
/// </para>
/// </summary>
public enum WakeModes
{
    Watcher,
    Ticket,
}

public static class WakeMode_Names
{
    public const string WATCHER = "watcher";
    public const string TICKET = "ticket";

    public static string Get_Word(WakeModes mode)
    {
        return mode switch
        {
            WakeModes.Watcher => WATCHER,
            WakeModes.Ticket => TICKET,
            _ => throw new Exception($"Unhandled WakeModes: {mode}"),
        };
    }

    /// <summary>
    /// Null for anything this build does not know, INCLUDING a typo. The caller falls back to
    /// <see cref="WakeModes.Watcher"/>: a machine whose config names a mode this binary has never
    /// heard of must keep the behaviour it already had, never lose its wake-ups to a spelling.
    /// </summary>
    public static WakeModes? Parse_OrNull(string? word)
    {
        return word?.Trim().ToLowerInvariant() switch
        {
            WATCHER => WakeModes.Watcher,
            TICKET => WakeModes.Ticket,
            _ => null,
        };
    }
}
