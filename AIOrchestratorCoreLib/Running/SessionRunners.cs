namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// HOW a session is run. <see cref="Terminal"/> is the shape this app has always had: a Windows
/// Terminal window per session, kept alive between messages and woken by its own watcher.
/// <see cref="Print"/> is the transient shape measured in the 2026-09-05 deep study: no process
/// between messages, every inbound entry becomes one <c>claude -p --resume</c> invocation, and the
/// bridge writes the session's channel entry from the JSON result. <see cref="Stream"/> is the
/// persistent one measured in the supervisor-mode study: ONE <c>claude -p --input-format
/// stream-json</c> process kept alive by the bridge, messages written on its stdin, p50 1.27 s per
/// turn against 5.77 s for print — which is why it is the supervisor's shape and not a member's
/// (a member's turn lasts minutes, so print's ~4 s of start-up is noise and its zero-cost wait is
/// worth more than N resident processes of 260 MB).
///
/// <see cref="Bg"/> is the documented FALLBACK of that study — <c>claude --bg</c>, which is twice
/// as slow as the stream and costs a daemon and a pty-host per session — and it is NOT implemented
/// as a transport in this stage: a role configured for it is launched in a terminal, with a warning
/// that says so. It exists here because the fallback ladder and the one rule that governs it (never
/// without Remote Control disabled, see <see cref="BgSettings_Rule"/>) are decided at this layer,
/// and a ladder with a hole in it silently skips a rung.
///
/// The default is Terminal everywhere, so nothing changes until the owner changes it in config.json.
/// </summary>
public enum SessionRunners
{
    Terminal,
    Print,
    Stream,
    Bg,
}

public static class SessionRunner_Names
{
    public const string TERMINAL = "terminal";
    public const string PRINT = "print";
    public const string STREAM = "stream";
    public const string BG = "bg";

    public static string Get_Word(SessionRunners runner)
    {
        return runner switch
        {
            SessionRunners.Terminal => TERMINAL,
            SessionRunners.Print => PRINT,
            SessionRunners.Stream => STREAM,
            SessionRunners.Bg => BG,
            _ => throw new Exception($"Unhandled SessionRunners: {runner}"),
        };
    }

    /// <summary>Null for an unknown word — the caller decides the default, this does not guess.</summary>
    public static SessionRunners? Parse_OrNull(string? word)
    {
        return word?.Trim().ToLowerInvariant() switch
        {
            TERMINAL => SessionRunners.Terminal,
            PRINT => SessionRunners.Print,
            STREAM => SessionRunners.Stream,
            BG => SessionRunners.Bg,
            _ => null,
        };
    }
}
