using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge.EngineState;

public static class EngineStateStore_Factory
{
    /// <summary>The production store: one atomic JSON file beside the bridge cursor.</summary>
    public static IEngineStateStore Create_File(ISupervisionPaths paths, IOrchestrationLog? log)
    {
        return new FileEngineStateStoreModel(paths, log);
    }

    /// <summary>
    /// A store with no filesystem behind it. Handed to two successive engines it reproduces a
    /// restart exactly, which is the only way the "nothing is lost" claim can be asserted rather
    /// than argued.
    /// </summary>
    public static IEngineStateStore Create_InMemory()
    {
        return new MemoryEngineStateStoreModel();
    }
}
