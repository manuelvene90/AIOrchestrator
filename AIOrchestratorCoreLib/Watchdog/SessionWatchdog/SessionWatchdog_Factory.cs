using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Watchdog.SessionWatchdog;

public static class SessionWatchdog_Factory
{
    public static ISessionWatchdog Create(
        ISupervisionPaths paths,
        IOrchestrationSessionStore store,
        IOrchestrationLauncher launcher,
        IOrchestrationLog log)
    {
        return new SessionWatchdogModel(paths, store, launcher, log);
    }
}
