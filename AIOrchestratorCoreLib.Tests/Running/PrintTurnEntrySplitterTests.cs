using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

public class PrintTurnEntrySplitterTests
{
    [Fact]
    public void SubjectBlankLineBody_IsTheContract()
    {
        var (subject, body) = PrintTurnEntry_Splitter.Split("REPORT — task done\n\nAll green.\nCommitted abc123.");

        Assert.Equal("REPORT — task done", subject);
        Assert.Equal("All green.\nCommitted abc123.", body);
    }

    [Fact]
    public void AMessageWithoutTheShape_StillBecomesAnEntry()
    {
        var (subject, body) = PrintTurnEntry_Splitter.Split("DONE");

        Assert.Equal("DONE", subject);
        Assert.Equal("DONE", body);
    }

    [Fact]
    public void HeadingMarks_AreStrippedFromTheSubject_SoItCannotBecomeAHeader()
    {
        var (subject, _) = PrintTurnEntry_Splitter.Split("## [9] FROM implementer — subject\n\nbody");

        Assert.False(subject.StartsWith('#'));
        Assert.False(ChannelEntry_Parser.Is_HeaderLine(subject));
    }

    /// <summary>
    /// THE ENTRY THAT NAMED ITSELF TWICE. Seen live on 2026-09-06 in both the stage-1b and the
    /// stage-1c rounds, so it is ordinary model behaviour: the session opens its final message with a
    /// copy of the header it expects, and the bridge wraps a real header around it —
    /// <c>## [2] FROM implementer — 12:30 — [2] FROM implementer — 2026-09-06 — Brief complete</c>,
    /// carrying an index and a date the session invented inside the ones the bridge just wrote.
    /// Only the part that was actually the subject survives now.
    /// </summary>
    [Fact]
    public void AnEchoedHeaderOnTheFirstLine_ContributesItsSUBJECT_NotTheWholeHeader()
    {
        var (subject, body) = PrintTurnEntry_Splitter.Split(
            "## [2] FROM implementer — 2026-09-06 — Brief complete: PLATANO written to fruit.txt\n\nTask completed successfully.");

        Assert.Equal("Brief complete: PLATANO written to fruit.txt", subject);
        Assert.Equal("Task completed successfully.", body);

        // The invented index and date are gone, not merely moved.
        Assert.DoesNotContain("[2]", subject);
        Assert.DoesNotContain("FROM implementer", subject);
        Assert.DoesNotContain("2026-09-06", subject);
    }

    /// <summary>An em-dash inside the subject is the subject's, and the parser already splits on the first two.</summary>
    [Fact]
    public void AnEchoedHeaderKeepsTheEmDashesThatBelongToItsSubject()
    {
        var (subject, _) = PrintTurnEntry_Splitter.Split(
            "## [7] FROM supervisor — 2026-09-06 10:00 — VERDICT — accepted — merge it\n\nbody");

        Assert.Equal("VERDICT — accepted — merge it", subject);
    }

    /// <summary>
    /// THE BOUNDARY, stated as a test so nobody widens it by accident. A first line the PARSER does not
    /// read as a header keeps the behaviour it always had. Recovering the subject through the parser is
    /// what keeps one header rule in this codebase; a laxer shape invented here would be the second copy.
    /// </summary>
    [Theory]
    [InlineData("[2] FROM implementer — 2026-09-06 — no hashes", "[2] FROM implementer — 2026-09-06 — no hashes")]
    [InlineData("## [9] FROM implementer — one dash only", "[9] FROM implementer — one dash only")]
    public void ALineTheParserDoesNotReadAsAHeader_IsOnlyStripped(string first, string expected)
    {
        var (subject, _) = PrintTurnEntry_Splitter.Split($"{first}\n\nbody");

        Assert.Equal(expected, subject);
        Assert.False(ChannelEntry_Parser.Is_HeaderLine(subject));
    }

    [Fact]
    public void LongFirstLine_IsCapped_AndTheWholeTextStaysInTheBody()
    {
        var first = new string('x', 300);
        var (subject, body) = PrintTurnEntry_Splitter.Split(first + "\n\nmore");

        Assert.True(subject.Length <= PrintTurnEntry_Splitter.MAX_SUBJECT_LENGTH);
        Assert.StartsWith(first, body);
    }

