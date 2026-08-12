using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The three gates that were unreachable, now where the suite can ask about them.
///
/// A reviewer deleted the trusted-stamp reader, the per-topic delivery gate and the backoff gate ALL
/// AT ONCE and the suite stayed green — then proved the build was genuine by injecting a syntax error
/// into the same engine file and watching it fail. The green was necessary, not observed:
/// BridgeEngineModel is internal sealed, there is no InternalsVisibleTo, and the test project
/// references CoreLib alone.
///
/// InternalsVisibleTo was the cheaper seam and was refused: it would make the engine testable without
/// making it tested. Moving the two error predicates out is what made them pinned; this applies the
/// same move to the gates and to the wiring that activates them.
/// </summary>
public class TopicStatusLinePlannerTests
{
    static readonly DateTime NOW = new(2026, 8, 12, 15, 0, 0);
    const int BACKOFF = 30;

    /// <summary>The ordinary case: something to say, nothing posted, delivery normal, no failures.</summary>
    [Fact]
    public void AFirstLineIsPosted()
    {
        Assert.Equal(TopicStatusActions.Post, Plan().Action);
    }

    /// <summary>
    /// THE DELIVERY GATE, on the POST. A topic the owner silenced must not be the thing that pushes
    /// to their phone.
    /// </summary>
    [Theory]
    [InlineData(TelegramDeliveryModes.Silenced)]
    [InlineData(TelegramDeliveryModes.Deferred)]
    public void ASilencedTopicIsNotPostedInto(TelegramDeliveryModes mode)
    {
        Assert.Equal(TopicStatusActions.None, Plan(mode: mode).Action);
    }

    /// <summary>
    /// And NOT on the edit. An edit notifies nobody, so gating it buys nothing and costs a line
    /// frozen at pre-DND content for the whole period — Deferred's contract is that nothing is lost.
    /// Asserted separately from the POST case so neither can pass for the other's reason.
    /// </summary>
    [Theory]
    [InlineData(TelegramDeliveryModes.Silenced)]
    [InlineData(TelegramDeliveryModes.Deferred)]
    public void ASilencedTopicIsStillEdited(TelegramDeliveryModes mode)
    {
        Assert.Equal(TopicStatusActions.Edit, Plan(mode: mode, existingMessageId: 4242, lastWrittenText: "something older").Action);
    }

    /// <summary>
    /// THE BACKOFF. A 429 answered at the tick rate inverts the cadence from once a minute to thirty
    /// times a minute per topic and sustains the throttling that caused it.
    /// </summary>
    [Fact]
    public void ARecentFailureHoldsTheNextAttempt()
    {
        Assert.Equal(TopicStatusActions.None, Plan(lastFailedAttemptUtc: NOW.AddSeconds(-5)).Action);
        Assert.Equal(TopicStatusActions.Post, Plan(lastFailedAttemptUtc: NOW.AddSeconds(-BACKOFF)).Action);
    }

    [Fact]
    public void NoRecordedFailureIsAlwaysDue()
    {
        Assert.True(TopicStatusLine_Planner.Is_AttemptDue(null, NOW, BACKOFF));
        Assert.False(TopicStatusLine_Planner.Is_AttemptDue(NOW.AddSeconds(-1), NOW, BACKOFF));
    }

    /// <summary>
    /// THE WIRING, which is what M-G3 caught: the engine could pass `false` where it meant "a message
    /// exists" and nothing reddened, so the whole spin fix rested on an argument no test could see.
    /// The planner takes the ID and derives the flag itself, so there is no boolean to get wrong —
    /// and this asserts the derivation from both sides.
    /// </summary>
    [Fact]
    public void TheMessageIdDecidesWhatNothingToSayMeans()
    {
        // Nothing to report at all: silence with no message, the bare title with one.
        Assert.Equal("", Plan(members: [], progress: null, lastSubject: null).Text);
        Assert.Equal("orch", Plan(members: [], progress: null, lastSubject: null, existingMessageId: 4242, lastWrittenText: "old row").Text);
    }

    static TopicStatusLine_Planner.TopicStatusPlan Plan(
        IReadOnlyList<ITopicStatusMember>? members = null,
        IPlanProgress? progress = null,
        string? lastSubject = "gate cleared",
        long? existingMessageId = null,
        string? lastWrittenText = null,
        TelegramDeliveryModes mode = TelegramDeliveryModes.Normal,
        DateTime? lastFailedAttemptUtc = null)
    {
        return TopicStatusLine_Planner.Plan(
            "orch",
            progress,
            members ?? [Member("imp-1", "fix the parser", "2026-08-12 14:50")],
            lastSubject,
            NOW,
            existingMessageId,
            lastWrittenText,
            mode,
            lastFailedAttemptUtc,
            BACKOFF);
    }

    static ITopicStatusMember Member(string memberId, string briefSubject, string stamp)
    {
        return TopicStatusMember_Factory.Create(
            memberId,
            [ChannelEntry_Factory.Create(1, ChannelAuthors.Supervisor, stamp, briefSubject, "body", $"## [1] FROM supervisor — {stamp} — {briefSubject}\nbody")],
            isClosed: false);
    }
}
