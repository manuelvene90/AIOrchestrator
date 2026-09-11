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
}
