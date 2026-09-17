using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Reviewing;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Reviewing;

/// <summary>
/// THE RELAY CARRIES TWO THINGS AND A POINTER, because the reviewer's skill says a re-review reviews
/// exactly two things: the DELTA and the earlier findings. An entry carrying the branch, or the
/// original brief, or a fresh instruction of the app's own invention would re-open the whole-branch
/// re-read that cost `fincanva-3` $49 across five rounds whose finding count never fell.
/// </summary>
public class RoutedReportComposerTests
{
    const string BRIEF = "Check F1 and F3 only. F2 was accepted as stated.";

    static IRerouteContract Routed()
    {
        return RerouteContract_Factory.CreateFrom_Routed(
            Declared(),
            reportIdentity: "9f2a1c", headCommit: "def5678", routedUtc: DateTime.UtcNow);
    }

    static IRerouteContract Declared()
    {
        return RerouteContract_Factory.Create_Declared(
            id: "c1", orchId: "repo-1", implementerId: "imp-1", reviewerId: "rev-1",
            baseCommit: "abc1234", brief: BRIEF, declaredUtc: DateTime.UtcNow);
    }

    static IChannelEntry Report(string body)
    {
        return Report("F1 and F3 fixed", body);
    }

    static IChannelEntry Report(string subject, string body)
    {
        return ChannelEntry_Parser.Parse_All($"## [8] FROM implementer — 2026-09-15 10:20 — {subject}\n{body}\n")[0];
    }

