namespace AIOrchestratorCoreLib.SupervisionPaths;

/// <summary>
/// Single authority for every file and folder under the central supervision home
/// (default: ~/.claude/supervision). No other component composes these paths by hand.
/// </summary>
public interface ISupervisionPaths
{
    string Root { get; }
    string ConfigFile { get; }
    string SecretsFile { get; }
    string BridgeStateFile { get; }
    string GlobalLogFile { get; }

    /// <summary>Home of the always-on GENERAL supervisor (owner ⇄ general, mirrored to the General topic).</summary>
    string GeneralFolder { get; }
    string GeneralChannelFile { get; }

    /// <summary>PID files, written by the spawned shells themselves — the watchdog's liveness source.</summary>
    string GeneralPidFile { get; }
    string Get_SupervisorPidFile(string orchId);
    string Get_CommunicatorPidFile(string orchId);
    string Get_ImplementerPidFile(string orchId, string memberId);

    /// <summary>Requests dropped by the general supervisor for the app to execute (start orchestration, ...).</summary>
    string RequestsFolder { get; }

    /// <summary>Per-window dedup state for the usage-limit Telegram alerts.</summary>
    string LimitAlertStateFile { get; }

    /// <summary>
    /// Which message in the General topic is the all-orchestrations dashboard. Persisted because the
    /// remembered TEXT is in memory: a restart with no stored id posts a SECOND dashboard beside the
    /// first, every time the app starts.
    /// </summary>
    string GeneralDashboardStateFile { get; }

    string Get_OrchestrationFolder(string orchId);
    string Get_SessionFile(string orchId);

    /// <summary>The supervisor-maintained task ledger (PLAN.md) the card's progress bar reads.</summary>
    string Get_PlanFile(string orchId);

    /// <summary>
    /// The app's PRECOMPUTED reading of that ledger, for the supervisor's terminal status line to
    /// render. It exists so the status line never parses PLAN.md itself: a second reader of that file
    /// is a second answer to "how far along is this", and the terminal and the owner's phone would
    /// disagree the first time either arithmetic changed.
    /// </summary>
    string Get_ProgressFile(string orchId);
    string Get_OwnerChannelFile(string orchId);
    string Get_OrchestrationLogFile(string orchId);
    string Get_ImplementerFolder(string orchId, string memberId);
    string Get_ImplementerChannelFile(string orchId, string memberId);
}
