using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Reviewing;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Reviewing;

/// <summary>
/// THE APP NEVER DECIDES THAT A FIX IS GOOD. It decides that the report the supervisor said to wait
/// for has arrived and names a delta — mechanical facts, no reading of prose. Everything the
/// supervisor actually judges stays the supervisor's, at the re-verdict.
///
/// <para>
/// AND EVERY REFUSAL IS A DESIGNED FAIL-OPEN, asserted ON ITS OWN WORDS rather than on
/// <c>Matched == false</c> alone: a refusal that could have come from any of six predicates is a
/// test that passes with the work not done (decision 20), and the refusal string is the line the
/// sweep writes to <c>orchestrator.log.jsonl</c> — decision 21's "which predicate failed".
/// </para>
/// </summary>
public class FixReportMatcherTests
{
    const string DECLARATION = "## [7] FROM supervisor — 2026-09-15 10:00 — VERDICT\nFix F1.\n\nREROUTE: rev-1 from abc1234\nCheck F1 only.\n";

    static IRerouteContract Contract()
    {
        return RerouteContract_Factory.Create_Declared(
            id: "c1", orchId: "repo-1", implementerId: "imp-1", reviewerId: "rev-1",
            baseCommit: "abc1234", brief: "Check F1 only.", declaredUtc: DateTime.UtcNow);
    }

    static (IReadOnlyList<IChannelEntry> Entries, string DeclarationIdentity) Channel(params string[] after)
    {
        var entries = ChannelEntry_Parser.Parse_All(DECLARATION + string.Concat(after));

        return (entries, ChannelEntry_Digest.Compute(entries[0]));
    }

    static FixReportMatch Find(params string[] after)
    {
        var channel = Channel(after);

        return FixReport_Matcher.Find(Contract(), channel.Entries, channel.DeclarationIdentity);
    }

    const string A_GOOD_REPORT = "## [8] FROM implementer — 2026-09-15 10:20 — F1 fixed\nFIXED: def5678\n214 tests green.\n";

    /// <summary>
    /// THE LIVE CONTROL every refusal below is read against. Without it, a suite of "nothing was
    /// routed" assertions passes just as well against a matcher that routes nothing ever.
    /// </summary>
    [Fact]
    public void AReportDeclaringAFixedCommitSatisfiesTheContract()
    {
        var match = Find(A_GOOD_REPORT);

        Assert.True(match.Matched);
        Assert.Equal("def5678", match.HeadCommit);
        Assert.Equal(string.Empty, match.Refusal);
        Assert.Equal("F1 fixed", match.Report!.Subject);
    }

    [Fact]
    public void AReportWithNoFixedLineSatisfiesNothingAndSaysWhy()
    {
        var match = Find("## [8] FROM implementer — 2026-09-15 10:20 — F1 fixed\nCommitted def5678. 214 tests green.\n");

        Assert.False(match.Matched);
        Assert.Equal(FixReport_Matcher.NO_DECLARATION, match.Refusal);
    }

    /// <summary>
    /// A MARKER INSIDE A SENTENCE IS DISCUSSION. The implementer writing "I will send FIXED: when the
    /// suite is green" has declared nothing, and the one matcher's line-start rule in the body is
    /// what tells that from a report.
    /// </summary>
    [Fact]
    public void AMemberMentioningTheMarkerMidSentenceHasDeclaredNothing()
    {
        var match = Find("## [8] FROM implementer — 2026-09-15 10:20 — working\nI will send FIXED: def5678 once the suite is green.\n");

        Assert.False(match.Matched);
        Assert.Equal(FixReport_Matcher.NO_DECLARATION, match.Refusal);
    }

