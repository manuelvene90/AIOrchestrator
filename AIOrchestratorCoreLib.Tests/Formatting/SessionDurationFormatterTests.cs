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
}
