using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The owner landed from a flight to a wall of messages, several of them multi-select questions,
/// with no way to tell which were still relevant. Away mode exists to stop that backlog forming —
/// so the two things that matter are that it triggers when they are genuinely gone, and that it
/// does NOT trigger while they are sitting right there.
/// </summary>
public class AwayModePolicyTests
{
    static readonly DateTime T0 = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ThreeUnansweredMessages_OldEnough_TripsAwayMode()
    {
        Assert.True(AwayMode_Policy.Should_EnterAway(3, T0, T0.AddMinutes(AwayMode_Policy.AWAY_AFTER_MINUTES)));
    }

    /// <summary>
    /// The chatty-supervisor case: three questions fired in the same minute means the SUPERVISOR is
    /// noisy, not that the owner has left. Without the clock this would trip while they are reading.
    /// </summary>
    [Fact]
    public void ThreeMessagesInABurst_DoesNotTripAwayMode()
    {
        Assert.False(AwayMode_Policy.Should_EnterAway(3, T0, T0.AddMinutes(1)));
        Assert.False(AwayMode_Policy.Should_EnterAway(9, T0, T0.AddMinutes(14)));
    }

    /// <summary>
    /// The mid-task case: one unanswered message, however old, means nothing. People finish what
    /// they are doing before replying.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FewerThanThreeUnanswered_NeverTripsIt_HoweverLongItHasBeen(int unanswered)
    {
        Assert.False(AwayMode_Policy.Should_EnterAway(unanswered, T0, T0.AddHours(8)));
    }

    [Fact]
    public void MoreThanThree_AndOldEnough_AlsoTrips()
    {
        Assert.True(AwayMode_Policy.Should_EnterAway(12, T0, T0.AddHours(3)));
    }

    /// <summary>
    /// The notice is the whole point of the feature for the owner: it has to tell them not to
    /// scroll back and answer the backlog.
    /// </summary>
    [Fact]
    public void TheOwnerNotice_SaysTheBacklogIsParkedAndTheyNeedNotAnswerIt()
    {
        Assert.Contains("PARKED", AwayMode_Policy.AWAY_ON_NOTICE);
        Assert.Contains("do not scroll back", AwayMode_Policy.AWAY_ON_NOTICE);
        Assert.Contains("30 min", AwayMode_Policy.AWAY_ON_NOTICE);
        Assert.Contains("still relevant", AwayMode_Policy.AWAY_ON_NOTICE);

        Assert.Contains("away mode off", AwayMode_Policy.AWAY_OFF_NOTICE, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parked", AwayMode_Policy.PARKED_SUFFIX, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheUpdateCadence_MatchesThePeriodicStatusClock()
    {
        Assert.Equal(30, AwayMode_Policy.AWAY_UPDATE_MINUTES);
    }
}
