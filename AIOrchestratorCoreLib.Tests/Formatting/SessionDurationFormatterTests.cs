using AIOrchestratorCoreLib.Formatting;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Formatting;

/// <summary>
/// Channel header timestamps are written by AGENTS, so they are untrusted input. These guard the
/// reading of that field — the member cards' "on task" figure comes straight through here.
/// </summary>
public class SessionDurationFormatterTests
{
    static readonly DateTime NOW = new(2026, 8, 10, 15, 20, 0);

    [Theory]
    [InlineData("2026-08-10 15:20", "under a minute")]
    [InlineData("2026-08-10 15:19", "1 min")]
    [InlineData("2026-08-10 14:50", "30 min")]
    [InlineData("2026-08-10 12:05", "3 h 15 min")]
    [InlineData("2026-08-08 09:20", "2 d 6 h")]
    public void Describe_SinceStamp_ReadsAnOrdinaryPastStamp(string stamp, string expected)
    {
        Assert.Equal(expected, SessionDuration_Formatter.Describe_SinceStamp_OrNull(stamp, NOW));
    }

    /// <summary>
    /// The 2026-08-10 defect, verbatim: a supervisor stamped 2026-08-11 01:34 on an entry written
    /// at 15:20 the day before. That negative span used to render as "on task under a minute" for
    /// hours. Saying nothing is correct; a confident wrong number is not.
    /// </summary>
    [Fact]
    public void Describe_SinceStamp_RefusesAStampFromTheFuture()
    {
        Assert.Null(SessionDuration_Formatter.Describe_SinceStamp_OrNull("2026-08-11 01:34", NOW));
    }

    /// <summary>A minute-rounded stamp written moments ago is skew, not a wrong clock — still shown.</summary>
    [Fact]
    public void Describe_SinceStamp_ToleratesSmallSkewAsJustNow()
    {
        Assert.Equal("under a minute", SessionDuration_Formatter.Describe_SinceStamp_OrNull("2026-08-10 15:21", NOW));
    }

    [Fact]
    public void Describe_SinceStamp_ReturnsNullForAnUnparseableStamp()
    {
        Assert.Null(SessionDuration_Formatter.Describe_SinceStamp_OrNull("", NOW));
        Assert.Null(SessionDuration_Formatter.Describe_SinceStamp_OrNull("yesterday-ish", NOW));
    }

    /// <summary>
    /// The UI once carried its own copy of this wording WITHOUT the negative guard, and that copy
    /// is what produced the wrong reading. Pinning the guard here keeps the single implementation
    /// honest for every surface that delegates to it.
    /// </summary>
    [Fact]
    public void Describe_ClampsANegativeSpanInsteadOfCallingItUnderAMinute()
    {
        Assert.Equal("now", SessionDuration_Formatter.Describe(TimeSpan.FromHours(-10)));
    }

    /// <summary>
    /// THE STEPPED READING — the owner's ruling of 2026-09-10 for PULSE's member rows: five-minute
    /// granularity so a surface that is rewritten whenever its text changes stops being rewritten for
    /// the minute hand.
    ///
    /// Every boundary, because the interesting cases are all at the edges: below the first step, ON a
    /// step, one minute short of the next, and across the hour where the shared renderer switches
    /// format. It FLOORS, so a row never claims more elapsed time than there is.
    /// </summary>
    [Theory]
    [InlineData(0, "under 5 min")]
    [InlineData(1, "under 5 min")]
    [InlineData(4, "under 5 min")]
    [InlineData(5, "5 min")]
    [InlineData(9, "5 min")]
    [InlineData(10, "10 min")]
    [InlineData(59, "55 min")]
    [InlineData(60, "1 h 0 min")]
    [InlineData(64, "1 h 0 min")]
    [InlineData(65, "1 h 5 min")]
    public void Describe_SinceStamp_Stepped_FloorsToTheStep(int elapsedMinutes, string expected)
    {
        var now = new DateTime(2026, 8, 12, 14, 0, 0);
        var stamp = now.AddMinutes(-elapsedMinutes).ToString("yyyy-MM-dd HH:mm");

        Assert.Equal(expected, SessionDuration_Formatter.Describe_SinceStamp_Stepped_OrNull(stamp, now, 5));
    }

    /// <summary>
    /// AN UNTRUSTED STAMP IS STILL NOTHING, unchanged by the stepping — decision 12. A step of five
    /// minutes must not turn "I cannot date this" into "under 5 min", which would be a confident
    /// wrong number where the whole rule is to give none.
    /// </summary>
    [Theory]
    [InlineData("not a date")]
    [InlineData("")]
    [InlineData("2026-08-12 23:00")]
    public void Describe_SinceStamp_Stepped_RefusesWhatTheUnsteppedOneRefuses(string stampText)
    {
        var now = new DateTime(2026, 8, 12, 14, 0, 0);

        Assert.Null(SessionDuration_Formatter.Describe_SinceStamp_Stepped_OrNull(stampText, now, 5));
        Assert.Null(SessionDuration_Formatter.Describe_SinceStamp_OrNull(stampText, now));
    }

    /// <summary>A step below a minute is a division by zero waiting to happen, so it is refused loudly.</summary>
    [Fact]
    public void Describe_SinceStamp_Stepped_RefusesAStepOfNothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SessionDuration_Formatter.Describe_SinceStamp_Stepped_OrNull("2026-08-12 13:56", new DateTime(2026, 8, 12, 14, 0, 0), 0));
    }
}
