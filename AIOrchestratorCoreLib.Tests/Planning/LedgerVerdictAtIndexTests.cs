using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Planning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// The verdict is judged where it SITS, not at the file's tail — because the tail moves between the
/// write and the mirror pass that inspects it.
///
/// A Do-Not-Disturb catch-up burst, a /resume app entry, or the tailer's ordinary batching all land
/// entries after a supervisor's verdict before the app looks at the channel. Asking "is the last
/// entry a verdict" then answered no, and the ledger obligation was silently dropped. The
/// pre-existing code judged the appended entries themselves and was immune; reading the live file
/// introduced this.
/// </summary>
public class LedgerVerdictAtIndexTests
{
    [Fact]
    public void AVerdictStillCounts_WhenSomethingLandedAfterIt()
    {
        var entries = Parse(
            "## [1] FROM implementer — x — imp-1 online",
            "## [2] FROM supervisor — x — BRIEF",
            "## [3] FROM implementer — x — report filed",
            "## [4] FROM supervisor — x — accepted",
            "## [5] FROM app — x — GO AHEAD, resume");

        Assert.True(LedgerHealth_Tracker.Is_VerdictAt(entries, 4));

        // ...and the tail-based question gets it wrong, which is the regression itself.
        Assert.False(LedgerHealth_Tracker.Is_VerdictOnMemberWork(entries));
    }

    [Fact]
    public void ABriefIsStillNotAVerdict_WhenJudgedAtItsIndex()
    {
        var entries = Parse(
            "## [1] FROM implementer — x — imp-1 online",
            "## [2] FROM supervisor — x — BRIEF",
            "## [3] FROM app — x — nudge");

        Assert.False(LedgerHealth_Tracker.Is_VerdictAt(entries, 2));
    }

    /// <summary>An index that is not a supervisor entry — or not there at all — arms nothing.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(99)]
    public void AnIndexThatIsNotASupervisorEntry_IsNeverAVerdict(int channelEntryIndex)
    {
        var entries = Parse(
            "## [1] FROM implementer — x — imp-1 online",
            "## [2] FROM supervisor — x — BRIEF",
            "## [3] FROM implementer — x — report filed");

        Assert.False(LedgerHealth_Tracker.Is_VerdictAt(entries, channelEntryIndex));
    }

    static IReadOnlyList<AIOrchestratorCoreLib.Channels.ChannelEntry.IChannelEntry> Parse(params string[] headers)
    {
        return ChannelEntry_Parser.Parse_All(string.Join("\n\n", headers.Select(header => $"{header}\n\nbody\n")));
    }
}
