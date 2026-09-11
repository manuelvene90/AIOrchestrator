using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Kit.PluginGate;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Composition.OrchestratorServices;

internal sealed class OrchestratorServicesModel(
    ISupervisionPaths paths,
    IOrchestratorConfigProvider configProvider,
    IOrchestrationLog log,
    IOrchestrationSessionStore store,
    IOrchestrationLauncher launcher,
    IBridgeEngine engine,
    IPluginGate pluginGate) : IOrchestratorServices
{
    public ISupervisionPaths Paths { get; } = paths;
    public IOrchestratorConfigProvider ConfigProvider { get; } = configProvider;
    public IOrchestrationLog Log { get; } = log;
    public IOrchestrationSessionStore Store { get; } = store;
    public IOrchestrationLauncher Launcher { get; } = launcher;
    public IBridgeEngine Engine { get; } = engine;
    public IPluginGate PluginGate { get; } = pluginGate;
}
