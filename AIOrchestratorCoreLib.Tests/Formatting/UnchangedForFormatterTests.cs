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
    /// The step these cases were written at, NAMED rather than read from the catalogue's default: they
    /// pin how the formatter rounds at five, and would stay true if the shipped default moved.
    /// </summary>
    const int FIVE_MINUTE_STEP = 5;

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
        Assert.Equal(expected, UnchangedFor_Formatter.Describe_OrNull(TimeSpan.FromMinutes(minutes), FIVE_MINUTE_STEP));
    }

    /// <summary>"unchanged 2 min" is not news — it is what a working orchestration looks like.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void ItSaysNothingWhileTheFiguresAreStillMoving(int minutes)
    {
        Assert.Null(UnchangedFor_Formatter.Describe_OrNull(TimeSpan.FromMinutes(minutes), FIVE_MINUTE_STEP));
    }

    /// <summary>
    /// A NEGATIVE SPAN SAYS NOTHING rather than a confident wrong number — item 12's rule, and the
    /// shape that once rendered a future stamp as "on task under a minute" indefinitely.
    /// </summary>
    [Fact]
    public void ANegativeSpanSaysNothing()
    {
        Assert.Null(UnchangedFor_Formatter.Describe_OrNull(TimeSpan.FromMinutes(-30), FIVE_MINUTE_STEP));
    }

    /// <summary>
    /// A STEP COARSER THAN THE QUIET THRESHOLD SAYS NOTHING UNTIL ITS FIRST WHOLE STEP. `pulse.stepMinutes`
    /// is the owner's now (plan 03) and may be 15 or 60: twelve minutes at a fifteen-minute step floors
    /// to zero, and zero through the duration formatter reads "under a minute" — a confident wrong
    /// number about figures that have stood still for twelve. Item 12's rule: nothing, not that.
    /// </summary>
    [Fact]
    public void AStepCoarserThanTheQuietThreshold_SaysNothingBeforeItsFirstWholeStep()
    {
        Assert.Null(UnchangedFor_Formatter.Describe_OrNull(TimeSpan.FromMinutes(12), 15));
        Assert.Equal("unchanged 15 min", UnchangedFor_Formatter.Describe_OrNull(TimeSpan.FromMinutes(16), 15));
    }
}
