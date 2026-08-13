using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// Every malformed shape here was taken from the LIVE channels of 2026-08-07, where one supervisor
/// wrote headers three different ways and its entries silently ceased to exist as far as the app
/// was concerned — never mirrored to the owner, never counted, indexes still free.
/// </summary>
public class ChannelShapeValidatorTests
{
    [Fact]
    public void Canonical_Headers_AreNeverFlagged()
    {
        var text = """
        ## [1] FROM supervisor — 2026-08-07 14:00 — a normal entry
        body text

        ## [2] FROM implementer — 2026-08-07 14:12 — WRITING WINDOW CLOSED. Task 2a committed.
        more body — with an em-dash inside the body, which is fine
        """;

        Assert.Empty(ChannelShape_Validator.Find_MalformedHeaders(text));
    }

    [Theory]
    [InlineData("## [SUPERVISOR — 2026-08-08 04:14] SWITCH YOUR WATCHER: Bash tasks get reaped.")]
    [InlineData("## [supervisor] FROM supervisor — 2026-08-08 04:48 — CHECK YOUR WATCHER ANCHOR")]
    [InlineData("## [2b] FROM supervisor — 2026-08-07 12:56 — Excellent pass.")]
    public void RealMalformedHeaders_AreCaught(string line)
    {
        var found = Assert.Single(ChannelShape_Validator.Find_MalformedHeaders($"## [1] FROM owner — d — s\nbody\n{line}\nbody"));

        Assert.Equal(3, found.LineNumber);
        Assert.Equal(line, found.Line);
    }

    /// <summary>
    /// The false positive that would make this useless: implementers write markdown headings in
    /// their report bodies all the time, and those are not entry headers.
    /// </summary>
    [Theory]
    [InlineData("## BRANCH SUMMARY — `worktree-screener-greedy-cluster-stall`, off `bab9a369`, 12 commits")]
    [InlineData("## What I changed")]
    [InlineData("### [1] FROM supervisor — not even an h2")]
    [InlineData("Some prose mentioning ## [3] FROM supervisor mid-line")]
    public void OrdinaryBodyHeadings_AreNotFlagged(string line)
    {
        Assert.Empty(ChannelShape_Validator.Find_MalformedHeaders($"## [1] FROM owner — d — s\n{line}"));
    }

    /// <summary>
    /// REAL false positives from the live channels. The first version scanned the whole line for
    /// "from", so an implementer's own report heading was reported to the owner as a malformed
    /// entry header. A warning that cries wolf is worse than no warning: this one exists to catch a
    /// failure that is otherwise completely invisible, so it has to stay trustworthy.
    /// </summary>
    [Theory]
    [InlineData("## B6. Can deployment rules be built from existing condition objects?")]
    [InlineData("## F11 — NEW, from your item (6). The search-budget editor does not exist.")]
    [InlineData("## F9 — LOW — CONFIRMED. The documented recovery from an unreadable document is a no-op.")]
    [InlineData("## Notes from imp-1")]
    public void ReportHeadingsContainingTheWordFrom_AreNotFlagged(string line)
    {
        Assert.Empty(ChannelShape_Validator.Find_MalformedHeaders($"## [1] FROM supervisor — d — s\nbody\n{line}"));
    }

    /// <summary>An attempted header that merely forgot its index is still an attempted header.</summary>
    [Fact]
    public void AHeaderMissingItsIndex_IsStillCaught()
    {
        var found = ChannelShape_Validator.Find_MalformedHeaders(
            "## [1] FROM owner — d — s\n## FROM supervisor — 2026-08-10 12:00 — no index at all");

        Assert.Single(found);
    }

    /// <summary>The genuine offenders from the live channels must still be caught.</summary>
    [Theory]
    [InlineData("## [FINAL] FROM supervisor — 2026-08-09 07:16 — MERGED to master.")]
    [InlineData("## [88b] FROM implementer — 2026-08-07 — Harness landed.")]
    [InlineData("## [supervisor] FROM supervisor — 2026-08-08 04:48 — CHECK YOUR WATCHER ANCHOR")]
    public void NonNumericIndexes_AreStillCaught(string line)
    {
        Assert.Single(ChannelShape_Validator.Find_MalformedHeaders($"## [1] FROM owner — d — s\n{line}"));
    }

