using System.Globalization;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Logging.OrchestrationLog;

internal sealed class OrchestrationLogModel(ISupervisionPaths paths) : IOrchestrationLog
{
    readonly ISupervisionPaths _paths = paths;
    readonly Lock _writeLock = new();

    public event Action<IOrchestrationLogEntry>? EntryLogged;

    public void Log_Info(string orchId, string message)
    {
        Write_Entry(OrchestrationLogEntry_Factory.Create(DateTime.UtcNow, orchId, LogLevels.Info, message));
    }

    public void Log_Warning(string orchId, string message)
    {
        Write_Entry(OrchestrationLogEntry_Factory.Create(DateTime.UtcNow, orchId, LogLevels.Warning, message));
    }

    public void Log_Error(string orchId, string message, Exception? exception)
    {
        var fullMessage = exception == null
            ? message
            : $"{message} — {exception.GetType().Name}: {exception.Message}";

        Write_Entry(OrchestrationLogEntry_Factory.Create(DateTime.UtcNow, orchId, LogLevels.Error, fullMessage));
    }

    void Write_Entry(IOrchestrationLogEntry entry)
    {
        try
        {
            var line = Build_JsonLine(entry);

            lock (_writeLock)
            {
                Directory.CreateDirectory(_paths.Root);
                File.AppendAllText(_paths.GlobalLogFile, line);

                if (entry.OrchId.Length > 0 && Directory.Exists(_paths.Get_OrchestrationFolder(entry.OrchId)))
                    File.AppendAllText(_paths.Get_OrchestrationLogFile(entry.OrchId), line);
            }
        }
        catch
        {
            // Logging must never take the bridge down; the live event below still fires.
        }

        try
        {
            EntryLogged?.Invoke(entry);
        }
        catch
        {
            // A faulty UI subscriber must not take the bridge down either.
        }
    }

    static string Build_JsonLine(IOrchestrationLogEntry entry)
    {
        var node = new JsonObject
        {
            ["ts"] = entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            ["orch"] = entry.OrchId,
            ["level"] = entry.Level.ToString(),
            ["message"] = entry.Message,
        };

        return node.ToJsonString() + Environment.NewLine;
    }
}