    /// <summary>
    /// A REPORT FILED BEFORE THE CONTRACT EXISTED IS NOT AN ANSWER TO IT. Asked of FILE ORDER, never
    /// of the `[n]` in the header, which is agent-written and has duplicated in production
    /// (CLAUDE.md decision 12).
    /// </summary>
    [Fact]
    public void AnEarlierFixedEntryDoesNotSatisfyALaterContract()
    {
        var entries = ChannelEntry_Parser.Parse_All(
            "## [6] FROM implementer — 2026-09-15 09:00 — earlier work\nFIXED: 1111111\n" + DECLARATION);

        var match = FixReport_Matcher.Find(Contract(), entries, ChannelEntry_Digest.Compute(entries[1]));

        Assert.False(match.Matched);
        Assert.Equal(FixReport_Matcher.PREDATES_THE_CONTRACT, match.Refusal);
    }

    /// <summary>
    /// AND THE `[n]` IS NOT CONSULTED EVEN WHEN IT CONTRADICTS THE FILE. Two `[80]`s in one channel
    /// is a live incident, not a hypothetical, and a report whose header number went BACKWARDS is
    /// still the report that was filed last.
    /// </summary>
    [Fact]
    public void FileOrderDecidesWhenTheHeaderNumberDisagreesWithIt()
    {
        var match = Find("## [3] FROM implementer — 2026-09-15 10:20 — F1 fixed\nFIXED: def5678\n");

        Assert.True(match.Matched);
        Assert.Equal("def5678", match.HeadCommit);
    }

    /// <summary>
    /// THE SUPERVISOR'S OWN DECLARATION OF THE MARKER IS NOT A REPORT. Asserted on a line-start
    /// `FIXED:` on purpose: a fixture that ALSO failed the marker test would pass with the author
    /// predicate deleted, which is the two-routes trap (decision 20).
    /// </summary>
    [Fact]
    public void OnlyAMemberFilesAReport()
    {
        var match = Find("## [8] FROM supervisor — 2026-09-15 10:20 — reminder\nFIXED: def5678\n");

        Assert.False(match.Matched);
        Assert.Equal(FixReport_Matcher.NO_DECLARATION, match.Refusal);
    }

    /// <summary>
    /// NOR IS THE APP'S OWN RELAY, which quotes the implementer's report — including its `FIXED:`
    /// line — into the reviewer's channel. An app entry read as a report is the app satisfying its
    /// own contract.
    /// </summary>
    [Fact]
    public void TheAppsOwnEntryIsNotAReportEither()
    {
        var match = Find("## [8] FROM app — 2026-09-15 10:20 — routed\nFIXED: def5678\n");

        Assert.False(match.Matched);
        Assert.Equal(FixReport_Matcher.NO_DECLARATION, match.Refusal);
    }

    [Fact]
    public void TwoFixedLinesInOneReportAreAmbiguousAndRouteNothing()
    {
        var match = Find("## [8] FROM implementer — 2026-09-15 10:20 — F1 fixed\nFIXED: def5678\nFIXED: 9999999\n");

        Assert.False(match.Matched);
        Assert.Equal(FixReport_Matcher.AMBIGUOUS_HEAD, match.Refusal);
    }

    /// <summary>
    /// BUT A QUOTED SECOND LINE IS NOT A SECOND LINE. An implementer quoting the brief back — the
    /// commonest thing a report does — must not make its own report ambiguous.
    /// </summary>
    [Fact]
    public void AQuotedSecondMarkerDoesNotMakeAReportAmbiguous()
    {
        var match = Find("## [8] FROM implementer — 2026-09-15 10:20 — F1 fixed\n> FIXED: 9999999 was the brief's example\nFIXED: def5678\n");

        Assert.True(match.Matched);
        Assert.Equal("def5678", match.HeadCommit);
    }

    [Fact]
    public void AFixedLineThatNamesNoCommitRoutesNothing()
    {
        var match = Find("## [8] FROM implementer — 2026-09-15 10:20 — F1 fixed\nFIXED: on the branch\n");

        Assert.False(match.Matched);
        Assert.Equal(FixReport_Matcher.NO_COMMIT, match.Refusal);
    }

    [Fact]
    public void AFixedCommitEqualToTheBaseIsNoDelta()
    {
        var match = Find("## [8] FROM implementer — 2026-09-15 10:20 — nothing to do\nFIXED: abc1234\n");

        Assert.False(match.Matched);
        Assert.Equal(FixReport_Matcher.NO_DELTA, match.Refusal);
    }

