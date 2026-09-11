using AIOrchestratorCoreLib.Composition;
using AIOrchestratorCoreLib.Logging;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Composition;

public class ConsoleLogLineFormatterTests
{
    static readonly DateTime STAMP = new(2026, 9, 5, 21, 47, 46, DateTimeKind.Utc);

    [Fact]
    public void ATerminalLine_CarriesTime_Level_ScopeAndMessage()
    {
        var entry = OrchestrationLogEntry_Factory.Create(STAMP, "option-lab-2", LogLevels.Warning, "General supervisor not running");

        var line = ConsoleLogLine_Formatter.Format(entry, forSystemdJournal: false);

        Assert.EndsWith("WARN  [option-lab-2] General supervisor not running", line);
        Assert.Matches(@"^\d\d:\d\d:\d\d ", line);
    }

    [Fact]
    public void AnAppGlobalEntry_HasNoScopeBrackets()
    {
        var entry = OrchestrationLogEntry_Factory.Create(STAMP, "", LogLevels.Info, "Bridge started");

        Assert.EndsWith("INFO  Bridge started", ConsoleLogLine_Formatter.Format(entry, forSystemdJournal: false));
    }

    [Fact]
    public void UnderSystemd_TheJournalPriorityLeads_AndTheTimestampIsLeftToTheJournal()
    {
        var error = OrchestrationLogEntry_Factory.Create(STAMP, "x", LogLevels.Error, "boom");
        var info = OrchestrationLogEntry_Factory.Create(STAMP, "", LogLevels.Info, "fine");

        Assert.Equal("<3>ERROR [x] boom", ConsoleLogLine_Formatter.Format(error, forSystemdJournal: true));
        Assert.Equal("<6>INFO  fine", ConsoleLogLine_Formatter.Format(info, forSystemdJournal: true));
        Assert.Equal(4, ConsoleLogLine_Formatter.Build_JournalPriority(LogLevels.Warning));
    }
}
