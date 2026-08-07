using AIOrchestratorCoreLib.Formatting;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Formatting;

public class TextSummaryFormatterTests
{
    [Fact]
    public void Take_Words_LongSubject_IsCutToTheWordLimit_WithAnEllipsis()
    {
        var summary = TextSummary_Formatter.Take_Words(
            "fix the screener so greedy and cluster discovery actually start after the download phase completes",
            TextSummary_Formatter.CARD_TASK_WORDS);

        Assert.Equal("fix the screener so greedy and cluster discovery actually start…", summary);
        Assert.Equal(TextSummary_Formatter.CARD_TASK_WORDS, summary.TrimEnd('…').Split(' ').Length);
    }

    [Fact]
    public void Take_Words_ShortSubject_IsLeftAlone_WithoutAnEllipsis()
    {
        Assert.Equal("fix the drift guard", TextSummary_Formatter.Take_Words("fix the drift guard", 10));
    }

    [Fact]
    public void Take_Words_CollapsesRunsOfWhitespaceAndNewlines()
    {
        Assert.Equal("brief for imp-2", TextSummary_Formatter.Take_Words("  brief\n\tfor   imp-2  ", 10));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Take_Words_NothingToShow_ReturnsEmpty(string text)
    {
        Assert.Equal(string.Empty, TextSummary_Formatter.Take_Words(text, 10));
    }
}
