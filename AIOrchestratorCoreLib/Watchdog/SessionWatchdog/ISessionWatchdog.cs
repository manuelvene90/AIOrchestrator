namespace AIOrchestratorCoreLib.Watchdog.SessionWatchdog;

/// <summary>
/// Keeps every required agent session alive while the app runs: the general supervisor (always),
/// and the supervisor + non-closed implementers of every open orchestration. A dead session is
/// respawned through its role command; a supervisor or solo also continues its OWN conversation
/// (`claude --resume`) when its transcript survives, while implementers and reviewers re-read the
/// channels — their designed durable state — and the general supervisor is stateless by owner
/// directive. Called from the bridge engine's tick.
/// </summary>
public interface ISessionWatchdog
{
    void Check_AndRestart_DeadSessions();

    /// <summary>
    /// Drains crash-loop alerts (a slot respawned repeatedly without ever coming alive) for the
    /// engine to escalate to Telegram — silent respawning is right for one death, wrong for a loop.
    /// </summary>
    IReadOnlyList<(string OrchId, string AlertText)> Take_PendingCrashLoopAlerts();
}
