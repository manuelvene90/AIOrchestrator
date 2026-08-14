using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// The screen for header lines that PARSE and should not be entries — a header quoted inside another
/// entry's body being the case that produced it.
///
/// It is the other half of <see cref="ChannelShape_Validator"/>: that one finds lines that look like
/// headers and do not parse, so a quoted header — which parses perfectly — walks straight past it. The
/// app then reads the quotation as a real entry, attributes a body to whoever was quoted, and burns an
/// index that a later entry collides with.
///
/// The fixtures below are the two real hits from `imp-2`'s own channel on 2026-08-13, and they are
/// used deliberately, because **one is a defect and one is not**: a quoted header at line 2671, and a
/// legitimate crossing at 14:44/15:07 where two authors allocated an index in the same minute. Nothing
/// mechanical separates them — which is the whole reason this is a screen and its output says so.
/// </summary>
public class ChannelIndexSequenceScreenTests
{
    /// <summary>
    /// THE DEFECT IT WAS BUILT FOR: a well-formed header sitting inside another entry's body. It
    /// parses, so the malformed-header validator is blind to it by design.
    /// </summary>
    [Fact]
    public void AQuotedHeaderIsFound()
    {
        var crossings = Screen(
            """
            ## [107] FROM implementer — 2026-08-13 21:34 — reporting an invisible entry

            At rest, right now:

            ## [106] FROM supervisor — 2026-08-13 21:33 — the entry I am quoting

            ...which proves the header is fine.

            ## [108] FROM implementer — 2026-08-13 21:35 — next entry
            """);

        var crossing = Assert.Single(crossings);

        Assert.Equal(106, crossing.Later.Index);
        Assert.Equal(107, crossing.Earlier.Index);
    }

    /// <summary>
    /// AND THE MALFORMED-HEADER VALIDATOR CANNOT SEE IT — asserted rather than asserted about, because
    /// "the two are complementary" is the entire argument for this class existing. If that validator
    /// ever did catch it, this screen would be a second copy of it.
    /// </summary>
    [Fact]
    public void TheMalformedHeaderValidatorIsBlindToTheSameLine()
    {
        const string QUOTED = "## [106] FROM supervisor — 2026-08-13 21:33 — the entry I am quoting";

        Assert.True(ChannelEntry_Parser.Is_HeaderLine(QUOTED));
        Assert.Empty(ChannelShape_Validator.Find_MalformedHeaders(QUOTED));
    }

    /// <summary>
    /// BOTH LINES ARE PRINTED WITH THEIR LINE NUMBERS, AND NEITHER IS ACCUSED.
    ///
    /// This test failed when it was first written, and the failure was the design being wrong rather
    /// than the expectation: the class labelled the EARLIER line the suspect, which is true when a
    /// higher-numbered header is quoted and FALSE for the case that actually happened here — where the
    /// quotation is the later of the two. See <see cref="TheIntruderIsTheLaterLineWhenAnOlderHeaderIsQuoted"/>
    /// and <see cref="TheIntruderIsTheEarlierLineWhenAHigherHeaderIsQuoted"/>: the two shapes put the
    /// intruder on opposite sides, so naming one would mislead about half the time.
    /// </summary>
    [Fact]
    public void BothLinesAreNamedAndNeitherIsAccused()
    {
        var crossing = Assert.Single(Screen(
            """
            ## [107] FROM implementer — 2026-08-13 21:34 — real

            ## [106] FROM supervisor — 2026-08-13 21:33 — quoted

            ## [108] FROM implementer — 2026-08-13 21:35 — real
            """));

        // Line 1 is the real entry, line 3 the quotation inside its body.
        Assert.Equal(1, crossing.Earlier.LineNumber);
        Assert.Equal(3, crossing.Later.LineNumber);

        var text = ChannelIndexSequence_Screen.Describe_Crossing(crossing);

        Assert.Contains("line 1", text);
        Assert.Contains("line 3", text);

        // REINSTATED. The commit that added the ordered assertions below DELETED these two while
        // fixing a finding about missing assertions, and said "it is not extra scope, it is the same
        // lines" — true of the lines, silent about what left with them. At that point no test
        // anywhere referenced either string, so the class's founding design rule — a SCREEN that
        // accuses nobody — was pinned by nothing: the clause could be deleted outright, or the
        // SUSPECT label its own docstring calls the original defect reinstated, with the suite green
        // (rev-6 F1, 2026-08-14).
        Assert.Contains("SCREEN, not a verdict", text);
        Assert.DoesNotContain("SUSPECT", text);

        // ORDERED, not merely present (rev-8 F6). "Contains line 1" and "contains line 3" both survive
        // swapping Earlier and Later throughout the format string, and the printed indices survive
        // deleting their clause outright — so the two assertions above pass while every printed
        // pairing is wrong. The class's own line says a screen that misstates which line it means
        // costs more than it saves; this is that sentence pinned.
        Assert.Contains("[107] then [106]", text);

        // PRESENCE BEFORE ORDER, because IndexOf returns -1 when absent and -1 precedes every found
        // position — so the ordering assertion alone passes when the "line n:" shape is missing
        // entirely rather than merely misordered. Decision 20 in miniature: never assert on a state
        // with two routes to it (rev-6 F5).
        Assert.Contains("line 1:", text);
        Assert.Contains("line 3:", text);
        Assert.True(text.IndexOf("line 1:") < text.IndexOf("line 3:"), $"the pair is printed in the wrong order: {text}");
    }

