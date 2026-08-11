using AIOrchestratorCoreLib.Limits;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Limits;

/// <summary>
/// The on-disk latch file, and specifically its MIGRATION. The old file recorded a threshold with
/// no record of which window earned it; read back carelessly, that number suppresses every alert
/// beneath it forever. These pin the reading of the real legacy content, not a paraphrase of it.
/// </summary>
public class LimitAlertStateStoreTests
{
    /// <summary>The exact bytes sitting in ~/.claude/supervision/.limit-alerts.json on 2026-08-11.</summary>
    const string LEGACY_FILE_CONTENT =
        """{"rate_limits.five_hour.used_percentage":100,"rate_limits.seven_day.used_percentage":100}""";

    /// <summary>
    /// THE failure case this unit exists to rule out: the legacy 100 being read as a latch that
    /// still applies. It must come back with NO window, which is what makes it re-arm.
    /// </summary>
    [Fact]
    public void Parse_TheRealLegacyFile_YieldsLatchesWithNoWindowIdentity()
    {
        var state = LimitAlertState_Store.Parse(LEGACY_FILE_CONTENT);

        Assert.Equal(2, state.Count);

        var weekly = state["rate_limits.seven_day.used_percentage"];
        Assert.Equal(100, weekly.Threshold);
        Assert.Null(weekly.WindowResetsAtUtc);
    }

    /// <summary>End to end on the real content: the owner's weekly is un-latched the moment this lands.</summary>
    [Fact]
    public void TheRealLegacyFile_ReArmsTheWeeklyWindow_InsteadOfInheritingThePin()
    {
        var state = LimitAlertState_Store.Parse(LEGACY_FILE_CONTENT);
        var stored = state["rate_limits.seven_day.used_percentage"];

        var lastAlerted = LimitAlert_Tracker.Resolve_LastAlertedThreshold_ForCurrentWindow(
            stored.Threshold,
            stored.WindowResetsAtUtc,
            currentWindowIdentity: DateTimeOffset.FromUnixTimeSeconds(1786953600).UtcDateTime,
            currentPercent: 89);

        Assert.Equal(0, lastAlerted);
    }

    [Fact]
    public void Parse_TheCurrentShape_ReadsBothTheThresholdAndTheWindow()
    {
        var state = LimitAlertState_Store.Parse("""{"rate_limits.seven_day.used_percentage":{"threshold":95,"window":1786953600}}""");

        var weekly = state["rate_limits.seven_day.used_percentage"];
        Assert.Equal(95, weekly.Threshold);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786953600).UtcDateTime, weekly.WindowResetsAtUtc);
    }

    [Fact]
    public void ToJson_ThenParse_RoundTripsBothWithAndWithoutAWindow()
    {
        Dictionary<string, (double Threshold, DateTime? WindowResetsAtUtc)> written = new()
        {
            ["rate_limits.five_hour.used_percentage"] = (97, DateTimeOffset.FromUnixTimeSeconds(1786493400).UtcDateTime),
            ["some.undated.percent"] = (90, null),
        };

        var readBack = LimitAlertState_Store.Parse(LimitAlertState_Store.To_Json(written));

        Assert.Equal(97, readBack["rate_limits.five_hour.used_percentage"].Threshold);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786493400).UtcDateTime, readBack["rate_limits.five_hour.used_percentage"].WindowResetsAtUtc);
        Assert.Equal(90, readBack["some.undated.percent"].Threshold);
        Assert.Null(readBack["some.undated.percent"].WindowResetsAtUtc);
    }

    /// <summary>An unreadable file re-alerts once rather than throwing out of the whole check.</summary>
    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("")]
    public void Parse_UnusableContent_YieldsNoLatches_NeverThrows(string content)
    {
        Assert.Empty(LimitAlertState_Store.Parse(content));
    }
}
