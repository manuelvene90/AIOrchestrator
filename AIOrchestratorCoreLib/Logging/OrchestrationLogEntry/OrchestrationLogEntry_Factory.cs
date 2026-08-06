namespace AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;

public static class OrchestrationLogEntry_Factory
{
    public static IOrchestrationLogEntry Create(
        DateTime timestampUtc,
        string orchId,
        LogLevels level,
        string message)
    {
        return new OrchestrationLogEntryModel(timestampUtc, orchId, level, message);
    }
}
