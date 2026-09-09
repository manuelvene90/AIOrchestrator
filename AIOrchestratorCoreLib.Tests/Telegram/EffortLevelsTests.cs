using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The five levels `claude --effort` accepts (2.1.266: low, medium, high, xhigh, max). Typed input
/// resolves only on an exact level — "extra high" is not a level, and the buttons exist precisely so
/// that nobody has to remember the spelling of xhigh.
/// </summary>
public class EffortLevelsTests
{
    [Fact]
    public void TheFiveLevels_InAscendingOrder()
    {
        Assert.Equal(["low", "medium", "high", "xhigh", "max"], EffortLevels.ALL);
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData("XHIGH", "xhigh")]
    [InlineData("  max ", "max")]
    public void ALevel_ResolvesRegardlessOfCaseAndPadding(string typed, string expected)
    {
        Assert.Equal(expected, EffortLevels.Resolve_OrNull(typed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("extra high")]
    [InlineData("x-high")]
    [InlineData("hi")]
    [InlineData("highest")]
    public void AnythingElse_IsNull(string? typed)
    {
        Assert.Null(EffortLevels.Resolve_OrNull(typed));
    }
}
