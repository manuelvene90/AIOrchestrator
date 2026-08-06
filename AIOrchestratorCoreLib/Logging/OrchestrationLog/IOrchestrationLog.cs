using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;

namespace AIOrchestratorCoreLib.Logging.OrchestrationLog;

/// <summary>
/// Structured JSONL logging: per-orchestration file plus a global file, and a live event the UI
/// subscribes to. Logging failures are swallowed — logging never takes the bridge down.
/// </summary>
public interface IOrchestrationLog
{
    void Log_Info(string orchId, string message);
    void Log_Warning(string orchId, string message);
    void Log_Error(string orchId, string message, Exception? exception);

    event Action<IOrchestrationLogEntry>? EntryLogged;
}
