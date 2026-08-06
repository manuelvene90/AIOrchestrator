using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Launching.OrchestrationLauncher;

public static class OrchestrationLauncher_Factory
{
    public static IOrchestrationLauncher Create(
        ISupervisionPaths paths,
        IOrchestratorConfig config,
        IOrchestrationSessionStore store,
        ISessionSpawner spawner,
        IOrchestrationLog log)
    {
        return new OrchestrationLauncherModel(paths, config, store, spawner, log);
    }
}
