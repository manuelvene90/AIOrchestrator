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

    /// <summary>
    /// Slot key → consecutive respawns with no observed liveness in between, for the bridge to
    /// PERSIST. The counter is what turns "a session died" into "a session cannot start", and it
    /// was reset to zero by every app restart — which is the one event most likely to be happening
    /// while a machine-wide cause (a binary off PATH, a machine that cannot fork) is crash-looping
    /// every session at once. The alert fires at exactly the threshold, so a forgotten counter is
    /// not a late alert: it is no alert at all.
    /// </summary>
    IReadOnlyDictionary<string, int> Get_ConsecutiveRespawns();

    /// <summary>
    /// Puts the persisted counters back at startup. Additive: a slot that comes alive still clears
    /// its own counter on the first check, so a restored count can only survive a slot that is
    /// still failing.
    /// </summary>
    void Restore_ConsecutiveRespawns(IReadOnlyDictionary<string, int> consecutiveRespawns);
}
