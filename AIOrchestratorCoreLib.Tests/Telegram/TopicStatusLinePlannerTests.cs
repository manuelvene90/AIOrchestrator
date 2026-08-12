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
        Assert.Equal(TopicStatusActions.None, Plan(lastFailedAttemptAt: NOW.AddSeconds(-5)).Action);
        Assert.Equal(TopicStatusActions.Post, Plan(lastFailedAttemptAt: NOW.AddSeconds(-BACKOFF)).Action);
    }

    [Fact]
    public void NoRecordedFailureIsAlwaysDue()
    {
        Assert.True(TopicStatusLine_Planner.Is_AttemptDue(null, NOW, BACKOFF));
        Assert.False(TopicStatusLine_Planner.Is_AttemptDue(NOW.AddSeconds(-1), NOW, BACKOFF));
    }

    /// <summary>
    /// THE CLOCK MISMATCH, written down as a test because no assertion can catch it structurally —
    /// the tests build both sides from one constant, so two clocks can never disagree in here.
    ///
    /// This is what it LOOKED like in production: the failure stamp came from UtcNow while `now` came
    /// from DateTime.Now, so on a UTC+2 machine one second after a failure computed as two hours
    /// elapsed and a 30-second backoff cleared instantly — at every value it could be given. The 429
    /// protection was absent while every test passed.
    ///
    /// The fix is that the caller now has only ONE clock to give. This case pins the arithmetic that
    /// made it invisible, so the next reader recognises the shape rather than rediscovering it.
    /// </summary>
    [Fact]
    public void AStampFromTheWrongClockReadsAsImmediatelyDue()
    {
        var twoHoursBehind = NOW.AddHours(-2);

        Assert.True(TopicStatusLine_Planner.Is_AttemptDue(twoHoursBehind, NOW, BACKOFF));
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
        Assert.Equal("", Plan(members: []).Text);
        Assert.Equal("orch", Plan(members: [], existingMessageId: 4242, lastWrittenText: "old row").Text);
    }

    /// <summary>
    /// GATE C — the trusted reading of an agent-written stamp, which never left the engine and so
    /// could be reverted to a raw parse with 630 tests staying green. A FUTURE-dated entry must not
    /// win `last` and hold it until real time catches up.
    /// </summary>
    [Fact]
    public void AFutureDatedEntryDoesNotWinTheLastLine()
    {
        var members = new[]
        {
            Member("imp-1", "the real latest", "2026-08-12 14:50"),
            Member("imp-2", "stamped in the future", "2026-08-13 23:00"),
        };

        Assert.Equal("the real latest", TopicStatusLine_Planner.Pick_LastSubject_OrNull(members, NOW));
    }

    /// <summary>An unparseable stamp loses rather than winning by accident.</summary>
    [Fact]
    public void AnUnparseableStampDoesNotWinTheLastLine()
    {
        var members = new[]
        {
            Member("imp-1", "the real latest", "2026-08-12 14:50"),
            Member("imp-2", "no date at all", "not a date"),
        };

        Assert.Equal("the real latest", TopicStatusLine_Planner.Pick_LastSubject_OrNull(members, NOW));
    }

    /// <summary>And the ordinary case still picks the genuinely most recent.</summary>
    [Fact]
    public void TheLatestTrustworthyStampWins()
    {
        var members = new[]
        {
            Member("imp-1", "older", "2026-08-12 10:00"),
            Member("imp-2", "newer", "2026-08-12 14:55"),
        };

        Assert.Equal("newer", TopicStatusLine_Planner.Pick_LastSubject_OrNull(members, NOW));
    }

    /// <summary>
    /// PINS THE CALL, not the callee. Replacing Pick_LastSubject_OrNull(...) with a plain null at the
    /// planner's own call site left 634 green, because the only two assertions on Plan(...).Text used
    /// an EMPTY roster — where the picker returns null anyway — and every other Plan assertion looks
    /// at .Action.
    ///
    /// Third instance of one shape: the derived bool, then the clock, now the picker call. Each time
    /// a decision moved somewhere the tests could reach and the WIRING that activates it stayed
    /// behind, unobserved. The rule is to pin the call as well as the thing it calls.
    ///
    /// In production that mutation removes the `last` row from every topic message, and where the
    /// subject is the only substance it reduces the message to the bare title or to nothing.
    /// </summary>
    [Fact]
    public void ThePlanActuallyCallsThePickerAndPutsTheWinnerInTheText()
    {
        var plan = Plan(members:
        [
            Member("imp-1", "older thing", "2026-08-12 10:00"),
            Member("imp-2", "the winning subject", "2026-08-12 14:55"),
        ]);

        // Asserted on the LAST LINE, not on the whole text: every member's brief also appears as its
        // own row, so "contains the subject" is satisfied by the row and says nothing about the
        // picker. The `last` line is the only place the picker's answer shows up.
        var lastLine = plan.Text.Split('\n')[^1];

        Assert.StartsWith("last", lastLine);
        Assert.Contains("the winning subject", lastLine);
        Assert.DoesNotContain("older thing", lastLine);
    }

    static TopicStatusLine_Planner.TopicStatusPlan Plan(
        IReadOnlyList<ITopicStatusMember>? members = null,
        IPlanProgress? progress = null,
        long? existingMessageId = null,
        string? lastWrittenText = null,
        TelegramDeliveryModes mode = TelegramDeliveryModes.Normal,
        DateTime? lastFailedAttemptAt = null)
    {
        return TopicStatusLine_Planner.Plan(
            "orch",
            progress,
            members ?? [Member("imp-1", "fix the parser", "2026-08-12 14:50")],
            NOW,
            existingMessageId,
            lastWrittenText,
            mode,
            lastFailedAttemptAt,
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
