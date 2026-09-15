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

    /// <summary>
    /// Once a collision has happened the file is no longer sorted, and numbering from the LAST
    /// entry walks straight back into indices that already exist: a writer that read [71] late
    /// leaves 71, 72, 73, 72 — and the next index taken from the tail is 73, which is taken.
    /// The highest index used is the only safe basis.
    /// </summary>
    [Fact]
    public void Get_NextIndex_AfterAnOutOfOrderEntry_ContinuesFromTheHighestIndexUsed()
    {
        var channelText =
            "## [71] FROM supervisor — d — brief\n\nbody\n\n"
            + "## [72] FROM implementer — d — report\n\nbody\n\n"
            + "## [73] FROM implementer — d — second report\n\nbody\n\n"
            + "## [72] FROM app — d — late arrival\n\nbody\n";

        Assert.Equal(74, ChannelEntry_Parser.Get_NextIndex(channelText));
    }

    [Fact]
    public void Is_HeaderLine_DistinguishesHeadersFromBodyText()
    {
        Assert.True(ChannelEntry_Parser.Is_HeaderLine("## [7] FROM implementer — 2026-08-06 — report"));
        Assert.False(ChannelEntry_Parser.Is_HeaderLine("## Section heading"));
        Assert.False(ChannelEntry_Parser.Is_HeaderLine("plain body text"));
    }

    /// <summary>
    /// A QUOTED HEADER IS EVIDENCE, NOT A NEW ENTRY.
    ///
    /// <para>
    /// Observed 2026-09-15, reproduced from the owner's Telegram export: a supervisor quoted an
    /// implementer's entry inside a fenced block, and the parser — which had no fence state at all —
    /// cut the supervisor's message at the quoted header. The owner received the first half signed
    /// "Sup", and the SECOND half signed "Imp", in a separate message minutes later, opening
    /// mid-sentence on "Quindi 273, 281, 288 e 043 sono lì da vedere." Worse silently: when the
    /// quoted author is `owner` the tail is never mirrored at all, and when it is `app` only the
    /// quoted subject survives.
    /// </para>
    /// </summary>
    const string SUPERVISOR_QUOTING_AN_ENTRY =
        "## [42] FROM supervisor — 2026-09-15 10:00 — tracker\n" +
        "Ho guardato i quattro item. L'implementer ha scritto:\n" +
        "```\n" +
        "## [17] FROM implementer — 2026-09-15 09:58 — report\n" +
        "fatto tutto\n" +
        "```\n" +
        "Quindi 273, 281, 288 e 043 sono lì da vedere.\n";

    [Fact]
    public void Parse_All_AHeaderInsideAFencedBlock_DoesNotOpenAnEntry()
    {
        var entry = Assert.Single(ChannelEntry_Parser.Parse_All(SUPERVISOR_QUOTING_AN_ENTRY));

        Assert.Equal(42, entry.Index);
        Assert.Equal(ChannelAuthors.Supervisor, entry.Author);

        // The tail is the half the owner lost; it must still be part of the supervisor's message.
        Assert.Contains("Quindi 273, 281, 288 e 043", entry.Body, StringComparison.Ordinal);
        Assert.Contains("fatto tutto", entry.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Count_Entries_AHeaderInsideAFencedBlock_IsNotCounted()
    {
        Assert.Equal(1, ChannelEntry_Parser.Count_Entries(SUPERVISOR_QUOTING_AN_ENTRY));
    }

    [Fact]
    public void Get_NextIndex_AHeaderInsideAFencedBlock_DoesNotClaimItsIndex()
    {
        // The quoted [17] is older than the real [42]; honouring it would hand out 18, which is taken.
        Assert.Equal(43, ChannelEntry_Parser.Get_NextIndex(SUPERVISOR_QUOTING_AN_ENTRY));
    }

    [Fact]
    public void Read_HeaderLines_AHeaderInsideAFencedBlock_IsNotReported()
    {
        var headers = ChannelEntry_Parser.Read_HeaderLines(SUPERVISOR_QUOTING_AN_ENTRY);

        Assert.Equal(42, Assert.Single(headers).Index);
    }

    [Theory]
    [InlineData("```", "```")]
    [InlineData("```json", "```")]
    [InlineData("~~~", "~~~")]
    [InlineData("   ```", "   ```")]
    public void Parse_All_EveryFenceSpelling_SuppressesAQuotedHeader(string opening, string closing)
    {
        var channelText =
            "## [5] FROM supervisor — 2026-09-15 10:00 — tracker\n"
            + "quoting:\n"
            + opening + "\n"
            + "## [1] FROM implementer — 2026-09-15 09:00 — old\n"
            + closing + "\n"
            + "tail\n";

        Assert.Equal(5, Assert.Single(ChannelEntry_Parser.Parse_All(channelText)).Index);
    }

    /// <summary>
    /// AN UNCLOSED FENCE MUST NOT SWALLOW THE CHANNEL.
    ///
    /// <para>
    /// The obvious fence fix — "suppress headers while a fence is open" — turns one agent's stray
    /// ``` into a channel that stops mirroring entirely: every later entry vanishes into one giant
    /// body and the bridge has no way to notice. That is strictly worse than the defect being fixed,
    /// so an OPENING fence with no closing partner suppresses nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void Parse_All_AnUnclosedFence_StillOpensTheEntriesAfterIt()
    {
        var channelText =
            "## [1] FROM supervisor — 2026-09-15 10:00 — brief\n"
            + "here is a block I forgot to close:\n"
            + "```\n"
            + "some text\n"
            + "## [2] FROM implementer — 2026-09-15 10:05 — report\n"
            + "done\n"
            + "## [3] FROM supervisor — 2026-09-15 10:09 — verdict\n"
            + "accepted\n";

        var entries = ChannelEntry_Parser.Parse_All(channelText);

        Assert.Equal([1, 2, 3], entries.Select(entry => entry.Index));
    }

    /// <summary>
    /// Observed 2026-09-15: a header carrying ONE em dash put its whole tail in the date field and
    /// left the subject empty. The mirror falls back to the subject when a body is all marker lines,
    /// so the owner received a message that was nothing but the speaker prefix — seven times.
    /// </summary>
    [Fact]
    public void Parse_All_AHeaderWithOneEmDash_KeepsTheTextAsTheSubject_NotAsADate()
    {
        var entry = Assert.Single(ChannelEntry_Parser.Parse_All(
            "## [12] FROM supervisor — QUESTION: quale opzione?\nQUESTION: quale opzione?\n"));

        Assert.Equal("QUESTION: quale opzione?", entry.Subject);
        Assert.Equal(string.Empty, entry.DateText);
    }

    [Fact]
    public void Parse_All_AHeaderWithOneEmDashCarryingAStamp_KeepsItAsTheDate()
    {
        var entry = Assert.Single(ChannelEntry_Parser.Parse_All(
            "## [12] FROM supervisor — 2026-09-15 10:00\nbody\n"));

        Assert.Equal("2026-09-15 10:00", entry.DateText);
        Assert.Equal(string.Empty, entry.Subject);
    }

    /// <summary>
    /// AN UNUSABLE INDEX IS ONE LOST ENTRY, NEVER A DEAD CHANNEL.
    ///
    /// <para>
    /// `int.Parse` on an unbounded `(\d+)` threw `OverflowException`, and `[0]` threw
    /// `ArgumentException` out of the factory. Either throw landed in the tailer after the offset had
    /// advanced and before `Pending` was cleared, so the same throw recurred every 2 seconds for
    /// ever: that channel never mirrored again and could not even be appended to — not even by the
    /// app's own error report about it. A channel header is agent-written, untrusted input
    /// (CLAUDE.md decision 12), so the safe direction is to skip the entry, not to throw.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("99999999999999999999")]
    [InlineData("0")]
    public void Parse_All_AnUnusableIndex_SkipsThatEntryAndKeepsTheRest(string badIndex)
    {
        var channelText =
            "## [1] FROM supervisor — 2026-09-15 10:00 — brief\nfirst\n"
            + $"## [{badIndex}] FROM supervisor — 2026-09-15 10:01 — poison\nsecond\n"
            + "## [3] FROM implementer — 2026-09-15 10:02 — report\nthird\n";

        var entries = ChannelEntry_Parser.Parse_All(channelText);

        Assert.Equal([1, 3], entries.Select(entry => entry.Index));
    }

    [Theory]
    [InlineData("99999999999999999999")]
    [InlineData("0")]
    public void Get_NextIndex_AnUnusableIndex_DoesNotThrow(string badIndex)
    {
        var channelText = $"## [{badIndex}] FROM supervisor — 2026-09-15 10:01 — poison\nbody\n";

        Assert.Equal(1, ChannelEntry_Parser.Get_NextIndex(channelText));
    }

    [Theory]
    [InlineData("99999999999999999999")]
    [InlineData("0")]
    public void Read_HeaderLines_AnUnusableIndex_DoesNotThrow(string badIndex)
    {
        var channelText = $"## [{badIndex}] FROM supervisor — 2026-09-15 10:01 — poison\nbody\n";

        Assert.Empty(ChannelEntry_Parser.Read_HeaderLines(channelText));
    }

    /// <summary>
    /// THE FAST PATH AND THE EXACT PATH MUST AGREE, or the cheap one is a second rule.
    ///
    /// <para>
    /// <see cref="ChannelEntry_Parser.Count_Entries"/> runs on every channel on every two-second tick,
    /// so it walks the text as a span and allocates nothing — but fence awareness cannot be settled in
    /// one forward pass, because whether a delimiter CLOSES is only known later. It resolves that by
    /// noticing that a file with no delimiter at all cannot hold a quoted header, and falling back to
    /// the exact scan only for a file that has one. This pins the two against each other over the
    /// shapes where they could come apart — which is the whole licence for having a fast path.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("## [1] FROM supervisor — 2026-09-15 10:00 — one\nbody\n")]
    [InlineData("no headers here at all\njust prose\n")]
    [InlineData(SUPERVISOR_QUOTING_AN_ENTRY)]
    [InlineData("## [1] FROM supervisor — d — a\n```\n## [2] FROM implementer — d — b\n")]
    [InlineData("## [1] FROM supervisor — d — a\n~~~\n## [2] FROM implementer — d — b\n~~~\ntail\n")]
    [InlineData("```\n## [1] FROM supervisor — d — quoted at the very top\n```\n")]
    [InlineData("## [1] FROM supervisor — d — a\r\n```\r\n## [2] FROM implementer — d — b\r\n```\r\n")]
    public void Count_Entries_AgreesWithTheHeaderScan(string channelText)
    {
        var exact = ChannelEntry_Parser.Read_HeaderLineIndexes(channelText.Split('\n')).Count;

        Assert.Equal(exact, ChannelEntry_Parser.Count_Entries(channelText));
    }

}