    /// <summary>
    /// THE FOUNDING SHAPE, and the screen could not see it: two adjacent headers carrying the SAME
    /// index. CLAUDE.md decision 12 records `option-lab-2` carrying two `[80]` and two `[81]` — the
    /// incident this whole class descends from — and `rev-8` counted 320 adjacent EQUAL pairs against
    /// 125 decreasing ones across the live channels on this machine.
    ///
    /// The comparison was `&lt;`, so equal produced nothing while two sentences of the class's own
    /// documentation promised it was caught. Its purpose line is "consuming an index that a later
    /// entry will collide with"; the collision itself was the invisible shape.
    /// </summary>
    [Fact]
    public void ARepeatedIndexIsFound()
    {
        var crossing = Assert.Single(Screen(
            """
            ## [80] FROM supervisor — 2026-08-10 15:20 — first entry to claim the number
            ## [80] FROM implementer — 2026-08-10 15:20 — same number, same minute, other author
            """));

        Assert.Equal(80, crossing.Earlier.Index);
        Assert.Equal(80, crossing.Later.Index);
    }

    /// <summary>
    /// AND THE NEXT COMPARISON DOES NOT RESCUE IT — the reason a strict test loses the pair entirely
    /// rather than reporting it one entry late. `80 &lt; 80` is false, then `81 &lt; 80` is false: with
    /// `&lt;` nothing fires anywhere, so the duplicate is not delayed, it is gone.
    /// </summary>
    [Fact]
    public void ARepeatedIndexFollowedByAResumedSequence_ProducesExactlyOneCrossing()
    {
        var crossing = Assert.Single(Screen(
            """
            ## [80] FROM supervisor — 2026-08-10 15:20 — a
            ## [80] FROM implementer — 2026-08-10 15:20 — b
            ## [81] FROM supervisor — 2026-08-10 15:25 — the sequence resumes
            """));

        Assert.Equal(80, crossing.Later.Index);
    }

    /// <summary>
    /// A REPEAT IS NAMED AS A REPEAT. "index runs backwards" is false about `[80]`, `[80]`, and it is
    /// the line a human reads in the log — the same class of defect as the docstring that claimed the
    /// repeat was caught, arriving through the fix for it.
    /// </summary>
    [Fact]
    public void ARepeatIsDescribedAsARepeat_NotAsRunningBackwards()
    {
        var repeat = ChannelIndexSequence_Screen.Describe_Crossing(Assert.Single(Screen(
            "## [80] FROM s — d — a\n## [80] FROM i — d — b")));

        Assert.Contains("index REPEATS: [80] then [80]", repeat);
        Assert.DoesNotContain("runs backwards", repeat);

        // And the decreasing case keeps its own true wording.
        var backwards = ChannelIndexSequence_Screen.Describe_Crossing(Assert.Single(Screen(
            "## [107] FROM s — d — a\n## [106] FROM s — d — quoted\n## [108] FROM s — d — b")));

        Assert.Contains("index runs backwards: [107] then [106]", backwards);
    }

    /// <summary>
    /// SHAPE ONE — quoting an OLDER header, which is what happened on this machine: the intruder is
    /// the LATER line of the pair.
    /// </summary>
    [Fact]
    public void TheIntruderIsTheLaterLineWhenAnOlderHeaderIsQuoted()
    {
        var crossing = Assert.Single(Screen("## [107] FROM a — d — real\n## [106] FROM b — d — QUOTED\n## [108] FROM a — d — real"));

        Assert.Contains("QUOTED", crossing.Later.Line);
    }

