using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Kit.PluginGate;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Composition.OrchestratorServices;

/// <summary>
/// The service graph every host runs: built ONCE, in one place, so the WPF app and the headless
/// daemon cannot drift apart in which services exist or in what order they are wired. A host adds
/// only what is its own — windows, or a console and a service lifetime.
/// </summary>
public interface IOrchestratorServices
{
    ISupervisionPaths Paths { get; }
    IOrchestratorConfigProvider ConfigProvider { get; }
    IOrchestrationLog Log { get; }
    IOrchestrationSessionStore Store { get; }
    IOrchestrationLauncher Launcher { get; }
    IBridgeEngine Engine { get; }

    /// <summary>Whether sessions may start. The host's kit check records the verdict into it.</summary>
    IPluginGate PluginGate { get; }
}
