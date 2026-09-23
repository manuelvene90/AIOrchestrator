using AIOrchestratorCoreLib.Logging.OrchestrationLog;

namespace AIOrchestratorCoreLib.Bridge.SuppressedEntries;

public static class SuppressedEntries_Factory
{
    /// <summary>An empty store; <paramref name="log"/> receives the line for every entry the cap drops.</summary>
    public static ISuppressedEntries Create(IOrchestrationLog log)
    {
        return new SuppressedEntriesModel(log);
    }
}