    /// <summary>
    /// SHAPE TWO — quoting a higher or foreign index: the intruder is the EARLIER line. Same screen,
    /// opposite side, which is the whole reason it accuses nobody.
    /// </summary>
    [Fact]
    public void TheIntruderIsTheEarlierLineWhenAHigherHeaderIsQuoted()
    {
        var crossing = Assert.Single(Screen("## [94] FROM a — d — real\n## [200] FROM b — d — QUOTED\n## [95] FROM a — d — real"));

        Assert.Contains("QUOTED", crossing.Earlier.Line);
    }

    /// <summary>
    /// A HIGHER-numbered intruder is caught too, by the next real entry falling back below it. This
    /// was measured before the class was written, against a claim that it would sail through.
    /// </summary>
    [Fact]
    public void AnIntruderWithAHigherIndexIsAlsoCaught()
    {
        var crossing = Assert.Single(Screen(
            """
            ## [94] FROM implementer — 2026-08-13 20:42 — real

            ## [200] FROM supervisor — 2026-08-13 20:41 — quoted from somewhere else

            ## [95] FROM implementer — 2026-08-13 20:43 — real
            """));

        Assert.Equal(200, crossing.Earlier.Index);
        Assert.Equal(95, crossing.Later.Index);
    }

    /// <summary>
    /// THE LIMIT, AND IT IS ONE SHAPE ONLY — the name says which, because the old one
    /// (<c>AQuotedHeaderAtTheEndOfTheFile…</c>) generalised this single fixture into a claim about
    /// both shapes, and the class docstring repeated it (rev-8 F3).
    ///
    /// A quoted header with a HIGHER index, still last in the file, is invisible: nothing has followed
    /// it to fall back below it. That window closes the moment anybody appends.
    /// </summary>
    [Fact]
    public void AQuotedHIGHERHeaderAtTheEndOfTheFileIsNotYetVisible()
    {
        Assert.Empty(Screen(
            """
            ## [94] FROM implementer — 2026-08-13 20:42 — real

            ## [200] FROM supervisor — 2026-08-13 20:41 — quoted, nothing follows it
            """));
    }

    /// <summary>
    /// AND THE OTHER SHAPE HAS NO WINDOW AT ALL — the one that actually happened here. A quoted OLDER
    /// header is a crossing against the line ABOVE it, so it fires with nothing following it.
    ///
    /// Not a guard for a code change; it is the missing half of the pair, and the pair is what makes
    /// the docstring's limit a true statement instead of a general one. The shape wrongly declared
    /// blind was the shape the class was written for, which is what made the wrong sentence worth a
    /// finding rather than a nit.
    /// </summary>
    [Fact]
    public void AQuotedOLDERHeaderAtTheEndOfTheFileIsCaughtImmediately()
    {
        var crossing = Assert.Single(Screen(
            """
            ## [107] FROM implementer — 2026-08-13 21:34 — real

            ## [106] FROM supervisor — 2026-08-13 21:33 — quoted, nothing follows it
            """));

        Assert.Equal(106, crossing.Later.Index);
    }

    /// <summary>
    /// A HEALTHY CHANNEL IS SILENT. Without this the screen could "work" by reporting everything, and
    /// a screen that always fires is a screen nobody reads.
    /// </summary>
    [Fact]
    public void AnOrdinaryChannelProducesNothing()
    {
        Assert.Empty(Screen(
            """
            ## [1] FROM supervisor — 2026-08-13 09:00 — brief

            ## [2] FROM implementer — 2026-08-13 09:05 — report

            ## [3] FROM supervisor — 2026-08-13 09:10 — accepted
            """));
    }

