using AIOrchestratorCoreLib.Formatting;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Formatting;

/// <summary>
/// The card's task label. The bar is "would a person glance at this and know what is happening" —
/// so no ellipsis, no mid-sentence cut, and never an empty or one-word label.
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
    public void Summarize_NeverEndsWithAnEllipsis()
    {
        var summary = TextSummary_Formatter.Summarize_Task(
            "investigate whether background watcher processes die and leave orphaned implementer sessions unreachable forever", MAX);

        Assert.DoesNotContain("…", summary);
        Assert.True(summary.Split(' ').Length <= MAX, $"'{summary}' should be at most {MAX} words");
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
