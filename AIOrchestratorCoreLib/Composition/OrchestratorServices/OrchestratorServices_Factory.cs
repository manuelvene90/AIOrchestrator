using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Kit.PluginGate;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Composition.OrchestratorServices;

/// <summary>
/// The composition root, extracted verbatim from the WPF app's OnStartup: config provider, log,
/// session store, spawner, launcher, bridge engine — in that order, because each takes the ones
/// before it. Nothing here starts running; the host decides when Run_Async begins.
/// </summary>
public static class OrchestratorServices_Factory
{
    public static IOrchestratorServices Create(ISupervisionPaths paths)
    {
        var configProvider = OrchestratorConfigProvider_Factory.Create(paths);
        var log = OrchestrationLog_Factory.Create(paths);
        var store = OrchestrationSessionStore_Factory.Create(paths);
        var spawner = SessionSpawner_Factory.Create();
        // Built here and RECORDED later, by the host's kit check: the verdict depends on a Claude
        // home this composition root is not given, so it genuinely is not knowable yet. Until it is
        // recorded the gate says yes, and the startup log says the check has not run.
        var pluginGate = PluginGate_Factory.Create();
        var launcher = OrchestrationLauncher_Factory.Create(paths, configProvider, store, spawner, log, pluginGate);
        var engine = BridgeEngine_Factory.Create(paths, configProvider, store, launcher, log);

        return new OrchestratorServicesModel(paths, configProvider, log, store, launcher, engine, pluginGate);
    }
}