    /// <summary>
    /// DECISION 13: the archive comes first and the sequence spans both files. Read live-only, a
    /// COMPACTED channel looks like a file that starts at index 84 — and every compaction boundary
    /// reads as a hole. This is the failure that once told an owner their message was unanswered long
    /// after it had been answered.
    /// </summary>
    [Fact]
    public void TheArchiveIsReadFirstAndTheSequenceSpansBoth()
    {
        var headers = ChannelIndexSequence_Screen.Read_Headers(
            archiveText: "## [1] FROM supervisor — 2026-08-13 09:00 — old\n\n## [2] FROM implementer — 2026-08-13 09:05 — old",
            liveText: "## [3] FROM supervisor — 2026-08-13 09:10 — live\n\n## [4] FROM implementer — 2026-08-13 09:15 — live");

        Assert.Equal([1, 2, 3, 4], headers.Select(header => header.Index));
        Assert.Equal(ChannelIndexSequence_Screen.ARCHIVE_SOURCE, headers[0].Source);
        Assert.Equal(ChannelIndexSequence_Screen.LIVE_SOURCE, headers[3].Source);

        // No crossing: a compacted channel is healthy, and reading it live-only would invent one.
        Assert.Empty(ChannelIndexSequence_Screen.Find_Crossings(headers));
    }

    /// <summary>
    /// A crossing ACROSS the boundary is still found — the archive's last entry against the live
    /// file's first.
    /// </summary>
    [Fact]
    public void ACrossingAtTheArchiveBoundaryIsFound()
    {
        var crossing = Assert.Single(ChannelIndexSequence_Screen.Find_Crossings(
            ChannelIndexSequence_Screen.Read_Headers(
                archiveText: "## [9] FROM supervisor — 2026-08-13 09:00 — old",
                liveText: "## [8] FROM implementer — 2026-08-13 09:05 — quoted into the live file")));

        Assert.Equal(ChannelIndexSequence_Screen.ARCHIVE_SOURCE, crossing.Earlier.Source);
        Assert.Equal(ChannelIndexSequence_Screen.LIVE_SOURCE, crossing.Later.Source);
    }

    /// <summary>
    /// THE DE-DUPE KEY IS STABLE AS THE CHANNEL GROWS, which is what stops a permanent old crossing
    /// re-logging on every sweep for as long as the app runs. Appending further entries does not
    /// change the pair, so the key does not change.
    /// </summary>
    [Fact]
    public void TheDedupeKeySurvivesLaterAppends()
    {
        var first = Assert.Single(Screen("## [7] FROM s — d — a\n## [6] FROM s — d — quoted\n## [8] FROM s — d — b"));
        var second = Assert.Single(Screen("## [7] FROM s — d — a\n## [6] FROM s — d — quoted\n## [8] FROM s — d — b\n## [9] FROM s — d — c"));

        Assert.Equal(ChannelIndexSequence_Screen.Build_DedupeKey(first), ChannelIndexSequence_Screen.Build_DedupeKey(second));
    }

    /// <summary>
    /// AND IT SURVIVES A COMPACTION, which is the move that actually breaks a key here. The old key
    /// carried Source and LineNumber, and `Channel_Compactor` changes BOTH when it moves entries from
    /// the live file into the `.archive.md` sibling: same two lines, new key, so a crossing already
    /// absorbed as history came back as NEW on the next sweep — with the channel no longer at first
    /// sight, so it logged. That is the waterfall the key exists to prevent, arriving by the one
    /// route the append test could not see (rev-8 F5).
    ///
    /// Decision 13 is why this is the realistic case rather than a corner: compaction runs on these
    /// channels routinely, and it is the reason the screen reads the archive at all.
    /// </summary>
    [Fact]
    public void TheDedupeKeySurvivesACOMPACTION()
    {
        const string EARLIER = "## [7] FROM s — 2026-08-13 09:00 — real";
        const string QUOTED = "## [6] FROM s — 2026-08-13 08:55 — quoted";

        // Before: the pair is in the live file, near its top.
        var beforeCompaction = Assert.Single(ChannelIndexSequence_Screen.Find_Crossings(
            ChannelIndexSequence_Screen.Read_Headers(archiveText: "", liveText: $"{EARLIER}\n{QUOTED}\n## [8] FROM s — d — b")));

        // After: the compactor has moved those entries into the archive, so both lines carry a
        // different Source AND a different LineNumber while being the same two lines.
        var afterCompaction = Assert.Single(ChannelIndexSequence_Screen.Find_Crossings(
            ChannelIndexSequence_Screen.Read_Headers(
                archiveText: $"## [1] FROM s — d — older\n## [2] FROM s — d — older\n{EARLIER}\n{QUOTED}\n## [8] FROM s — d — b",
                liveText: "## [9] FROM s — d — the live file starts here now")));

        Assert.NotEqual(beforeCompaction.Earlier.LineNumber, afterCompaction.Earlier.LineNumber);
        Assert.NotEqual(beforeCompaction.Earlier.Source, afterCompaction.Earlier.Source);

        Assert.Equal(
            ChannelIndexSequence_Screen.Build_DedupeKey(beforeCompaction),
            ChannelIndexSequence_Screen.Build_DedupeKey(afterCompaction));
    }

