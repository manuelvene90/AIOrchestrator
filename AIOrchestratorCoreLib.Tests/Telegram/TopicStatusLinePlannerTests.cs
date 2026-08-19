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
    const long STATUS_ID = 4242;

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

    // ── THE REPOST, owner directive 2026-08-13 ────────────────────────────────────────────────────
    //
    // Posted once and edited forever meant the line SCROLLED AWAY: entering the topic showed whatever
    // was last said, and the current state was somewhere above. The owner wants the status to be the
    // thing they see without typing a command, so when it is no longer the last message AND the topic
    // has been quiet for two minutes, it is rewritten at the bottom. While it IS the last message it
    // keeps being edited exactly as before, because an edit notifies nobody.

    /// <summary>
    /// The rule as the owner stated it: buried by later traffic, and the topic has gone quiet.
    /// </summary>
    [Fact]
    public void AStatusLineBuriedByLaterTrafficIsRepostedOnceTheTopicGoesQuiet()
    {
        var plan = Plan(
            existingMessageId: STATUS_ID,
            lastWrittenText: "an older line",
            newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)));

        Assert.Equal(TopicStatusActions.Repost, plan.Action);
    }

    /// <summary>
    /// AND THE OTHER SIDE, which is the one that keeps this feature from becoming a waterfall: while
    /// the status line IS the last message it is EDITED, silently, exactly as before. A repost
    /// notifies — Telegram cannot move a message — so one that fires while the line is already at the
    /// bottom would ping the owner for a duration ticking from 4 to 5 minutes.
    /// </summary>
    [Fact]
    public void AStatusLineThatIsStillTheLastMessageIsEditedInPlace()
    {
        var plan = Plan(
            existingMessageId: STATUS_ID,
            lastWrittenText: "an older line",
            newestTopicMessage: Newest(STATUS_ID - 20, NOW.AddHours(-1)));

        Assert.Equal(TopicStatusActions.Edit, plan.Action);
    }

    /// <summary>
    /// THE QUIET WINDOW, asserted THROUGH Plan and at its boundary — so it pins the wiring of the
    /// constant as well as the arithmetic. Item: the derived bool, the clock and the picker call were
    /// each moved somewhere reachable while the wiring that activates them stayed behind, unobserved.
    ///
    /// One second short holds; the window itself fires. Without the window a repost would land on the
    /// owner's phone in the middle of their own conversation, which is the opposite of the ask.
    /// </summary>
    [Fact]
    public void TheRepostWaitsForTheTopicToGoQuiet()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddSeconds(-(TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS - 1)))).Action);

        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddSeconds(-TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS))).Action);
    }

    /// <summary>
    /// AND THE WINDOW IS TWO MINUTES — the one number the owner actually specified, asserted with
    /// LITERAL seconds because the case above cannot see it.
    ///
    /// F2, rev-1: every other test passes `REPOST_AFTER_QUIET_SECONDS` symbolically, so they pin the
    /// ARITHMETIC around the constant and never its VALUE. Set the constant to 0 and all of them stay
    /// green — `AddSeconds(-(0 - 1))` is a stamp one second in the FUTURE, which holds, and
    /// `AddSeconds(0)` is due. A repost would then fire the instant any message buried the line: a
    /// notification in the middle of the owner's own sentence, which is the waterfall item 14 exists
    /// to prevent, arriving with a green suite.
    ///
    /// 119 and 120 pin it exactly rather than approximately: 30 alone would allow anything above 30.
    /// </summary>
    [Fact]
    public void TheQuietWindowIsTwoMinutes()
    {
        Assert.Equal(TopicStatusActions.Edit, Plan_AfterQuietSeconds(30).Action);
        Assert.Equal(TopicStatusActions.Edit, Plan_AfterQuietSeconds(119).Action);
        Assert.Equal(TopicStatusActions.Repost, Plan_AfterQuietSeconds(120).Action);
    }

    /// <summary>
    /// THE ONE THAT DECIDES WHETHER THE FEATURE WORKS AT ALL. A buried status line is USUALLY
    /// unchanged text — a quiet orchestration says the same thing minute after minute — and the
    /// identical-text rule answers None to exactly that. If the repost sat behind that rule it would
    /// fire only for orchestrations that happened to change something in the same tick, which is the
    /// quiet topic it was asked for, never reached.
    ///
    /// Both sides, from the SAME text: unchanged and not buried is still silence.
    /// </summary>
    [Fact]
    public void TheRepostFiresEvenWhenTheTextHasNotChanged()
    {
        var current = Plan(existingMessageId: STATUS_ID).Text;

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: Newest(STATUS_ID - 20, NOW.AddHours(-1))).Action);

        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2))).Action);
    }

    /// <summary>
    /// With no status message up there is nothing to move: the first line is still a POST, not a
    /// repost, and it must not delete an id it does not have.
    /// </summary>
    [Fact]
    public void WithNoStatusMessageUpThereIsNothingToRepost()
    {
        Assert.Equal(
            TopicStatusActions.Post,
            Plan(newestTopicMessage: Newest(9999, NOW.AddMinutes(-2))).Action);
    }

    /// <summary>
    /// AN UNKNOWN TOPIC IS NOT A BURIED ONE. The newest id is remembered in memory, so after an app
    /// restart it is absent for every topic until traffic repopulates it — and "I do not know" must
    /// not be answered with a notification. It edits, as it always did, and the first message through
    /// the mirror restores the knowledge.
    /// </summary>
    [Fact]
    public void ATopicWithNoKnownTrafficIsNotReposted()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line", newestTopicMessage: null).Action);
    }

    /// <summary>
    /// THE DELIVERY GATE APPLIES, because a repost NOTIFIES and a topic the owner silenced must not
    /// be the thing that pushes to their phone — the same rule the POST already obeys.
    ///
    /// It falls back to the EDIT rather than to silence: the edit notifies nobody, so Deferred's
    /// contract that nothing is lost survives, and the line stays current instead of freezing at
    /// pre-DND content for the whole period. The move to the bottom is what waits for the unmute.
    /// </summary>
    [Theory]
    [InlineData(TelegramDeliveryModes.Silenced)]
    [InlineData(TelegramDeliveryModes.Deferred)]
    public void ASilencedTopicIsNotRepostedIntoAndFallsBackToTheEdit(TelegramDeliveryModes mode)
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(mode: mode, existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2))).Action);
    }

    /// <summary>
    /// And the fallback is to what the decider actually said, not to an edit regardless: silenced,
    /// buried, and nothing new to say is NOTHING. Falling back to a blanket Edit would write the same
    /// text every tick for the whole DND period — the wasted-call spin the identical-text rule exists
    /// to stop, reintroduced through the back door of a feature that is supposed to be quiet.
    /// </summary>
    [Fact]
    public void ASilencedTopicWithNothingNewToSayStaysSilent()
    {
        var current = Plan(existingMessageId: STATUS_ID).Text;

        Assert.Equal(
            TopicStatusActions.None,
            Plan(mode: TelegramDeliveryModes.Silenced, existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2))).Action);
    }

    /// <summary>
    /// THE BACKOFF APPLIES TOO. A repost is a delete plus a post — two calls where an edit was one —
    /// so a 429 answered at the tick rate costs double what it did before.
    /// </summary>
    [Fact]
    public void ARecentFailureHoldsTheRepostAsWell()
    {
        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)), lastFailedAttemptAt: NOW.AddSeconds(-5)).Action);

        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)), lastFailedAttemptAt: NOW.AddSeconds(-BACKOFF)).Action);
    }

    /// <summary>
    /// The predicate on its own, at the three edges Plan cannot show as clearly. EQUAL ids are the
    /// subtle one: the newest message the app knows of IS the status line itself, which means nothing
    /// came after it.
    /// </summary>
    [Fact]
    public void TheRepostPredicateAtItsEdges()
    {
        var quiet = NOW.AddMinutes(-5);

        Assert.False(TopicStatusLine_Planner.Is_RepostDue(null, Newest(9999, quiet), NOW, TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS));
        Assert.False(TopicStatusLine_Planner.Is_RepostDue(STATUS_ID, null, NOW, TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS));
        Assert.False(TopicStatusLine_Planner.Is_RepostDue(STATUS_ID, Newest(STATUS_ID, quiet), NOW, TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS));
        Assert.True(TopicStatusLine_Planner.Is_RepostDue(STATUS_ID, Newest(STATUS_ID + 1, quiet), NOW, TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS));
    }

    /// <summary>
    /// A message stamped in the FUTURE is not a quiet topic. Both stamps are read off the same local
    /// clock, so this can only come from a clock step — and it must hold the repost rather than
    /// treat a negative elapsed as "long enough".
    /// </summary>
    [Fact]
    public void AMessageStampedInTheFutureDoesNotCountAsQuiet()
    {
        Assert.False(TopicStatusLine_Planner.Is_RepostDue(
            STATUS_ID, Newest(STATUS_ID + 1, NOW.AddMinutes(5)), NOW, TopicStatusLine_Planner.REPOST_AFTER_QUIET_SECONDS));
    }

    /// <summary>
    /// A REPOST STILL HAS TO HAVE SOMETHING TO SEND. The repost overrides the decider, and the
    /// decider is where emptiness is refused — so overriding it without re-checking would hand the
    /// engine a delete followed by a sendMessage with an empty body, which Telegram rejects outright.
    /// The topic would lose the status line it had and get a 400 in exchange.
    ///
    /// Reached through a topic whose display name is blank with nothing else to report: the builder's
    /// bare-title fallback is then a bare NOTHING.
    /// </summary>
    [Fact]
    public void ARepostIsNotAttemptedWithNothingToSend()
    {
        var plan = Plan(
            title: "",
            members: [],
            existingMessageId: STATUS_ID,
            newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)));

        Assert.Equal("", plan.Text);
        Assert.Equal(TopicStatusActions.None, plan.Action);
    }

    /// <summary>
    /// THE LATCH, rev-1 F1. A delete that is REFUSED rather than failed loops forever and starves the
    /// edit with it: `Is_MessageGone` matches none of the refusal wordings, so the id is never
    /// cleared, the delete throws before the send every time, and because the repost overrides the
    /// decider unconditionally the Edit never runs either.
    ///
    /// That is a REGRESSION, not a missing improvement, and this is the sentence that decides the
    /// design: before this branch a buried line at least stayed CURRENT. Un-latched it can now be
    /// buried AND stale, which is worse than the behaviour it replaced.
    ///
    /// The fix is NOT to call the message gone — rev-1 was right that "can't be deleted" is unsound
    /// for the identical reason "can't be edited" is excluded: the message still EXISTS, so clearing
    /// the id posts a second line beside an undeletable one, which is the two-lines-in-one-topic
    /// defect through a third door. Instead the topic stops trying to MOVE its line and keeps
    /// updating it in place — degrading to master's behaviour rather than to nothing.
    /// </summary>
    [Fact]
    public void ATopicWhereTheRepostIsImpossibleKeepsEditingInPlace()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)), repostIsImpossible: true).Action);
    }

    /// <summary>
    /// And it falls back to what the DECIDER said, not to a blanket edit — the same rule the silenced
    /// topic follows. Nothing new to say is still silence, or a latched topic would rewrite identical
    /// text every tick for the rest of the app's life, which is a worse loop than the one being fixed.
    /// </summary>
    [Fact]
    public void ATopicWhereTheRepostIsImpossibleWithNothingNewToSayStaysSilent()
    {
        var current = Plan(existingMessageId: STATUS_ID).Text;

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)), repostIsImpossible: true).Action);
    }

    /// <summary>
    /// The latch is PER TOPIC and nothing else changes: an unlatched topic in the same state still
    /// reposts. Asserted beside the two above so neither can pass because reposting broke generally.
    /// </summary>
    [Fact]
    public void TheLatchStopsOnlyTheTopicItWasSetFor()
    {
        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddMinutes(-2)), repostIsImpossible: false).Action);
    }

    /// <summary>A buried line in a topic that has been quiet for exactly this many seconds.</summary>
    static TopicStatusLine_Planner.TopicStatusPlan Plan_AfterQuietSeconds(int quietSeconds)
    {
        return Plan(
            existingMessageId: STATUS_ID,
            lastWrittenText: "an older line",
            newestTopicMessage: Newest(STATUS_ID + 20, NOW.AddSeconds(-quietSeconds)));
    }

    static TopicStatusLine_Planner.TopicNewestMessage Newest(long messageId, DateTime arrivedAt)
    {
        return new TopicStatusLine_Planner.TopicNewestMessage(messageId, arrivedAt);
    }

    static TopicStatusLine_Planner.TopicStatusPlan Plan(
        IReadOnlyList<ITopicStatusMember>? members = null,
        IPlanProgress? progress = null,
        long? existingMessageId = null,
        string? lastWrittenText = null,
        TelegramDeliveryModes mode = TelegramDeliveryModes.Normal,
        DateTime? lastFailedAttemptAt = null,
        TopicStatusLine_Planner.TopicNewestMessage? newestTopicMessage = null,
        string title = "orch",
        bool repostIsImpossible = false)
    {
        return TopicStatusLine_Planner.Plan(
            title,
            progress,
            members ?? [Member("imp-1", "fix the parser", "2026-08-12 14:50")],
            NOW,
            existingMessageId,
            lastWrittenText,
            mode,
            lastFailedAttemptAt,
            BACKOFF,
            newestTopicMessage,
            repostIsImpossible);
    }

    /// <summary>
    /// THE OWNER'S OWN WORDS ARE NOT THE ORCHESTRATION'S LAST WORD (their call, 2026-08-19).
    ///
    /// A solo's "member channel" IS the owner channel, so this scan reaches the owner's inbound
    /// messages — and the bridge stamps every one of them with the subject "via Telegram". The owner
    /// read that back on their own topic as what the session last said.
    /// </summary>
    [Fact]
    public void TheOwnersOwnEntryIsNeverTheLastLine()
    {
        var solo = TopicStatusMember_Factory.Create(
            "solo-1",
            [
                Entry(1, ChannelAuthors.Solo, "2026-08-12 14:50", "fix landed — 1316 green"),
                Entry(2, ChannelAuthors.Owner, "2026-08-12 14:55", "via Telegram"),
            ],
            isClosed: false);

        Assert.Equal(
            "fix landed — 1316 green",
            TopicStatusLine_Planner.Pick_LastSubject_OrNull([solo], NOW));
    }

    /// <summary>The app was already excluded, and still is — this pins that the new filter kept it out.</summary>
    [Fact]
    public void TheAppsOwnEntryIsNeverTheLastLine()
    {
        var solo = TopicStatusMember_Factory.Create(
            "solo-1",
            [
                Entry(1, ChannelAuthors.Solo, "2026-08-12 14:50", "fix landed"),
                Entry(2, ChannelAuthors.App, "2026-08-12 14:55", "STATUS"),
            ],
            isClosed: false);

        Assert.Equal("fix landed", TopicStatusLine_Planner.Pick_LastSubject_OrNull([solo], NOW));
    }

    /// <summary>
    /// A SUPERVISOR IS NOT A MEMBER BUT IT IS A SESSION, and on a spoke channel its brief is very
    /// often the newest thing said. Filtering to Is_Member instead of Is_Session would have emptied
    /// this field for every orchestration between a brief and the implementer's first report.
    /// </summary>
    [Fact]
    public void ASupervisorsEntryStillCounts()
    {
        var imp = TopicStatusMember_Factory.Create(
            "imp-1",
            [
                Entry(1, ChannelAuthors.Implementer, "2026-08-12 14:50", "TASK 1 landed"),
                Entry(2, ChannelAuthors.Supervisor, "2026-08-12 14:55", "brief — TASK 2"),
            ],
            isClosed: false);

        Assert.Equal("brief — TASK 2", TopicStatusLine_Planner.Pick_LastSubject_OrNull([imp], NOW));
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string stamp, string subject)
    {
        return ChannelEntry_Factory.Create(
            index, author, stamp, subject, "body", $"## [{index}] FROM x — {stamp} — {subject}");
    }

    static ITopicStatusMember Member(string memberId, string briefSubject, string stamp)
    {
        return TopicStatusMember_Factory.Create(
            memberId,
            [ChannelEntry_Factory.Create(1, ChannelAuthors.Supervisor, stamp, briefSubject, "body", $"## [1] FROM supervisor — {stamp} — {briefSubject}\nbody")],
            isClosed: false);
    }
}
