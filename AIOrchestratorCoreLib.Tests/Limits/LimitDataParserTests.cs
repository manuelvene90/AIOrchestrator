using AIOrchestratorCoreLib.Limits;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Limits;

public class LimitDataParserTests
{
    [Fact]
    public void Build_ShortLabel_FiveHourKey_BecomesCompact()
    {
        Assert.Equal("5h", LimitData_Parser.Build_ShortLabel("rate_limits.five_hour.used_percentage"));
    }

    [Fact]
    public void Build_ShortLabel_WeeklyKey_BecomesCompact()
    {
        Assert.Equal("weekly", LimitData_Parser.Build_ShortLabel("rate_limits.seven_day.utilization"));
    }

    [Fact]
    public void Build_ShortLabel_UnknownSegments_PassThroughReadably()
    {
        Assert.Equal("opus weekly", LimitData_Parser.Build_ShortLabel("usage.opus.seven_day.percent"));
    }

    [Fact]
    public void Build_ShortLabel_AllSegmentsGeneric_FallsBackToFullKey()
    {
        Assert.Equal("usage.percent", LimitData_Parser.Build_ShortLabel("usage.percent"));
    }

    [Fact]
    public void Extract_LimitPercents_FindsPercentFieldsUnderLimitPaths()
    {
        var json = """{"rate_limits":{"five_hour":{"used_percentage":91.0},"seven_day":{"used_percentage":0.42}}}""";

        var percents = LimitData_Parser.Extract_LimitPercents(json);

        Assert.Equal(91.0, percents["rate_limits.five_hour.used_percentage"]);
        Assert.Equal(42.0, percents["rate_limits.seven_day.used_percentage"]);
    }
}