    /// <summary>
    /// BOTH LINES ARE IN THE KEY — pinned by two crossings that differ in only one of them. The old
    /// test compared two keys built from the SAME crossing, so it passed with either half dropped, or
    /// with the whole key replaced by a constant (rev-8 F5). A control that reddens nothing is not
    /// coverage.
    /// </summary>
    [Fact]
    public void CrossingsDifferingInOnlyONELineKeepDifferentKeys()
    {
        var sharedEarlier = new ChannelHeaderLine(ChannelIndexSequence_Screen.LIVE_SOURCE, 1, 7, "## [7] FROM s — d — real");
        var firstQuote = new ChannelHeaderLine(ChannelIndexSequence_Screen.LIVE_SOURCE, 3, 6, "## [6] FROM s — d — first quotation");
        var secondQuote = new ChannelHeaderLine(ChannelIndexSequence_Screen.LIVE_SOURCE, 3, 6, "## [6] FROM s — d — a DIFFERENT quotation");

        Assert.NotEqual(
            ChannelIndexSequence_Screen.Build_DedupeKey(new ChannelIndexCrossing(sharedEarlier, firstQuote)),
            ChannelIndexSequence_Screen.Build_DedupeKey(new ChannelIndexCrossing(sharedEarlier, secondQuote)));

        // And the mirror case: the same quotation reached from two different entries.
        var otherEarlier = new ChannelHeaderLine(ChannelIndexSequence_Screen.LIVE_SOURCE, 1, 7, "## [7] FROM s — d — a DIFFERENT real entry");

        Assert.NotEqual(
            ChannelIndexSequence_Screen.Build_DedupeKey(new ChannelIndexCrossing(sharedEarlier, firstQuote)),
            ChannelIndexSequence_Screen.Build_DedupeKey(new ChannelIndexCrossing(otherEarlier, firstQuote)));
    }

    /// <summary>
    /// THE SEPARATOR CANNOT BE FORGED. Two different pairs whose concatenations are identical must
    /// still key differently — that is the entire job of the separator, and it was the mutation
    /// nothing in the suite could see. A newline is used because a header line provably cannot
    /// contain one; every printable separator can appear inside a subject.
    /// </summary>
    [Fact]
    public void TwoPairsThatConcatenateIdenticallyKeepDifferentKeys()
    {
        var crossing = new ChannelIndexCrossing(
            new ChannelHeaderLine(ChannelIndexSequence_Screen.LIVE_SOURCE, 1, 7, "## [7] FROM s — d — ab"),
            new ChannelHeaderLine(ChannelIndexSequence_Screen.LIVE_SOURCE, 3, 6, "## [6] FROM s — d — c"));

        var recut = new ChannelIndexCrossing(
            new ChannelHeaderLine(ChannelIndexSequence_Screen.LIVE_SOURCE, 1, 7, "## [7] FROM s — d — a"),
            new ChannelHeaderLine(ChannelIndexSequence_Screen.LIVE_SOURCE, 3, 6, "b## [6] FROM s — d — c"));

        Assert.NotEqual(
            ChannelIndexSequence_Screen.Build_DedupeKey(crossing),
            ChannelIndexSequence_Screen.Build_DedupeKey(recut));
    }

    /// <summary>And two DIFFERENT crossings are not collapsed into one key.</summary>
    [Fact]
    public void TwoDifferentCrossingsKeepDifferentKeys()
    {
        var crossings = Screen("## [7] FROM s — d — a\n## [6] FROM s — d — quoted\n## [8] FROM s — d — b\n## [2] FROM s — d — another quote\n## [9] FROM s — d — c");

        Assert.Equal(2, crossings.Count);
        Assert.NotEqual(
            ChannelIndexSequence_Screen.Build_DedupeKey(crossings[0]),
            ChannelIndexSequence_Screen.Build_DedupeKey(crossings[1]));
    }

    static IReadOnlyList<ChannelIndexCrossing> Screen(string liveText)
    {
        return ChannelIndexSequence_Screen.Find_Crossings(
            ChannelIndexSequence_Screen.Read_Headers(archiveText: "", liveText: liveText));
    }
}
