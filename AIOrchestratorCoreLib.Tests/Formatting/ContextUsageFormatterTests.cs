using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Status.SessionContextUsage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Formatting;

/// <summary>
/// One wording for a context reading, on every surface. Item 12 of the project decisions: never a
/// second copy of a formatter — the status line, both digests, /context and the session's own
/// terminal must say the same thing about the same number.
/// </summary>
public class ContextUsageFormatterTests
{
    static readonly DateTime PROBED = new(2026, 8, 21, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ItReadsAsTheFieldTheSurfacesAppend()
    {
        Assert.Equal("ctx 93%", ContextUsage_Formatter.Describe(Reading(93)));
    }

    /// <summary>
    /// IT TRUNCATES, like every other percentage in this repo. 89.7 must not read as 90, because 90
    /// is the number the owner attached a meaning to — a figure that rounds UP to a threshold would
    /// show a member as being at the line it has not reached.
    /// </summary>
    [Fact]
    public void ItTruncatesTowardsTheSaferNumber()
    {
        Assert.Equal("ctx 89%", ContextUsage_Formatter.Describe(Reading(89.7)));
        Assert.Equal("ctx 0%", ContextUsage_Formatter.Describe(Reading(0.9)));
    }

    /// <summary>
    /// No reading means NO FIELD, so a surface drops it rather than printing an empty one. A row
    /// that ends in a dangling separator reads as a value that failed to load, when the truth is
    /// that the session has not reported one.
    /// </summary>
    [Fact]
    public void NoReadingProducesNoField()
    {
        Assert.Null(ContextUsage_Formatter.Describe_OrNull(null));
    }

    [Fact]
    public void TheNullableFormOfTheSameNumberSaysTheSameThing()
    {
        Assert.Equal(ContextUsage_Formatter.Describe(Reading(52)), ContextUsage_Formatter.Describe_OrNull(Reading(52)));
    }

    static ISessionContextUsage Reading(double percent)
    {
        return SessionContextUsage_Factory.Create(percent, PROBED);
    }
}