    [Fact]
    public void SeveralMalformed_AreAllReported_WithTheirLineNumbers()
    {
        var text = """
        ## [1] FROM owner — 2026-08-07 10:00 — fine
        ## [SUPERVISOR — 2026-08-07 10:01] first bad one
        ## [2] FROM supervisor — 2026-08-07 10:02 — fine again
        ## [2b] FROM supervisor — 2026-08-07 10:03 — second bad one
        """;

        var malformed = ChannelShape_Validator.Find_MalformedHeaders(text);

        Assert.Equal(2, malformed.Count);
        Assert.Equal(2, malformed[0].LineNumber);
        Assert.Equal(4, malformed[1].LineNumber);
    }

    [Fact]
    public void ReportBody_NamesTheOffendingLines_AndTheOnlyAcceptedFormat()
    {
        var body = ChannelShape_Validator.Build_ReportBody([(7, "## [2b] FROM supervisor — d — s")], nextFreeIndex: 3);

        Assert.Contains("line 7", body);
        Assert.Contains("## [2b] FROM supervisor", body);
        Assert.Contains(ChannelShape_Validator.CANONICAL_FORMAT, body);

        // Append-only: the fix is a NEW entry, never an edit of the broken line.
        Assert.Contains("append-only", body);
    }

    /// <summary>
    /// THE REPORT MAY STATE ONLY WHAT IT OBSERVED. It used to open by asserting three consequences it
    /// never computed — "they were never mirrored to the owner's phone, never counted as traffic, and
    /// their index numbers are still free" — as a string literal, in a channel, with the app's
    /// authority.
    ///
    /// For a line caught MID-WRITE all three are probably false: the tailer buffers an incomplete line
    /// and delivers the entry once the rest arrives, so the entry was mirrored, was counted, and its
    /// index was taken. On 2026-08-13 a supervisor believed that sentence about its own entry,
    /// re-appended a duplicate, and spent three exchanges hunting a cause.
    ///
    /// Same defect as the guard alert's deleted tail, four hours apart in the same evening: that one
    /// invented a CAUSE, this one invented CONSEQUENCES. The rule is decision 21's, applied to a
    /// report — never state an answer you did not compute.
    ///
    /// The positives are what stop this being a negative-only assertion, which would admit an empty
    /// body: the report must still NAME the line and say what to do about it.
    /// </summary>
    [Fact]
    public void ReportBody_ClaimsNoDeliveryConsequencesItDidNotMeasure()
    {
        var body = ChannelShape_Validator.Build_ReportBody([(7, "## [2b] FROM supervisor — d — s")], nextFreeIndex: 3);

        Assert.DoesNotContain("never mirrored", body);
        Assert.DoesNotContain("never counted", body);
        Assert.DoesNotContain("CANNOT SEE", body);

        Assert.Contains("do not parse", body);
        Assert.Contains("line 7", body);
    }

    /// <summary>
    /// The one consequence the validator CAN compute, so it is the one it is allowed to state. It is
    /// also the actionable half — a writer re-appending needs the number, and guessing it is how two
    /// entries end up sharing an index.
    /// </summary>
    [Fact]
    public void ReportBody_StatesTheMeasuredNextFreeIndex()
    {
        var body = ChannelShape_Validator.Build_ReportBody([(7, "## [2b] FROM supervisor — d — s")], nextFreeIndex: 12);

        Assert.Contains("12", body);
    }

    [Fact]
    public void EmptyOrSeedOnlyChannel_IsClean()
    {
        Assert.Empty(ChannelShape_Validator.Find_MalformedHeaders(""));
        Assert.Empty(ChannelShape_Validator.Find_MalformedHeaders("# Channel\n\nSeed preamble, no entries yet.\n"));
    }
}
