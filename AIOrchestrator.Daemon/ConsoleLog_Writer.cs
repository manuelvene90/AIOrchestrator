using AIOrchestratorCoreLib.Composition;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;

namespace AIOrchestrator.Daemon;

/// <summary>
/// Subscribes to the orchestrator log and renders every entry on stdout — the daemon's window
/// onto the engine, in the place of the WPF log panel. Writes are serialised so two engine loops
/// logging at once cannot interleave half-lines.
/// </summary>
sealed class ConsoleLog_Writer(bool forSystemdJournal)
{
    readonly bool _forSystemdJournal = forSystemdJournal;
    readonly Lock _writeLock = new();

    public void On_EntryLogged(IOrchestrationLogEntry entry)
    {
        var line = ConsoleLogLine_Formatter.Format(entry, _forSystemdJournal);

        lock (_writeLock)
            Console.Out.WriteLine(line);
    }
}