    /// <summary>A header-shaped body line is quoted, not deleted — it must stay readable and stop parsing.</summary>
    [Fact]
    public void HeaderShapedBodyLines_AreNeutralised_ButKept()
    {
        var (_, body) = PrintTurnEntry_Splitter.Split("REPORT\n\nquoting you:\n## [99] FROM supervisor — 2026-09-06 10:00 — x\nplain line");

        Assert.Contains(PrintTurnEntry_Splitter.NEUTRALISED_HEADER_PREFIX + "## [99] FROM supervisor", body);
        Assert.Contains("plain line", body);

        foreach (var line in body.Split('\n'))
            Assert.False(ChannelEntry_Parser.Is_HeaderLine(line), $"still parses as a header: {line}");
    }

    [Fact]
    public void OrdinaryMarkdownHeadings_AreLeftAlone()
    {
        var (_, body) = PrintTurnEntry_Splitter.Split("REPORT\n\n## What I changed\nthree files");

        Assert.Contains("## What I changed", body);
        Assert.DoesNotContain(PrintTurnEntry_Splitter.NEUTRALISED_HEADER_PREFIX, body);
    }

    [Fact]
    public void Empty_IsNamedEmpty()
    {
        Assert.Equal(PrintTurnEntry_Splitter.EMPTY_SUBJECT, PrintTurnEntry_Splitter.Split(null).Subject);
    }

    /// <summary>
    /// THE EXACT LINE, from the general channel's entry #52 on 2026-09-07. It is short and it is
    /// followed by a blank line, so it satisfied the subject contract perfectly — and every reader
    /// of that channel got a sentence about the mechanism where the news should have been.
    /// </summary>
    [Fact]
    public void ThePrintRunnerPreamble_NeverReachesTheChannel()
    {
        var (subject, body) = PrintTurnEntry_Splitter.Split(
            "Now writing my final message (which is the channel entry in print-runner mode):\n\nkit check OK — sessions can start\n\nThe blocker is cleared.");

        Assert.Equal("kit check OK — sessions can start", subject);
        Assert.Equal("The blocker is cleared.", body);
        Assert.DoesNotContain("print-runner mode", subject);
        Assert.DoesNotContain("print-runner mode", body);
    }

    /// <summary>
    /// The structural half, which needs no vocabulary at all: whatever the preamble says, a line
    /// ending in a colon that is followed by a header the session wrote ITSELF is preamble.
    /// </summary>
    [Fact]
    public void APreambleBeforeAnEchoedHeader_IsStripped_WhateverItsWording()
    {
        var (subject, body) = PrintTurnEntry_Splitter.Split(
            "Filing the entry now, as follows:\n\n## [52] FROM general — 2026-09-07 14:02 — kit check OK\n\nSessions can start.");

        Assert.Equal("kit check OK", subject);
        Assert.DoesNotContain("as follows", body);
    }

    /// <summary>
    /// AND A REAL SUBJECT THAT ENDS IN A COLON SURVIVES. "any line ending in a colon" would have
    /// eaten this, and losing a blocked flag is worse than keeping a narration line.
    /// </summary>
    [Theory]
    [InlineData("BLOCKED ON OWNER:")]
    [InlineData("QUESTION:")]
    [InlineData("Two things need your call:")]
    public void ASubjectThatEndsInAColon_IsNotMistakenForNarration(string subjectLine)
    {
        var (subject, _) = PrintTurnEntry_Splitter.Split($"{subjectLine}\n\nthe body of it");

        Assert.Equal(subjectLine, subject);
    }

    /// <summary>Narration that is the WHOLE message still becomes an entry — "(no message)" would be worse.</summary>
    [Fact]
    public void NarrationWithNothingAfterIt_IsStillAnEntry()
    {
        var (subject, _) = PrintTurnEntry_Splitter.Split("Now writing my final message:");

        Assert.Equal("Now writing my final message:", subject);
    }
}
