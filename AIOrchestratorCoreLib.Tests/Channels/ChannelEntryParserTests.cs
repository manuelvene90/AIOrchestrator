using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

public class ChannelEntryParserTests
{
    const string TWO_ENTRY_CHANNEL =
        "# SUPERVISION CHANNEL — seed preamble\n" +
        "\n" +
        "---\n" +
        "\n" +
        "## [1] FROM supervisor — 2026-08-06 — first orders\n" +
        "\n" +
        "Do the thing.\n" +
        "\n" +
        "## [2] FROM implementer — 2026-08-06 — boundary report\n" +
        "\n" +
        "Done. Suite green.\n";

    [Fact]
    public void Parse_All_TwoEntries_ParsesIndexAuthorSubjectAndBody()
    {
        var entries = ChannelEntry_Parser.Parse_All(TWO_ENTRY_CHANNEL);

        Assert.Equal(2, entries.Count);

        Assert.Equal(1, entries[0].Index);
        Assert.Equal(ChannelAuthors.Supervisor, entries[0].Author);
        Assert.Equal("2026-08-06", entries[0].DateText);
        Assert.Equal("first orders", entries[0].Subject);
        Assert.Equal("Do the thing.", entries[0].Body);

        Assert.Equal(2, entries[1].Index);
        Assert.Equal(ChannelAuthors.Implementer, entries[1].Author);
        Assert.Equal("boundary report", entries[1].Subject);
    }

    [Fact]
    public void Parse_All_PreambleBeforeFirstHeader_IsIgnored()
    {
        var entries = ChannelEntry_Parser.Parse_All(TWO_ENTRY_CHANNEL);

        Assert.DoesNotContain(entries, e => e.RawText.Contains("seed preamble"));
    }

    [Fact]
    public void Parse_All_SubjectContainingEmDash_KeepsFullSubject()
    {
        var text = "## [3] FROM supervisor — 2026-08-06 — verdicts on [11]: C3 RATIFIED — no escalation\nbody\n";

        var entries = ChannelEntry_Parser.Parse_All(text);

        Assert.Single(entries);
        Assert.Equal("verdicts on [11]: C3 RATIFIED — no escalation", entries[0].Subject);
        Assert.Equal("2026-08-06", entries[0].DateText);
    }

    [Fact]
    public void Parse_All_OwnerAndAppAuthors_AreRecognized()
    {
        var text =
            "## [1] FROM owner — 2026-08-06 10:00 — via Telegram\nhello\n" +
            "## [2] FROM app — 2026-08-06 10:01 — orchestration started\nok\n";

        var entries = ChannelEntry_Parser.Parse_All(text);

        Assert.Equal(ChannelAuthors.Owner, entries[0].Author);
        Assert.Equal(ChannelAuthors.App, entries[1].Author);
    }

    [Fact]
    public void Parse_All_UnknownAuthorWord_MapsToUnknown_AndEntryIsKept()
    {
        var text = "## [1] FROM auditor — 2026-08-06 — surprise\nbody\n";

        var entries = ChannelEntry_Parser.Parse_All(text);

        Assert.Single(entries);
        Assert.Equal(ChannelAuthors.Unknown, entries[0].Author);
    }

    [Fact]
    public void Get_NextIndex_EmptyText_ReturnsOne()
    {
        Assert.Equal(1, ChannelEntry_Parser.Get_NextIndex(string.Empty));
    }

    [Fact]
    public void Get_NextIndex_TwoEntries_ReturnsThree()
    {
        Assert.Equal(3, ChannelEntry_Parser.Get_NextIndex(TWO_ENTRY_CHANNEL));
    }

    [Fact]
    public void Is_HeaderLine_DistinguishesHeadersFromBodyText()
    {
        Assert.True(ChannelEntry_Parser.Is_HeaderLine("## [7] FROM implementer — 2026-08-06 — report"));
        Assert.False(ChannelEntry_Parser.Is_HeaderLine("## Section heading"));
        Assert.False(ChannelEntry_Parser.Is_HeaderLine("plain body text"));
    }
}
