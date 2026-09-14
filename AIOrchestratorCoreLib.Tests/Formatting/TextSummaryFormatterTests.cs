using AIOrchestratorCoreLib.Formatting;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Formatting;

/// <summary>
/// The card's task label. The bar is "would a person glance at this and know what is happening" —
/// so summarise before cutting, never an empty or one-word label, plain text only, and a cut that
/// still has to happen says so with an ellipsis (owner, 2026-09-11: an unmarked cut read as a
/// broken sentence).
/// </summary>
public class TextSummaryFormatterTests
{
    const int MAX = TextSummary_Formatter.CARD_TASK_WORDS;

    [Fact]
    public void Summarize_KeepsTheTask_AndDropsTheJustification()
    {
        var summary = TextSummary_Formatter.Summarize_Task(
            "fix the screener so greedy and cluster discovery actually start after the download phase completes", MAX);

        Assert.Equal("fix the screener", summary);
    }

    [Theory]
    [InlineData("Task 3: wire the settings window", "wire the settings window")]
    [InlineData("3. wire the settings window", "wire the settings window")]
    [InlineData("[imp-2] wire the settings window", "wire the settings window")]
    [InlineData("brief for imp-2 — wire the settings window", "wire the settings window")]
    [InlineData("verdict: accepted, the pid fix holds", "accepted")]
    public void Summarize_StripsProtocolBookkeeping(string subject, string expected)
    {
        Assert.Equal(expected, TextSummary_Formatter.Summarize_Task(subject, MAX));
    }

    [Fact]
    public void Summarize_CutsAtTheFirstDetailBreak()
    {
        Assert.Equal("rebuild the discovery pipeline",
            TextSummary_Formatter.Summarize_Task("rebuild the discovery pipeline (pairs, baskets, brute force gate)", MAX));

        Assert.Equal("add the gear icon",
            TextSummary_Formatter.Summarize_Task("add the gear icon, then wire the settings window and the KB page", MAX));
    }

    [Fact]
    public void Summarize_AHardCut_EndsWithAnEllipsis_AndStaysWithinTheBudget()
    {
        // Was Summarize_NeverEndsWithAnEllipsis. Reversed 2026-09-11: the owner read an unmarked cut
        // on PULSE ("GO.** 278e14d35 is final. deep") as a sentence broken off, i.e. as a defect.
        var summary = TextSummary_Formatter.Summarize_Task(
            "investigate whether background watcher processes die and leave orphaned implementer sessions unreachable forever", MAX);

        Assert.EndsWith("…", summary);
        Assert.True(summary.Split(' ').Length <= MAX, $"'{summary}' should be at most {MAX} words");
    }

    [Fact]
    public void Summarize_NoCut_NoEllipsis()
    {
        // The mark means "words were cut away" — a summary that fits, or one shortened only by
        // dropping the justification clause, must not claim there is more.
        Assert.Equal("fix the screener",
            TextSummary_Formatter.Summarize_Task("fix the screener so greedy discovery starts after the download", MAX));
    }

    [Fact]
    public void Summarize_TheSubjectFromTheOwnersPulse_ArrivesAsPlainTextWithAnEllipsis()
    {
        // Verbatim shape from 2026-09-11: PULSE is sent without parse_mode, so the markers reached
        // the phone literally, and the word cut left one half of the bold pair dangling.
        var summary = TextSummary_Formatter.Summarize_Task(
            "**GO.** 278e14d35 is final. deep review found nothing blocking the merge to staging tonight", MAX);

        Assert.DoesNotContain("*", summary);
        Assert.StartsWith("GO. 278e14d35 is final. deep", summary);
        Assert.EndsWith("…", summary);
    }

    [Theory]
    [InlineData("GO.** 278e14d35 is final", "GO. 278e14d35 is final")]
    [InlineData("**fix** the `Channel_Parser` header", "fix the Channel_Parser header")]
    [InlineData("__wire__ the settings window", "wire the settings window")]
    [InlineData("## Wire the settings window", "Wire the settings window")]
    [InlineData("***both*** markers", "both markers")]
    [InlineData("**Verdict:** accepted, the pid fix holds", "accepted")]
    public void Summarize_StripsMarkdownMarkers_SoOnlyPlainTextRemains(string subject, string expected)
    {
        Assert.Equal(expected, TextSummary_Formatter.Summarize_Task(subject, MAX));
    }

    [Theory]
    [InlineData("rename snake__case_name and __init__ helpers", "rename snake__case_name and init helpers")]
    [InlineData("check 2 * 3 in the ratio", "check 2 * 3 in the ratio")]
    public void Summarize_LeavesWhatIsNotAMarker(string subject, string expected)
    {
        // An underscore run inside a word is an identifier, and a lone star is arithmetic.
        Assert.Equal(expected, TextSummary_Formatter.Summarize_Task(subject, MAX));
    }

    [Fact]
    public void Summarize_ASubjectThatIsOnlyMarkers_ReturnsEmpty_NotTheMarkers()
    {
        Assert.Equal(string.Empty, TextSummary_Formatter.Summarize_Task("****", MAX));
    }

    [Fact]
    public void Summarize_LongClauseWithNoBreak_DropsFillerRatherThanMeaning()
    {
        var summary = TextSummary_Formatter.Summarize_Task(
            "investigate whether background watcher processes die and leave orphaned implementer sessions unreachable forever", MAX);

        // The meaningful words survive; only filler is sacrificed.
        Assert.Contains("watcher", summary);
        Assert.Contains("orphaned", summary);
    }

    [Fact]
    public void Summarize_ShortSubject_IsLeftAlone()
    {
        Assert.Equal("fix the drift guard", TextSummary_Formatter.Summarize_Task("fix the drift guard", MAX));
    }

    [Fact]
    public void Summarize_ABadClauseCut_FallsBackInsteadOfLeavingOneWord()
    {
        // "fix it" alone would be useless — the fallback keeps enough to be meaningful.
        var summary = TextSummary_Formatter.Summarize_Task("fix, because the guard was inverted", MAX);

        Assert.True(summary.Split(' ').Length >= 2, $"'{summary}' is too short to mean anything");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Summarize_NothingToShow_ReturnsEmpty(string subject)
    {
        Assert.Equal(string.Empty, TextSummary_Formatter.Summarize_Task(subject, MAX));
    }
}
