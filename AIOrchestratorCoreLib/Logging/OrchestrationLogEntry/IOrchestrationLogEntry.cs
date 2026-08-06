namespace AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;

/// <summary>One structured log line. OrchId is empty for app-global events.</summary>
public interface IOrchestrationLogEntry
{
    DateTime TimestampUtc { get; }
    string OrchId { get; }
    LogLevels Level { get; }
    string Message { get; }
}
