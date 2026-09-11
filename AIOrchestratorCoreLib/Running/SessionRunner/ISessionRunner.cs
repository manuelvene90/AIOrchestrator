using AIOrchestratorCoreLib.Running.SessionLaunch;

namespace AIOrchestratorCoreLib.Running.SessionRunner;

/// <summary>
/// THE SEAM between "a session exists" and "how it runs". The launcher decides WHAT to start and
/// hands it here; the runner decides whether that means a terminal window kept alive between
/// messages or a registration that turns each inbound entry into one <c>claude -p</c> invocation.
/// </summary>
public interface ISessionRunner
{
    SessionRunners Kind { get; }

    /// <summary>Starts (or restarts) the session. Idempotent for the print runner — a restart resumes the same transcript.</summary>
    void Start(ISessionLaunch launch);
}
