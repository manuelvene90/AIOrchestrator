using AIOrchestratorCoreLib.Bridge.Decisions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The receipt must mean what it says: a ✓ may follow only an outcome where the message actually
/// reached the channel the owner was writing into.
/// </summary>
public class OwnerRouteWordingTests
{
    /// <summary>
    /// Enumerated with <see cref="Enum.GetValues"/> rather than one value at a time, so a new
    /// outcome added to the enum later fails this test instead of silently earning a receipt.
    /// </summary>
    [Fact]
    public void Deserves_Receipt_IsTrueOnlyForRouted()
    {
        foreach (OwnerRouteOutcomes outcome in Enum.GetValues<OwnerRouteOutcomes>())
        {
            var expected = outcome == OwnerRouteOutcomes.Routed;

            Assert.Equal(expected, OwnerRoute_Wording.Deserves_Receipt(outcome));
        }
    }

    [Fact]
    public void Describe_ForOwner_OrNull_IsNullForRouted()
    {
        Assert.Null(OwnerRoute_Wording.Describe_ForOwner_OrNull(OwnerRouteOutcomes.Routed));
    }

    /// <summary>AnsweredDirectly's own path already told the owner what went wrong, so no second
    /// line is needed here.</summary>
    [Fact]
    public void Describe_ForOwner_OrNull_IsNullForAnsweredDirectly()
    {
        Assert.Null(OwnerRoute_Wording.Describe_ForOwner_OrNull(OwnerRouteOutcomes.AnsweredDirectly));
    }

    [Fact]
    public void Describe_ForOwner_OrNull_NamesTheWayOutForClosedOrchestration()
    {
        var line = OwnerRoute_Wording.Describe_ForOwner_OrNull(OwnerRouteOutcomes.ClosedOrchestration);

        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.Contains("general supervisor", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ForOwner_OrNull_NamesTheWayOutForUnknownTopic()
    {
        var line = OwnerRoute_Wording.Describe_ForOwner_OrNull(OwnerRouteOutcomes.UnknownTopic);

        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.Contains("General", line, StringComparison.Ordinal);
    }
}
