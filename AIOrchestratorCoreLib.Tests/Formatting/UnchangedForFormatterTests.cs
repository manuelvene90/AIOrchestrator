using AIOrchestratorCoreLib.Formatting;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Formatting;

/// <summary>
/// The auto-updating status line's answer to "has anything moved?" — the owner asked for this here
/// INSTEAD of a delta, because the line refreshes constantly and a difference would read zero.
/// </summary>
public class UnchangedForFormatterTests
{
    /// <summary>
    /// COARSENESS IS THE FEATURE, not an approximation anyone should tidy up. The status line is
    /// edited whenever its text changes, so a duration ticking by the minute would make the quietest
    /// topic the busiest one and defeat the decider's "nothing has changed" answer entirely.
    /// </summary>
    [Theory]
    [InlineData(10, "unchanged 10 min")]
    [InlineData(14, "unchanged 10 min")]
    [InlineData(15, "unchanged 15 min")]
    [InlineData(29, "unchanged 25 min")]
    public void ItStepsInFiveMinuteJumpsSoTheLineIsRarelyEdited(int minutes, string expected)
    {
        Assert.Equal(expected, UnchangedFor_Formatter.Describe_OrNull(TimeSpan.FromMinutes(minutes)));
    }

    /// <summary>"unchanged 2 min" is not news — it is what a working orchestration looks like.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void ItSaysNothingWhileTheFiguresAreStillMoving(int minutes)
    {
        Assert.Null(UnchangedFor_Formatter.Describe_OrNull(TimeSpan.FromMinutes(minutes)));
    }

    /// <summary>
    /// A NEGATIVE SPAN SAYS NOTHING rather than a confident wrong number — item 12's rule, and the
    /// shape that once rendered a future stamp as "on task under a minute" indefinitely.
    /// </summary>
    [Fact]
    public void ANegativeSpanSaysNothing()
    {
        Assert.Null(UnchangedFor_Formatter.Describe_OrNull(TimeSpan.FromMinutes(-30)));
    }
}
