namespace AIOrchestratorCoreLib.Status.SessionContextUsage;

/// <summary>
/// How full ONE session's context window is, as that session's own status-line probe last reported
/// it. Claude Code hands the probe a `context_window` object on every render and the probe dumps it
/// verbatim into the session's .usage.json, so this costs a file read and no new plumbing.
///
/// BOTH MEMBERS ARE NON-NULLABLE BY CONSTRUCTION: the factory returns null for the whole reading
/// rather than an object whose percentage is missing. A nullable percent inside a non-null reading
/// would give every caller two ways to spell "unknown" and two places to forget the check.
/// </summary>
public interface ISessionContextUsage
{
    /// <summary>
    /// Percentage of the context window in use, 0-100, exactly as Claude Code computed it — never
    /// recomputed here from the token fields. The app and the session's own terminal must never
    /// quote different numbers for the same window.
    /// </summary>
    double UsedPercent { get; }

    /// <summary>
    /// When the probe file was last written. The reading is only as fresh as that session's last
    /// status-line render, so a session which ended its turn minutes ago is quoting minutes-old
    /// state. Carried so a caller can say how stale a figure is rather than implying it is live.
    /// </summary>
    DateTime ProbeTimeUtc { get; }
}
