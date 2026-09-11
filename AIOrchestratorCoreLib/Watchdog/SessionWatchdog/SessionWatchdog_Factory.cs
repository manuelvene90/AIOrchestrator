using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Watchdog.SessionWatchdog;

public static class SessionWatchdog_Factory
{
    /// <summary>
    /// The config provider is read on every tick, not captured: a print-run session has no pid file
    /// BY DESIGN, and whether a role is print-run is a config answer that can change while the app
    /// runs. Deciding it from the filesystem alone left a role flipped back to terminal with a
    /// session nothing would ever respawn.
    /// </summary>
    public static ISessionWatchdog Create(
        ISupervisionPaths paths,
        IOrchestratorConfigProvider configProvider,
        IOrchestrationSessionStore store,
        IOrchestrationLauncher launcher,
        IOrchestrationLog log)
    {
        return new SessionWatchdogModel(paths, configProvider, store, launcher, log);
    }
}
