using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// The idle-implementer nudge decides purely from WHO wrote last. These pin that reading, which is
/// what separates "a brief nobody answered" (the 35-minute stall) from a healthy exchange.
/// </summary>
public class ChannelEntryAuthorOrderTests
{
    const string SUPERVISOR_BRIEF = "## [1] FROM supervisor — 2026-08-07 10:00 — brief\ndo the thing\n";
    const string IMPLEMENTER_REPORT = "\n## [2] FROM implementer — 2026-08-07 10:20 — done\nfinished\n";
    const string APP_NUDGE = "\n## [3] FROM app — 2026-08-07 10:30 — unread traffic\nnudge\n";

    [Fact]
    public void LastAuthor_SupervisorSpokeLast_MeansTheImplementerOwesAnAnswer()
    {
        var entries = ChannelEntry_Parser.Parse_All(SUPERVISOR_BRIEF);

        Assert.Equal(ChannelAuthors.Supervisor, entries[^1].Author);
    }

    [Fact]
    public void LastAuthor_ImplementerAnswered_MeansNothingIsOwed()
    {
        var entries = ChannelEntry_Parser.Parse_All(SUPERVISOR_BRIEF + IMPLEMENTER_REPORT);

        Assert.Equal(ChannelAuthors.Implementer, entries[^1].Author);
    }

    [Fact]
    public void LastAuthor_AppNudge_IsRecognised_SoTheAppNeverNudgesOnTopOfItsOwnNudge()
    {
        var entries = ChannelEntry_Parser.Parse_All(SUPERVISOR_BRIEF + APP_NUDGE);

        Assert.Equal(ChannelAuthors.App, entries[^1].Author);
    }
}
