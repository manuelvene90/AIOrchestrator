using AIOrchestratorCoreLib.Limits;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Limits;

/// <summary>
/// The tolerant parser's window IDENTITY — the value alerts de-duplicate on. It is extracted in the
/// same spirit as the percentages themselves: by hint, from the reading's own container, and never
/// at the cost of the reading. This parser exists to survive a Claude Code schema change nobody
/// warned us about, so every case here checks that a payload it cannot fully understand still
/// yields its percentages.
/// </summary>
public class LimitDataParserWindowsTests
{
    /// <summary>The shape actually on disk (Claude Code 2.1.227).</summary>
    const string REAL_PAYLOAD =
        """{"rate_limits":{"five_hour":{"used_percentage":40,"resets_at":1786493400},"seven_day":{"used_percentage":89,"resets_at":1786953600}}}""";

    [Fact]
    public void Extract_LimitWindows_TakesEachWindowsIdentityFromItsOwnContainer()
    {
        var windows = LimitData_Parser.Extract_LimitWindows(REAL_PAYLOAD);

        Assert.Equal(40, windows["rate_limits.five_hour.used_percentage"].Percent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786493400).UtcDateTime, windows["rate_limits.five_hour.used_percentage"].WindowResetsAtUtc);

        // The neighbouring window must not leak its stamp across — that would make two different
        // windows look like one and re-introduce exactly the de-duplication bug being fixed.
        Assert.Equal(89, windows["rate_limits.seven_day.used_percentage"].Percent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786953600).UtcDateTime, windows["rate_limits.seven_day.used_percentage"].WindowResetsAtUtc);
    }

    /// <summary>The percentages must survive a payload with no reset field at all — degrade, never drop.</summary>
    [Fact]
    public void Extract_LimitWindows_NoResetField_StillReportsThePercent_WithNoIdentity()
    {
        var windows = LimitData_Parser.Extract_LimitWindows("""{"rate_limits":{"five_hour":{"used_percentage":91}}}""");

        var window = Assert.Single(windows);
        Assert.Equal(91, window.Value.Percent);
        Assert.Null(window.Value.WindowResetsAtUtc);
    }

    /// <summary>
    /// A future schema could write the instant as text. It still places the window in time, which is
    /// the only kind of identity worth having — both "is this newer" and "has this already reset"
    /// have to be answerable from it.
    /// </summary>
    [Fact]
    public void Extract_LimitWindows_ATimestampWrittenAsText_StillIdentifiesTheWindow()
    {
        var windows = LimitData_Parser.Extract_LimitWindows(
            """{"rate_limits":{"five_hour":{"used_percentage":91,"resets_at":"2026-08-17T10:00:00Z"}}}""");

        Assert.Equal(
            new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc),
            Assert.Single(windows).Value.WindowResetsAtUtc);
    }

    /// <summary>
    /// An identity we cannot place in time is worse than none: it would make both "is this newer"
    /// and "has this window already reset" silently unanswerable.
    /// </summary>
    [Fact]
    public void Extract_LimitWindows_AnUnorderableResetValue_IsTreatedAsNoIdentity()
    {
        var windows = LimitData_Parser.Extract_LimitWindows(
            """{"rate_limits":{"five_hour":{"used_percentage":91,"resets_at":"whenever the window rolls"}}}""");

        var window = Assert.Single(windows);
        Assert.Equal(91, window.Value.Percent);
        Assert.Null(window.Value.WindowResetsAtUtc);
    }

    /// <summary>Renamed fields are what the hint list is for — the reading and its identity both survive.</summary>
    [Theory]
    [InlineData("resets_at")]
    [InlineData("reset_time")]
    [InlineData("expires_at")]
    [InlineData("renews_at")]
    [InlineData("valid_until")]
    public void Extract_LimitWindows_RecognisesResetFieldsByHint(string resetFieldName)
    {
        var payload = """{"rate_limits":{"five_hour":{"used_percentage":91,"RESET_FIELD":1786493400}}}"""
            .Replace("RESET_FIELD", resetFieldName);

        var windows = LimitData_Parser.Extract_LimitWindows(payload);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786493400).UtcDateTime, Assert.Single(windows).Value.WindowResetsAtUtc);
    }

    /// <summary>The pre-existing percent-only contract is unchanged — everything reading it is untouched.</summary>
    [Fact]
    public void Extract_LimitPercents_StillReportsExactlyWhatItAlwaysDid()
    {
        var percents = LimitData_Parser.Extract_LimitPercents(REAL_PAYLOAD);

        Assert.Equal(2, percents.Count);
        Assert.Equal(40, percents["rate_limits.five_hour.used_percentage"]);
        Assert.Equal(89, percents["rate_limits.seven_day.used_percentage"]);
    }

    /// <summary>
    /// The phantom that pinned the alert latch. Four sessions in the current weekly window reported
    /// `"used_percentage": 1` — one percent — and the fraction rule turned each into 100%, which
    /// latched the alert state at its ceiling and silenced every real crossing beneath it. 1 is not
    /// a fraction in this data.
    /// </summary>
    [Fact]
    public void OnePercent_IsOnePercent_NotAFractionMeaningFullyUsed()
    {
        var percents = LimitData_Parser.Extract_LimitPercents(
            """{"rate_limits":{"seven_day":{"used_percentage":1,"resets_at":1786953600}}}""");

        Assert.Equal(1, percents["rate_limits.seven_day.used_percentage"]);
    }

    /// <summary>A genuine fraction below 1 still normalises — the documented behaviour is untouched.</summary>
    [Theory]
    [InlineData(0.42, 42)]
    [InlineData(0.9, 90)]
    public void AFractionStrictlyBelowOne_StillNormalisesToPercent(double raw, double expected)
    {
        var payload = """{"rate_limits":{"seven_day":{"used_percentage":RAW}}}"""
            .Replace("RAW", raw.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var percents = LimitData_Parser.Extract_LimitPercents(payload);

        Assert.Equal(expected, percents["rate_limits.seven_day.used_percentage"]);
    }
}