    [Fact]
    public void TheSubjectIsTaggedSoTheReviewerWakesOnIt()
    {
        var composed = RoutedReport_Composer.Compose(Routed(), Report("FIXED: def5678"));

        Assert.True(RoutedReport_Tag.Is_Routed(composed.Subject));
        Assert.Contains("imp-1", composed.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBodyNamesTheDeltaAsACommandAndCarriesBothCommits()
    {
        var composed = RoutedReport_Composer.Compose(Routed(), Report("FIXED: def5678"));

        Assert.Contains("git diff abc1234..def5678", composed.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSupervisorsBriefIsCarriedVerbatim()
    {
        Assert.Contains(BRIEF, RoutedReport_Composer.Compose(Routed(), Report("FIXED: def5678")).Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheImplementersReportIsCarriedVerbatim()
    {
        var composed = RoutedReport_Composer.Compose(Routed(), Report("FIXED: def5678\nF1: the guard now runs before the write.\nF3: covered by a new case."));

        Assert.Contains("F3: covered by a new case.", composed.Body, StringComparison.Ordinal);
        Assert.Contains("F1 and F3 fixed", composed.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A LONG REPORT IS CUT, AND THE CUT IS ANNOUNCED. A silent truncation is a hole, and a reviewer
    /// reasoning from half a claim it believes is whole is the worst shape a wrong answer can take.
    ///
    /// <para>
    /// ASSERTED ON THE ARITHMETIC AND ON WHICH END SURVIVED, not on the word TRUNCATED: a cut that
    /// kept the TAIL, or that miscounted what it dropped, would carry that word just as proudly
    /// (decision 20 — never assert on a state with two routes to it). The report's own first line is
    /// what a reviewer needs, so the front is the half that has to be there.
    /// </para>
    /// </summary>
    [Fact]
    public void AnOverlongReportIsTruncatedWithTheOmissionStated()
    {
        // 30 + MAXIMUM + 12 characters, so exactly 42 fall off the end — and Trim('\n') at the parse
        // leaves the body the length written here.
        var reportBody = "FIXED: def5678\nFIRST-LINE-KEPT" + new string('x', RoutedReport_Composer.MAXIMUM_REPORT_CHARACTERS) + "TAIL-DROPPED";

        var composed = RoutedReport_Composer.Compose(Routed(), Report(reportBody));

        Assert.Contains("FIRST-LINE-KEPT", composed.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("TAIL-DROPPED", composed.Body, StringComparison.Ordinal);
        Assert.Contains("TRUNCATED, 42 characters omitted", composed.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// AND THE CUT DOES NOT FIRE ON AN ORDINARY REPORT — the control for the case above, without
    /// which "it says TRUNCATED when it is long" is satisfied by a composer that always says it.
    /// </summary>
    [Fact]
    public void AReportInsideTheLimitIsCarriedWholeAndSaysNothingAboutTruncation()
    {
        var composed = RoutedReport_Composer.Compose(Routed(), Report("FIXED: def5678\n" + new string('x', RoutedReport_Composer.MAXIMUM_REPORT_CHARACTERS - 15)));

        Assert.DoesNotContain("TRUNCATED", composed.Body, StringComparison.Ordinal);
        Assert.Contains(new string('x', RoutedReport_Composer.MAXIMUM_REPORT_CHARACTERS - 15), composed.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// AND THE BRANCH IS NOT IN IT. The one instruction this entry gives is the reviewer's own rule;
    /// nothing here re-opens the scope its skill spent five rounds of `fincanva-3` learning to close.
    /// </summary>
    [Fact]
    public void TheEntryNeverInvitesAFreshPassOverTheBranch()
    {
        var body = RoutedReport_Composer.Compose(Routed(), Report("FIXED: def5678")).Body;

        Assert.DoesNotContain("the branch", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delta", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// THE APP COPIES, IT NEVER COMPOSES — the task's title, asserted mechanically rather than
    /// promised in a docstring. Take two reports that say different things, cut each one's own words
    /// out of its relay, and what is left has to be IDENTICAL: a frame that varied with the content
    /// would be the app saying something ABOUT the fix, and the reviewer would read that sentence as
    /// if its supervisor had written it.
    ///
    /// <para>
    /// This is the case a summary, a finding count, a "the implementer claims…" preamble, or any
    /// re-wording fails, and it is the only one here that does — every other test in this file is
    /// satisfied by a composer that copies faithfully AND editorialises beside it.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFrameIsTheSameWhateverTheReportSays()
    {
        // THE TWO REPORTS DIFFER IN EVERY QUANTITY A COMPOSED SENTENCE COULD BE DERIVED FROM — line
        // count, character count, word count, and the subject's own length. A first draft of this
        // test used two same-shaped reports, and a mutation that appended "the implementer wrote N
        // lines about it" stayed GREEN because N was 2 in both.
        const string ONE = "F1: one.";
        const string OTHER = "F1: the guard now runs before the write.\nF3: covered by a new case.\nF5: reverted.\nAnd a fourth line of prose nobody needed.";

        var frameOfOne = RoutedReport_Composer.Compose(Routed(), Report("F1 fixed", ONE)).Body
            .Replace(ONE, "<<report>>", StringComparison.Ordinal)
            .Replace("F1 fixed", "<<subject>>", StringComparison.Ordinal);

        var frameOfOther = RoutedReport_Composer.Compose(Routed(), Report("F1 and F3 and F5 fixed, and one more thing", OTHER)).Body
            .Replace(OTHER, "<<report>>", StringComparison.Ordinal)
            .Replace("F1 and F3 and F5 fixed, and one more thing", "<<subject>>", StringComparison.Ordinal);

        Assert.Equal(frameOfOne, frameOfOther);
    }

    /// <summary>
    /// A CONTRACT THAT IS ONLY DECLARED HAS NO RIGHT-HAND SIDE, so there is no delta to name. Left to
    /// interpolate, a null head commit writes `git diff abc1234..` — a line that READS like a command
    /// and diffs the working tree against a commit, which is not the thing the reviewer was asked to
    /// look at. The matcher's refusals are the designed fail-open for channel input; reaching this
    /// method with a half-built contract is a caller fault, and the factory draws the same line.
    /// </summary>
    [Fact]
    public void ADeclaredContractIsRefusedRatherThanRelayedWithHalfADelta()
    {
        var refusal = Assert.Throws<ArgumentException>(
            () => RoutedReport_Composer.Compose(Declared(), Report("FIXED: def5678")));

        Assert.Contains("ROUTED", refusal.Message, StringComparison.Ordinal);
    }
}
