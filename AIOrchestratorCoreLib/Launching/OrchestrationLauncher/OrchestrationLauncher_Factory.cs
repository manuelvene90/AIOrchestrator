using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Kit.PluginGate;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Running.SessionRunner;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Launching.OrchestrationLauncher;

public static class OrchestrationLauncher_Factory
{
    /// <summary>
    /// The production shape: the terminal runner wraps the spawner; the two bridge-driven ones only
    /// register a state file, which is why they are built from the same two dependencies.
    /// </summary>
    public static IOrchestrationLauncher Create(
        ISupervisionPaths paths,
        IOrchestratorConfigProvider configProvider,
        IOrchestrationSessionStore store,
        ISessionSpawner spawner,
        IOrchestrationLog log,
        IPluginGate? pluginGate = null)
    {
        return Create(paths, configProvider, store,
        [
            SessionRunner_Factory.Create_Terminal(spawner),
            SessionRunner_Factory.Create_Print(paths, store, log),
            SessionRunner_Factory.Create_Stream(paths, store, log),
        ], log, pluginGate);
    }

    public static IOrchestrationLauncher Create(
        ISupervisionPaths paths,
        IOrchestratorConfigProvider configProvider,
        IOrchestrationSessionStore store,
        IReadOnlyList<ISessionRunner> runners,
        IOrchestrationLog log,
        IPluginGate? pluginGate = null)
    {
        // No gate given = a caller with no kit to check (every existing test, and any host that does
        // not ship one). An absent gate must not be a silent refusal, so it is an explicit yes.
        return new OrchestrationLauncherModel(paths, configProvider, store, runners, pluginGate ?? PluginGate_Factory.Create_Allowing(), log);
    }
}