    /// <summary>A SHA IS A SHA WHICHEVER CASE IT WAS TYPED IN — including when that is what makes it
    /// equal to the base.</summary>
    [Fact]
    public void TheCaseOfTheShaChangesNothing()
    {
        Assert.Equal("def5678", Find("## [8] FROM implementer — 2026-09-15 10:20 — F1 fixed\nFIXED: DEF5678\n").HeadCommit);
        Assert.Equal(FixReport_Matcher.NO_DELTA, Find("## [8] FROM implementer — 2026-09-15 10:20 — none\nFIXED: ABC1234\n").Refusal);
    }

    /// <summary>
    /// AND THE NEWEST ONE WINS. An implementer that reports twice — a partial fix, then the rest —
    /// has moved the head; re-reviewing the first would review a delta that no longer exists.
    /// </summary>
    [Fact]
    public void TheNewestFixedEntryIsTheOneRouted()
    {
        var match = Find(
            "## [8] FROM implementer — 2026-09-15 10:20 — half of F1\nFIXED: def5678\n",
            "## [9] FROM implementer — 2026-09-15 10:40 — the rest of F1\nFIXED: 0abcdef\n");

        Assert.True(match.Matched);
        Assert.Equal("0abcdef", match.HeadCommit);
        Assert.Equal("the rest of F1", match.Report!.Subject);
    }

    /// <summary>
    /// A REVIEWER'S OWN CHANNEL ENTRY IS A MEMBER ENTRY TOO, and this contract is the implementer's.
    /// The matcher is handed one member's channel by the caller, so it screens the KIND of author,
    /// not the id — this pins that a reviewer filing a `FIXED:` on the implementer's channel (which
    /// cannot happen: members never write to each other) is still read as a member report, and says
    /// so rather than leaving it implicit.
    /// </summary>
    [Fact]
    public void AnyMemberAuthorQualifiesBecauseTheCallerChoseTheChannel()
    {
        var match = Find("## [8] FROM solo — 2026-09-15 10:20 — F1 fixed\nFIXED: def5678\n");

        Assert.True(match.Matched);
    }

    /// <summary>
    /// A DECLARATION THAT IS NOT IN THE ENTRIES HANDED OVER ROUTES NOTHING. The live file is not
    /// stable — <c>Channel_Compactor</c> archives the oldest entries above 90 — and "everything left
    /// must therefore be newer" is an inference, not a fact the caller proved. It is also what a
    /// caller passing the WRONG channel looks like. Refuse, and name it.
    /// </summary>
    [Fact]
    public void ADeclarationMissingFromTheEntriesRoutesNothing()
    {
        var entries = ChannelEntry_Parser.Parse_All(A_GOOD_REPORT);

        var match = FixReport_Matcher.Find(Contract(), entries, "0000000000000000");

        Assert.False(match.Matched);
        Assert.Equal(FixReport_Matcher.DECLARATION_NOT_IN_CHANNEL, match.Refusal);
    }

    /// <summary>
    /// A CHANNEL WITH ONLY THE DECLARATION IN IT IS THE NORMAL STATE for most of a fix round, and
    /// must be a quiet refusal rather than anything at all.
    /// </summary>
    [Fact]
    public void NothingFiledYetIsTheOrdinaryAnswer()
    {
        var match = Find();

        Assert.False(match.Matched);
        Assert.Equal(FixReport_Matcher.NO_DECLARATION, match.Refusal);
        Assert.Null(match.Report);
        Assert.Equal(string.Empty, match.HeadCommit);
    }

    /// <summary>
    /// THE WORD COMES FROM THE GRAMMAR. Every fixture above spells `FIXED:`; this is the one case
    /// that says where that spelling is allowed to live.
    /// </summary>
    [Fact]
    public void TheWordIsTheGrammarsWord()
    {
        Assert.Equal("FIXED:", ChannelGrammar.FIXED);
    }
}
