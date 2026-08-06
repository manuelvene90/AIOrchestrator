namespace AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;

internal sealed class OrchestrationLogEntryModel(
    DateTime timestampUtc,
    string orchId,
    LogLevels level,
    string message) : IOrchestrationLogEntry
{
    public DateTime TimestampUtc { get; } = timestampUtc;
    public string OrchId { get; } = orchId;
    public LogLevels Level { get; } = level;
    public string Message { get; } = message;
}
