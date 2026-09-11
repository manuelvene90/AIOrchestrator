using System.Globalization;
using AIOrchestratorCoreLib.Logging;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;

namespace AIOrchestratorCoreLib.Composition;

/// <summary>
/// The daemon's stdout rendering of a log entry — the headless counterpart of the WPF log panel.
/// The JSONL files are unchanged (they are the record); this is the line a person reads in a
/// terminal or in journalctl. Under systemd the line carries the journal's priority prefix
/// (&lt;4&gt; warning, &lt;3&gt; error, &lt;6&gt; info) so <c>journalctl -p warning</c> filters
/// on it, and the journal's own timestamp makes ours redundant — so it is left out there.
/// </summary>
public static class ConsoleLogLine_Formatter
{
    public static string Format(IOrchestrationLogEntry entry, bool forSystemdJournal)
    {
        var level = Describe_Level(entry.Level);
        var scope = entry.OrchId.Length == 0 ? "" : $"[{entry.OrchId}] ";

        if (forSystemdJournal)
            return $"<{Build_JournalPriority(entry.Level)}>{level} {scope}{entry.Message}";

        var time = entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        return $"{time} {level} {scope}{entry.Message}";
    }

    public static string Describe_Level(LogLevels level)
    {
        return level switch
        {
            LogLevels.Warning => "WARN ",
            LogLevels.Error => "ERROR",
            _ => "INFO ",
        };
    }

    /// <summary>syslog priorities, which is what the journal reads off an sd_journal stream prefix.</summary>
    public static int Build_JournalPriority(LogLevels level)
    {
        return level switch
        {
            LogLevels.Warning => 4,
            LogLevels.Error => 3,
            _ => 6,
        };
    }
}
