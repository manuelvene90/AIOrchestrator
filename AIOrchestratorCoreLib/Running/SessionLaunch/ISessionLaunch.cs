namespace AIOrchestratorCoreLib.Running.SessionLaunch;

/// <summary>
/// Everything a runner needs to start one session, whichever way it runs it. The terminal runner
/// turns it into a Windows Terminal command; the print runner registers it and waits for traffic.
/// </summary>
public interface ISessionLaunch
{
    SessionRoles Role { get; }
    string OrchId { get; }

    /// <summary>'imp-n' / 'rev-n' / 'solo-n' for members; 'sup', 'com', 'general' for the singletons — the AIORCH_MEMBER value.</summary>
    string MemberId { get; }

    /// <summary>The repo (or the general supervisor's home) — the session's cwd.</summary>
    string WorkingDirectory { get; }
    string? Model { get; }

    /// <summary>Written by a terminal session's shell; a print session has no pid file at all.</summary>
    string PidFilePath { get; }
    string? DisplayName { get; }

    /// <summary>
    /// `--effort` for this session, or null for the CLI's own default. A role default (xhigh for the
    /// supervisor and the solo) and a per-orchestration override both arrive here already resolved;
    /// the runner never decides effort, it only carries it. Bridge-driven runners ignore it until the
    /// dispatcher's turn command learns the flag.
    /// </summary>
    string? Effort { get; }

    /// <summary>
    /// The Claude session id to `--resume`, or null for a fresh conversation. Set ONLY for a terminal
    /// supervisor or solo whose previous transcript still exists (ResumableSession_Resolver) — a
    /// `--resume` of an unknown id prints "No conversation found" and exits, which under the watchdog
    /// is a respawn loop (owner request 2026-09-10). Never `--continue`.
    /// </summary>
    string? ResumeSessionId { get; }
}
