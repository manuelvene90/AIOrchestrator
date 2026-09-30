using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;
using Xunit;
using AIOrchestratorCoreLib.Formatting;

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
    /// THE DELIVERY GATE, on the POST — and SILENCED ONLY since 2026-09-10. 🔕 means the owner is
    /// reading the same content live in a terminal and asked not to have it twice; dropping is the
    /// mode's whole contract, so a first PULSE is not posted into it.
    /// </summary>
    [Fact]
    public void ASilencedTopicIsNotPostedInto()
    {
        Assert.Equal(TopicStatusActions.None, Plan(mode: TelegramDeliveryModes.Silenced).Action);
    }

    /// <summary>
    /// AND DEFERRED IS NOT SILENCED — the other half of the owner's ruling of 2026-09-09: "🌙 holds
    /// only what rings; PULSE and the dashboard keep updating silently". This case asserted None
    /// until 2026-09-10, sharing a Theory with the Silenced one, and the shared assertion was the
    /// mistake: the two modes mean OPPOSITE things about content. Deferred keeps everything and
    /// replays it because the owner is coming back to it; Silenced throws it away.
    ///
    /// The gate could treat them alike while a post NOTIFIED. Every write here is silent now, so
    /// under 🌙 there is nothing left to protect the owner from — and refusing the first post cost
    /// them a topic with NO status surface at all for the length of the mute, which is what their
    /// check-in ritual reads when they come back.
    /// </summary>
    [Fact]
    public void ADeferredTopicIsStillPostedInto_Silently()
    {
        Assert.Equal(TopicStatusActions.Post, Plan(mode: TelegramDeliveryModes.Deferred).Action);
    }

    /// <summary>
    /// And NOT on the edit, under either mode. An edit notifies nobody, so gating it buys nothing and
    /// costs a line frozen at pre-DND content for the whole period — Deferred's contract is that
    /// nothing is lost. Asserted separately from the POST cases so none can pass for another's reason.
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
        // Nothing to report at all: silence with no message, the bare LEAD WORD with one. It
        // used to be the bare topic title; the topic name left the line on 2026-08-24 and the
        // opening field is now the literal `PULSE`, which is what tells this constantly-edited
        // message apart from the half-hourly digest that opens with STATUS.
        Assert.Equal("", Plan(members: []).Text);
        Assert.Equal("PULSE", Plan(members: [], existingMessageId: 4242, lastWrittenText: "old row").Text);
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

        Assert.Equal("the real latest", TopicStatusLine_Planner.Pick_LastEvent_OrNull(members, NOW)?.Subject);
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

        Assert.Equal("the real latest", TopicStatusLine_Planner.Pick_LastEvent_OrNull(members, NOW)?.Subject);
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

        Assert.Equal("newer", TopicStatusLine_Planner.Pick_LastEvent_OrNull(members, NOW)?.Subject);
    }

    /// <summary>
    /// PINS THE CALL, not the callee. Replacing Pick_LastEvent_OrNull(...) with a plain null at the
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
    ///
    /// ADAPTED 2026-09-10: `[^1]` stopped being the `last` line once PULSE grew a trailing
    /// `updated HH:MM` heartbeat (Brief C, field 6) — the array's last element is now always that
    /// line when there is anything to say at all. Picking the line by its own `last ` prefix instead
    /// of by position keeps the claim ("the picker's answer lands in the text") true regardless of
    /// which other fields are present.
    /// </summary>
    [Fact]
    public void ThePlanActuallyCallsThePickerAndPutsTheWinnerInTheText()
    {
        var plan = Plan(members:
        [
            Member("imp-1", "older thing", "2026-08-12 10:00"),
            Member("imp-2", "the winning subject", "2026-08-12 14:55"),
        ]);

        // Asserted on the `last` LINE, not on the whole text: every member's brief also appears as
        // its own row, so "contains the subject" is satisfied by the row and says nothing about the
        // picker. The `last` line is the only place the picker's answer shows up.
        var lastFieldLine = plan.Text.Split('\n').Single(line => line.StartsWith("last "));

        Assert.Contains("the winning subject", lastFieldLine);
        Assert.DoesNotContain("older thing", lastFieldLine);
    }

    /// <summary>
    /// FIELD 4'S CLOCK AND ITS SUBJECT COME FROM THE SAME EVENT — the owner's ruling of 2026-09-10,
    /// and a defect nothing could have caught before it.
    ///
    /// <para>
    /// What it was: the subject was the newest session entry across the live member SPOKES, and the
    /// clock was the stamp on the supervisor's last entry in `owner-channel.md` — the same value that
    /// fills "declared HH:MM" one field above. So a topic where the supervisor spoke at 10:00 and an
    /// implementer reported at 14:55 rendered `last · 10:00 · &lt;the implementer's subject&gt;`. Two true
    /// facts, one false sentence, in the field the owner reads to know when something last moved.
    /// </para>
    /// <para>
    /// It survived because the two halves were never asserted TOGETHER: no test in the suite ever
    /// set the clock at all. This asserts the pair — the winner's own stamp is printed, and the
    /// loser's is absent — and the pair is now structural: the builder takes one
    /// &lt;see cref="TopicLastEvent"/&gt;, so there is no second argument left to fill from a second file.
    /// </para>
    /// </summary>
    [Fact]
    public void TheLastFieldsClockIsTheClockOfTheEventItNames()
    {
        var plan = Plan(members:
        [
            Member("imp-1", "older thing", "2026-08-12 10:00"),
            Member("imp-2", "the winning subject", "2026-08-12 14:55"),
        ]);

        var lastFieldLine = plan.Text.Split('\n').Single(line => line.StartsWith("last "));

        Assert.Equal("last · 14:55 · the winning subject", lastFieldLine);
        Assert.DoesNotContain("10:00", lastFieldLine);
    }

    /// <summary>
    /// AND AN UNTRUSTWORTHY STAMP PRINTS NO CLOCK rather than a plausible one. The entry still WINS
    /// the field on the rule that already governed the pick — this fixture has only one member, so
    /// there is nothing to lose to — and its unreadable stamp simply prints nothing, which is
    /// decision 12: a time is shown only when it was read from something an agent actually wrote in
    /// a form that could be read.
    /// </summary>
    [Fact]
    public void AnEventWhoseStampCannotBeReadPrintsNoClock()
    {
        var plan = Plan(members: [Member("imp-1", "the only subject", "not a date")]);

        var lastFieldLine = plan.Text.Split('\n').Single(line => line.StartsWith("last "));

        Assert.Equal("last · the only subject", lastFieldLine);
    }

    /// <summary>
    /// CONTENT PROBE (d) from Brief C's "Done when": the `last` field is the latest SUPERVISOR
    /// subject, never the first `[>]` ledger line — the old STATUS message's defect ("now: FIN-D-293a
    /// step 6 and step 7" repeated identically for hours after the work merged). PULSE's `last` field
    /// never reads the ledger at all: it is Pick_LastEvent_OrNull's answer and nothing else, so this
    /// pins that a ledger with a same-named in-progress line cannot leak into it.
    /// </summary>
    [Fact]
    public void TheLastFieldIsTheLatestSupervisorSubjectNeverTheFirstInProgressLedgerLine()
    {
        var progress = PlanProgress_Factory.Create(
            0, 1, 0, 0, 1, "the first in-progress ledger line",
            ["the first in-progress ledger line"], [], [], null,
            [new PlanLedgerLine(">", "the first in-progress ledger line")]);

        var plan = Plan(
            progress: progress,
            members: [Member("imp-1", "the real latest supervisor subject", "2026-08-12 14:55")]);

        var lastFieldLine = plan.Text.Split('\n').Single(line => line.StartsWith("last "));

        Assert.Contains("the real latest supervisor subject", lastFieldLine);
        Assert.DoesNotContain("first in-progress ledger line", lastFieldLine);
    }

    // ── THE REPOST, owner directive 2026-08-13, RULE REPLACED 2026-09-30 ──────────────────────────
    //
    // Posted once and edited forever meant the line SCROLLED AWAY, so a buried line is rewritten at
    // the bottom (Telegram cannot move a message: delete, then a silent send). While it IS the last
    // message it keeps being edited exactly as before.
    //
    // WHEN IT MOVES is the owner's rule of 2026-09-30, and it replaces both earlier gates — the
    // ten-second quiet window (2026-08-24) and "buried AND changed" (2026-09-09, ruling R27):
    // *"it should be updated often so that is basically always the last message in the conversation.
    // it should not get in the way of me speaking with the session, so it should be updated once the
    // last session's message is at least 1 minute old. (not my last message, because to the session
    // it quite often take a lot of time to reply)"*. Buried by ANYTHING — the owner, the app, the
    // session — and the SESSION's last message at least sixty seconds old (or none seen): it moves.
    // Content no longer matters to the move, only to the edit.
    //
    // Each case below that pinned an older ruling was rewritten to assert this one, under a name that
    // says so, rather than deleted — so the diff of this file shows which claims were reversed.

    /// <summary>
    /// The rule as the owner stated it on 2026-09-30: buried by later traffic, and the session's own
    /// last message a minute old. It was "the topic has gone quiet" until then.
    /// </summary>
    [Fact]
    public void AStatusLineBuriedByLaterTraffic_IsRepostedOnceTheSessionsLastMessageIsAMinuteOld()
    {
        var plan = Plan(
            existingMessageId: STATUS_ID,
            lastWrittenText: "an older line",
            newestTopicMessage: Newest(STATUS_ID + 20),
            sessionSilence: Session_Spoke(NOW.AddMinutes(-1)));

        Assert.Equal(TopicStatusActions.Repost, plan.Action);
    }

    /// <summary>
    /// AND THE OTHER SIDE: while the status line IS the last message it is EDITED, silently, exactly as
    /// before — however long the session has been quiet. Nothing is below it, so there is nothing to
    /// move it past.
    /// </summary>
    [Fact]
    public void AStatusLineThatIsStillTheLastMessageIsEditedInPlace()
    {
        var plan = Plan(
            existingMessageId: STATUS_ID,
            lastWrittenText: "an older line",
            newestTopicMessage: Newest(STATUS_ID - 20),
            sessionSilence: Session_Spoke(NOW.AddHours(-1)));

        Assert.Equal(TopicStatusActions.Edit, plan.Action);
    }

    /// <summary>
    /// THE SESSION'S MINUTE, asserted THROUGH Plan and at its boundary — so it pins the wiring of the
    /// constant as well as the arithmetic. One second short edits in place; the minute itself moves.
    /// </summary>
    [Fact]
    public void TheRepostWaitsForTheSessionsLastMessageToBeAMinuteOld()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan_WhenTheSessionSpoke(TopicStatusLine_Planner.REPOST_AFTER_SESSION_QUIET_SECONDS - 1).Action);

        Assert.Equal(
            TopicStatusActions.Repost,
            Plan_WhenTheSessionSpoke(TopicStatusLine_Planner.REPOST_AFTER_SESSION_QUIET_SECONDS).Action);
    }

    /// <summary>
    /// AND THE WINDOW IS SIXTY SECONDS — the owner's number (2026-09-30: *"at least 1 minute old"*),
    /// asserted with LITERAL seconds because the case above only pins the arithmetic around the
    /// constant: set it to 0 and that case stays green while every burial moves the line in the middle
    /// of the session's reply.
    ///
    /// 59 and 60 pin it EXACTLY. 10 holding is what makes this red against the ten-second window this
    /// replaced, and 30 is the brief's own example of a session that spoke "30 s ago".
    /// </summary>
    [Fact]
    public void TheSessionWindowIsSixtySeconds()
    {
        Assert.Equal(TopicStatusActions.Edit, Plan_WhenTheSessionSpoke(0).Action);
        Assert.Equal(TopicStatusActions.Edit, Plan_WhenTheSessionSpoke(10).Action);
        Assert.Equal(TopicStatusActions.Edit, Plan_WhenTheSessionSpoke(30).Action);
        Assert.Equal(TopicStatusActions.Edit, Plan_WhenTheSessionSpoke(59).Action);
        Assert.Equal(TopicStatusActions.Repost, Plan_WhenTheSessionSpoke(60).Action);
        Assert.Equal(TopicStatusActions.Repost, Plan_WhenTheSessionSpoke(61).Action);
    }

    /// <summary>
    /// A SESSION THAT SPOKE THIRTY SECONDS AGO HOLDS THE MOVE, and with nothing new to say the line is
    /// not touched at all. This case used to assert that a fifteen-second pause was ALREADY enough —
    /// the ten-second window's complaint. The owner's 2026-09-30 complaint is the opposite shape: the
    /// line must not move while the session may still be mid-reply, so that the move never lands
    /// between two of its messages.
    /// </summary>
    [Fact]
    public void ASessionMessageThirtySecondsOld_HoldsTheRepost()
    {
        Assert.Equal(TopicStatusActions.Edit, Plan_WhenTheSessionSpoke(30).Action);

        var current = Plan(existingMessageId: STATUS_ID).Text;

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: Newest(STATUS_ID + 20), sessionSilence: Session_Spoke(NOW.AddSeconds(-30))).Action);
    }

    /// <summary>
    /// THE OWNER'S OWN MESSAGE DOES NOT HOLD THE MOVE — it BURIES the line and the move follows at once
    /// when the session has been quiet. This case asserted the reverse until 2026-09-30 ("traffic that
    /// has just landed still holds the repost"): every message reset the window. The owner named the
    /// reason it must not: *"not my last message, because to the session it quite often take a lot of
    /// time to reply"* — timing the move off the owner's words left PULSE twenty messages up in a topic
    /// where the owner was waiting on a slow session.
    ///
    /// The planner does not see WHO buried the line — only the newest id and the session's own clock —
    /// which is exactly the rule: a message that landed a moment ago buries, and does not hold.
    /// </summary>
    [Fact]
    public void AnOwnerMessageThatJustLanded_DoesNotHoldTheRepost_WhenTheSessionIsQuiet()
    {
        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 1), sessionSilence: Session_Spoke(NOW.AddMinutes(-5))).Action);
    }

    /// <summary>
    /// A SESSION THAT HAS SAID NOTHING SINCE THE APP STARTED COUNTING IS A QUIET ONE — both spellings of
    /// "nothing": no silence record at all, and a record with no message in it.
    /// </summary>
    [Fact]
    public void ASessionThatHasPostedNothing_DoesNotHoldTheRepost()
    {
        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line", newestTopicMessage: Newest(STATUS_ID + 1)).Action);

        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line", newestTopicMessage: Newest(STATUS_ID + 1),
                 sessionSilence: new TopicStatusLine_Planner.TopicSessionSilence(null, NOW)).Action);
    }

    /// <summary>
    /// WHAT BOUNDS THE REPOST: after a repost the app's stored id is the FRESH message, whose Telegram id
    /// is higher than every message the topic has seen — so the very next tick reads the line as
    /// UN-BURIED and plans an edit, however quiet the session is. Without this, "reposts as soon as the
    /// session is quiet" would read as "every tick".
    ///
    /// The engine now RECORDS the status line's own send as topic traffic too (every send is, since
    /// 2026-09-30). That is harmless for exactly one reason — EQUAL is not buried — and the second
    /// assertion pins it: the newest message the app knows of being the line itself buries nothing.
    /// </summary>
    [Fact]
    public void AFreshlyRepostedLineIsNoLongerBuriedAndDoesNotRepostAgain()
    {
        const long BURYING_TRAFFIC_ID = STATUS_ID + 20;
        const long REPOSTED_ID = BURYING_TRAFFIC_ID + 1;

        var sessionLongQuiet = Session_Spoke(NOW.AddHours(-1));

        Assert.False(TopicStatusLine_Planner.Is_RepostDue(REPOSTED_ID, Newest(BURYING_TRAFFIC_ID), sessionLongQuiet));
        Assert.False(TopicStatusLine_Planner.Is_RepostDue(REPOSTED_ID, Newest(REPOSTED_ID), sessionLongQuiet));

        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: REPOSTED_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(REPOSTED_ID), sessionSilence: sessionLongQuiet).Action);
    }

    /// <summary>
    /// THE RULE REVERSED A SECOND TIME, ON THE OWNER'S OWN WORDS (2026-09-30). Until 2026-09-10 burial
    /// alone moved the line; from then until 2026-09-30 this case asserted that an UNCHANGED line stayed
    /// buried (owner, 2026-09-09: re-posted "only when it is buried by later traffic AND its content
    /// changed"), on the argument that a repost carrying no news was traffic. The owner has now read the
    /// cost of that trade — PULSE stranded twenty messages up, its BUTTONS with it, so pressing one meant
    /// scrolling — and wants it "basically always the last message". Every write here is silent, so a
    /// move that says nothing new still rings nobody.
    ///
    /// KEPT, INVERTED, UNDER A NAME THAT SAYS SO: unchanged and buried moves once the session is quiet;
    /// unchanged and NOT buried is still no call at all.
    /// </summary>
    [Fact]
    public void TheRepostFiresEvenWhenTheTextHasNotChanged()
    {
        var current = Plan(existingMessageId: STATUS_ID).Text;
        var sessionQuiet = Session_Spoke(NOW.AddMinutes(-2));

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: Newest(STATUS_ID - 20), sessionSilence: sessionQuiet).Action);

        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: Newest(STATUS_ID + 20), sessionSilence: sessionQuiet).Action);
    }

    /// <summary>
    /// THE BRIEF'S PROBE, rewritten for the 2026-09-30 rule: PULSE buried under three later messages
    /// moves ONCE whether or not its content changed, and the fresh line — whose id is above the traffic
    /// that buried the old one — does not move again on the next tick.
    /// </summary>
    [Fact]
    public void BuriedMovesOnce_ChangedOrNot()
    {
        var buriedUnderThree = Newest(STATUS_ID + 3);
        var sessionQuiet = Session_Spoke(NOW.AddMinutes(-2));

        var current = Plan(existingMessageId: STATUS_ID).Text;

        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: buriedUnderThree, sessionSilence: sessionQuiet).Action);

        var afterAChange = Plan(
            existingMessageId: STATUS_ID,
            lastWrittenText: "what PULSE said before anything moved",
            newestTopicMessage: buriedUnderThree,
            sessionSilence: sessionQuiet);

        Assert.Equal(TopicStatusActions.Repost, afterAChange.Action);
        Assert.False(string.IsNullOrWhiteSpace(afterAChange.Text));

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID + 4, lastWrittenText: afterAChange.Text,
                 newestTopicMessage: Newest(STATUS_ID + 4), sessionSilence: sessionQuiet).Action);
    }

    // ── THE ANSWER THAT BURIES AND CHANGES PULSE (ruling R27's case, re-read under the 2026-09-30 rule) ──
    //
    // R27 (2026-09-24) existed because "changed since the last write" left a line edited in place while
    // buried and then stranded for good. Under the owner's 2026-09-30 rule content no longer gates the
    // move, so the memory R27 kept ("what the owner saw at the bottom") is gone with it; what these cases
    // still pin is the SEQUENCE the owner lives through.

    static readonly IReadOnlyList<ITopicStatusMember> AFTER_THE_ANSWER = [Member("imp-1", "run the integration suite", "2026-08-12 14:50")];

    /// <summary>
    /// THE COMMON CASE, step by step: the session's answer buries PULSE and changes it → edited in place
    /// while the answer is fresh (the move would land in the middle of the session's reply) → once the
    /// answer is a minute old: reposted ONCE → the fresh line is at the bottom: nothing more.
    /// </summary>
    [Fact]
    public void AnAnswerThatBuriesAndChangesPulse_IsEditedInPlace_ThenBroughtBackOnceItIsAMinuteOld()
    {
        var atTheBottom = Plan(existingMessageId: STATUS_ID, commandButtonRows: THREE_VERB_BAR);

        var buriedByTheAnswer = Newest(STATUS_ID + 2);

        var inTheMinute = Plan(
            members: AFTER_THE_ANSWER, existingMessageId: STATUS_ID, commandButtonRows: THREE_VERB_BAR,
            lastWrittenText: atTheBottom.Text, lastWrittenRenderKey: atTheBottom.RenderKey,
            newestTopicMessage: buriedByTheAnswer, sessionSilence: Session_Spoke(NOW.AddSeconds(-2)));

        Assert.Equal(TopicStatusActions.Edit, inTheMinute.Action);

        var aMinuteLater = Plan(
            members: AFTER_THE_ANSWER, existingMessageId: STATUS_ID, commandButtonRows: THREE_VERB_BAR,
            lastWrittenText: inTheMinute.Text, lastWrittenRenderKey: inTheMinute.RenderKey,
            newestTopicMessage: buriedByTheAnswer, sessionSilence: Session_Spoke(NOW.AddSeconds(-TopicStatusLine_Planner.REPOST_AFTER_SESSION_QUIET_SECONDS)));

        Assert.Equal(TopicStatusActions.Repost, aMinuteLater.Action);

        // The fresh line carries an id above the traffic that buried the old one — and, recorded as
        // traffic itself, it IS the newest message.
        var afterTheRepost = Plan(
            members: AFTER_THE_ANSWER, existingMessageId: STATUS_ID + 3, commandButtonRows: THREE_VERB_BAR,
            lastWrittenText: aMinuteLater.Text, lastWrittenRenderKey: aMinuteLater.RenderKey,
            newestTopicMessage: Newest(STATUS_ID + 3), sessionSilence: Session_Spoke(NOW.AddMinutes(-5)));

        Assert.Equal(TopicStatusActions.None, afterTheRepost.Action);
    }

    /// <summary>
    /// AN UNCHANGED BURIED LINE STAYS PUT WHILE THE SESSION IS RECENT — the "None" half of the brief's
    /// "buried, session posted 30 s ago ⇒ Edit/None, not Repost". It asserted the opposite reason until
    /// 2026-09-30 (unchanged ⇒ never moved); the reason is now the session's clock, and the next case
    /// shows the same line moving once that clock has run.
    /// </summary>
    [Fact]
    public void AnUnchangedBuriedLine_StaysPutWhileTheSessionIsRecent()
    {
        var written = Plan(existingMessageId: STATUS_ID, commandButtonRows: THREE_VERB_BAR);

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, commandButtonRows: THREE_VERB_BAR,
                 lastWrittenText: written.Text, lastWrittenRenderKey: written.RenderKey,
                 newestTopicMessage: Newest(STATUS_ID + 20), sessionSilence: Session_Spoke(NOW.AddSeconds(-30))).Action);

        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, commandButtonRows: THREE_VERB_BAR,
                 lastWrittenText: written.Text, lastWrittenRenderKey: written.RenderKey,
                 newestTopicMessage: Newest(STATUS_ID + 20), sessionSilence: Session_Spoke(NOW.AddSeconds(-90))).Action);
    }

    /// <summary>
    /// THE HEARTBEAT STILL EDITS IN PLACE while the session is recent: a buried line whose only difference
    /// is `updated HH:MM` is rewritten where it is. Until 2026-09-30 this asserted that the heartbeat was
    /// never a reason to MOVE; nothing about content is a reason to move now, so the case pins that the
    /// clock alone does not move it early either.
    /// </summary>
    [Fact]
    public void AHeartbeatOnlyDifference_IsEditedInPlaceWhileTheSessionIsRecent()
    {
        var buried = Newest(STATUS_ID + 3);

        var seen = TopicStatusLine_Planner.Plan(
            A_Ledger(), [], NOW, STATUS_ID, null, TelegramDeliveryModes.Normal, null, BACKOFF, null, repostIsImpossible: false);

        var later = TopicStatusLine_Planner.Plan(
            A_Ledger(), [], NOW.AddMinutes(5), STATUS_ID, seen.Text, TelegramDeliveryModes.Normal, null, BACKOFF, buried, repostIsImpossible: false,
            sessionSilence: Session_Spoke(NOW.AddMinutes(5).AddSeconds(-10)));

        Assert.NotEqual(seen.Text, later.Text);
        Assert.Equal(TopicStatusActions.Edit, later.Action);
    }

    // ── RETIRED 2026-09-10, AFTER THE DEPLOY ──────────────────────────────────────────────────────
    //
    // Two tests lived here and both asserted that PULSE's text CHANGES every minute and is edited in
    // place: `TheMinuteRollingOverIsNotSomethingNewToSay` (the text differs, but the line must not
    // MOVE) and `TheMinuteRollingOverStillEditsTheLineInPlace` (and it must still be edited). They
    // were written in stage 8d against the heartbeat's per-minute clock, on the reasoning that "an
    // edit notifies nobody, so silencing it buys nothing".
    //
    // TRUE OF THE OWNER'S PHONE, FALSE OF THE API. In production on 2026-09-10, three minutes after
    // the deploy, that per-minute edit drew 429s with `retry_after` 20, then 22, then 32 seconds —
    // once a minute, per live topic. Telegram throttles edits of ONE message far harder than calls to
    // the group, which is the ceiling the control bucket was sized from. The heartbeat steps to five
    // minutes now, so from one minute to the next the text does not change and there is NO call at
    // all — which those two tests would have forbidden.
    //
    // What replaced them is `OneMinuteLaterPulseSaysTheSameThing_SoNothingIsEdited` and its two
    // neighbours, which pin the stronger property: not "the edit is harmless" but "there is no edit".
    // Since 2026-09-30 they run on a line that is NOT buried: whether a buried line moves is decided by
    // the session's clock alone, and these cases are about the edit.

    /// <summary>One merged line of four, so the surface has substance without a member on it.</summary>
    static IPlanProgress A_Ledger()
    {
        return PlanProgress_Factory.Create(1, 0, 0, 0, 4, null, [], [], [], null, []);
    }

    /// <summary>The newest message the app knows of IS the status line — nothing below it.</summary>
    static readonly TopicStatusLine_Planner.TopicNewestMessage AT_THE_BOTTOM = new(STATUS_ID);

    /// <summary>
    /// The same call as <see cref="Plan"/> but with the clock as an argument — the fixture's `Plan`
    /// hard-codes `NOW`, and the tests below exist precisely to move it. No members and a ledger,
    /// so the heartbeat is the only line that reads the clock.
    /// </summary>
    static TopicStatusLine_Planner.TopicStatusPlan Plan_At(
        DateTime now, string? lastWrittenText, TopicStatusLine_Planner.TopicNewestMessage newestTopicMessage)
    {
        return TopicStatusLine_Planner.Plan(
            A_Ledger(),
            [],
            now,
            STATUS_ID,
            lastWrittenText,
            TelegramDeliveryModes.Normal,
            null,
            BACKOFF,
            newestTopicMessage,
            repostIsImpossible: false);
    }

    /// <summary>
    /// A LIVE MEMBER NO LONGER REWRITES THE LINE FOR THE MINUTE HAND — the owner's ruling of 2026-09-10.
    ///
    /// <para>
    /// A row ends in "for how long", read from a live clock, so a topic with anybody working in it
    /// was rewritten about once a minute. The duration now steps to five minutes, so rounding what the
    /// owner reads is what stops the call.
    /// </para>
    /// <para>
    /// FOUR MINUTES APART, INSIDE ONE STEP: the two renderings must be identical, so there is nothing
    /// to write. Then across the step boundary it is edited once — asserted too, because "it never
    /// writes" would also be satisfied by a surface that had stopped reporting durations at all. This
    /// case asserted a MOVE across the step until 2026-09-30; the line is at the bottom here, because
    /// a move is no longer about content (see the repost cases above).
    /// </para>
    /// </summary>
    [Fact]
    public void AWorkingMemberDoesNotRewriteTheLineUntilItsDurationStepsOver()
    {
        var working = Member("imp-1", "fix the parser", NOW.AddMinutes(-1).ToString("yyyy-MM-dd HH:mm"));

        // The ledger matches Plan_At's, so the ONLY thing that can differ between the two renderings
        // is the member's duration.
        var first = Plan(members: [working], progress: A_Ledger(), existingMessageId: STATUS_ID, newestTopicMessage: AT_THE_BOTTOM);

        // +3 minutes: the member is 4 minutes in, still below the first step, so the row reads the same.
        var insideTheStep = Plan_At(NOW.AddMinutes(3), [working], first.Text, AT_THE_BOTTOM);

        // COMPARED WITHOUT THE HEARTBEAT, because field 6 carries a wall clock and always differs
        // across a clock move — that is stage 8d's rule, and asserting on the raw text here would
        // measure the heartbeat instead of the duration.
        Assert.Equal(
            TopicStatusLine_Builder.Strip_Heartbeat(first.Text),
            TopicStatusLine_Builder.Strip_Heartbeat(insideTheStep.Text));

        // NONE, since 2026-09-10: with the heartbeat stepped to five minutes as well, four minutes apart
        // inside one step means nothing on the surface has changed, and the cheapest correct answer is
        // no call.
        Assert.Equal(TopicStatusActions.None, insideTheStep.Action);

        // +5 minutes: 6 minutes in, over the step, so the row genuinely changed and the line is edited.
        var pastTheStep = Plan_At(NOW.AddMinutes(5), [working], first.Text, AT_THE_BOTTOM);

        Assert.NotEqual(
            TopicStatusLine_Builder.Strip_Heartbeat(first.Text),
            TopicStatusLine_Builder.Strip_Heartbeat(pastTheStep.Text));

        Assert.Equal(TopicStatusActions.Edit, pastTheStep.Action);
    }

    /// <summary>As <see cref="Plan_At(DateTime, string?, TopicStatusLine_Planner.TopicNewestMessage)"/>, with members — the duration cases need one on the line.</summary>
    static TopicStatusLine_Planner.TopicStatusPlan Plan_At(
        DateTime now,
        IReadOnlyList<ITopicStatusMember> members,
        string? lastWrittenText,
        TopicStatusLine_Planner.TopicNewestMessage newestTopicMessage)
    {
        return TopicStatusLine_Planner.Plan(
            A_Ledger(),
            members,
            now,
            STATUS_ID,
            lastWrittenText,
            TelegramDeliveryModes.Normal,
            null,
            BACKOFF,
            newestTopicMessage,
            repostIsImpossible: false);
    }

    /// <summary>
    /// THE PROBE THAT WOULD HAVE CAUGHT THE 429s. PULSE's text must not change from one MINUTE to
    /// the next when nothing has happened — because the app EDITS the message whenever the text
    /// changes, and Telegram throttles edits of one message far harder than calls to the group.
    ///
    /// <para>
    /// PRODUCTION, 2026-09-10 20:57-20:59, three minutes after the deploy: `editMessageText` fired
    /// once a minute per live topic and Telegram answered 429 with `retry_after` 20, then 22, then 32
    /// seconds, per topic. Nothing was lost — the retry lands — but that is a throttle being hit
    /// continuously, and it was hit because field 6 carried a per-minute clock.
    /// </para>
    /// <para>
    /// The clock still moves: across a five-minute step the text changes and the line is edited, and
    /// that half is asserted too, or a heartbeat that had simply stopped would satisfy the first.
    /// </para>
    /// </summary>
    [Fact]
    public void OneMinuteLaterPulseSaysTheSameThing_SoNothingIsEdited()
    {
        var atTheStart = Plan(members: [], progress: A_Ledger(), existingMessageId: STATUS_ID, newestTopicMessage: AT_THE_BOTTOM);

        var aMinuteLater = Plan_At(NOW.AddMinutes(1), atTheStart.Text, AT_THE_BOTTOM);

        // The TEXT is identical, which is what stops the edit: the decider answers None to identical
        // text, and None is no API call at all.
        Assert.Equal(atTheStart.Text, aMinuteLater.Text);
        Assert.Equal(TopicStatusActions.None, aMinuteLater.Action);
    }

    /// <summary>
    /// AND THE HEARTBEAT HAS NOT SIMPLY STOPPED. Across the five-minute step the text changes and the
    /// line is edited in place — which is the field's whole job, telling a quiet orchestration apart
    /// from a dead app. Without this, a heartbeat deleted outright would pass the test above.
    /// </summary>
    [Fact]
    public void FiveMinutesLaterTheHeartbeatHasMoved()
    {
        var atTheStart = Plan(members: [], progress: A_Ledger(), existingMessageId: STATUS_ID, newestTopicMessage: AT_THE_BOTTOM);

        // NOW is 15:00 in this fixture, so +5 crosses a step boundary whatever the minute happens to be.
        var laterStill = Plan_At(NOW.AddMinutes(5), atTheStart.Text, AT_THE_BOTTOM);

        Assert.NotEqual(atTheStart.Text, laterStill.Text);
        Assert.Equal(TopicStatusActions.Edit, laterStill.Action);
    }

    /// <summary>
    /// THE STEP IS THE MEMBER DURATION'S STEP, read from the one constant. Two ticking fields on one
    /// line tuned apart by accident would put the surface back to changing every minute through
    /// whichever of them was left finer.
    /// </summary>
    [Fact]
    public void EveryMinuteInsideOneStepRendersTheSameHeartbeat()
    {
        var step = UnchangedFor_Formatter.STEP_MINUTES;

        // From a moment floored to a step, every minute up to the next boundary must read alike.
        var start = NOW.AddMinutes(-(NOW.Minute % step));
        var atTheStart = Plan_At(start, null, AT_THE_BOTTOM);

        for (var minute = 1; minute < step; minute++)
        {
            Assert.Equal(
                atTheStart.Text,
                Plan_At(start.AddMinutes(minute), null, AT_THE_BOTTOM).Text);
        }

        Assert.NotEqual(atTheStart.Text, Plan_At(start.AddMinutes(step), null, AT_THE_BOTTOM).Text);
    }

    /// <summary>
    /// THE PLANNER DOES NOT GUESS ABOUT TRAFFIC IT WAS NOT TOLD OF: no newest message ⇒ not buried ⇒ an
    /// edit in place. What answers the restart since 2026-09-30 is the ENGINE's traffic record
    /// (<c>ITopicTraffic.Find_Newest_OrAssumeBuried</c>), which hands the planner a newest message just
    /// above a line it has never seen traffic for — so the first eligible tick after a restart moves it
    /// once. Kept at the planner level because "unknown is not buried" is still the planner's rule; the
    /// assumption is made, and pinned, where the knowledge is missing.
    /// </summary>
    [Fact]
    public void APlannerToldOfNoTraffic_DoesNotRepost()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: null, newestTopicMessage: null).Action);
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
            Plan(newestTopicMessage: Newest(9999)).Action);
    }

    /// <summary>
    /// AN UNKNOWN TOPIC IS NOT A BURIED ONE, at the planner — see <see cref="APlannerToldOfNoTraffic_DoesNotRepost"/>
    /// for where the restart is answered instead.
    /// </summary>
    [Fact]
    public void ATopicWithNoKnownTrafficIsNotReposted()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line", newestTopicMessage: null).Action);
    }

    /// <summary>
    /// THE DELIVERY GATE APPLIES TO SILENCED, and it falls back to the EDIT rather than to silence:
    /// the edit notifies nobody, so the line stays current instead of freezing, and only the MOVE to
    /// the bottom waits. Kept through the 2026-09-30 rule: 🔕 means the owner is reading this in a
    /// terminal, so nothing is put in the topic.
    /// </summary>
    [Fact]
    public void ASilencedTopicIsNotRepostedIntoAndFallsBackToTheEdit()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(mode: TelegramDeliveryModes.Silenced, existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20), sessionSilence: Session_Spoke(NOW.AddMinutes(-2))).Action);
    }

    /// <summary>
    /// A DEFERRED TOPIC STILL MOVES ITS LINE, silently — the repost half of the same ruling. Under 🌙 the
    /// owner is away and will read this topic when they return; a PULSE stranded above an hour of later
    /// traffic is the one thing they then have to scroll for.
    /// </summary>
    [Fact]
    public void ADeferredTopicStillMovesItsLine_Silently()
    {
        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(mode: TelegramDeliveryModes.Deferred, existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20), sessionSilence: Session_Spoke(NOW.AddMinutes(-2))).Action);
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
        // `current` is captured under the SAME mode the second call uses. The header now carries the
        // mode glyph (the planner fills `fields.Mode` in from `mode` before calling the builder — see
        // TopicStatusLine_Planner.Plan's own comment, "THE MODE IS FILLED IN HERE"), so a "previously
        // written" text for an ALREADY-silenced topic would itself read `🔕 PULSE`, never bare `PULSE`.
        var current = Plan(mode: TelegramDeliveryModes.Silenced, existingMessageId: STATUS_ID).Text;

        Assert.Equal(
            TopicStatusActions.None,
            Plan(mode: TelegramDeliveryModes.Silenced, existingMessageId: STATUS_ID, lastWrittenText: current,
                 newestTopicMessage: Newest(STATUS_ID + 20), sessionSilence: Session_Spoke(NOW.AddMinutes(-2))).Action);
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
                 newestTopicMessage: Newest(STATUS_ID + 20), lastFailedAttemptAt: NOW.AddSeconds(-5)).Action);

        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20), lastFailedAttemptAt: NOW.AddSeconds(-BACKOFF)).Action);
    }

    // ── TEXT AGAINST TEXT, AND THE BAR BESIDE IT (plan 03 Task 17, 2026-09-23) ──────────────────────
    //
    // The engine remembered only the render key and handed it in as `lastWrittenText`, so the planner
    // compared "<length>:<text>|…" with the raw text: every tick answered Edit (plan 03 report §5.3).
    // The engine now remembers both and hands in both; these pin what the planner does with them.

    static readonly IReadOnlyList<IReadOnlyList<(string Data, string Label)>> THREE_VERB_BAR =
        [[("cmd:screen", "📸 /screen"), ("cmd:show", "👁 /show"), ("cmd:merge", "🔀 /merge")]];

    static readonly IReadOnlyList<IReadOnlyList<(string Data, string Label)>> TWO_VERB_BAR =
        [[("cmd:screen", "📸 /screen"), ("cmd:show", "👁 /show")]];

    /// <summary>
    /// WHAT WAS WRITTEN, HANDED BACK AS IT WAS WRITTEN, IS NOT WRITTEN AGAIN — unless the line is buried
    /// and the session quiet, which since 2026-09-30 moves it regardless of content (this case asserted
    /// None for the buried half until then).
    /// </summary>
    [Fact]
    public void TheRenderingJustWritten_IsNotWrittenAgain_UnlessItIsBuried()
    {
        var written = Plan(existingMessageId: STATUS_ID, commandButtonRows: THREE_VERB_BAR);

        Assert.Equal(TopicStatusLine_RenderKey.Build(written.Text, THREE_VERB_BAR), written.RenderKey);

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: written.Text, lastWrittenRenderKey: written.RenderKey,
                 commandButtonRows: THREE_VERB_BAR).Action);

        Assert.Equal(
            TopicStatusActions.Repost,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: written.Text, lastWrittenRenderKey: written.RenderKey,
                 commandButtonRows: THREE_VERB_BAR, newestTopicMessage: Newest(STATUS_ID + 20)).Action);
    }

    /// <summary>
    /// THE BAR ALONE CHANGED, the line is still last: one edit. The text is identical, so a text
    /// comparison says nothing moved — the render key is what sees it (brief D: the held count lives on
    /// a label, and a quiet orchestration's text does not move for hours).
    /// </summary>
    [Fact]
    public void AChangeToTheBarAlone_IsAnEdit()
    {
        var written = Plan(existingMessageId: STATUS_ID, commandButtonRows: THREE_VERB_BAR);

        var plan = Plan(existingMessageId: STATUS_ID, lastWrittenText: written.Text, lastWrittenRenderKey: written.RenderKey,
                        commandButtonRows: TWO_VERB_BAR);

        Assert.Equal(written.Text, plan.Text);
        Assert.Equal(TopicStatusActions.Edit, plan.Action);
        Assert.Equal(TopicStatusLine_RenderKey.Build(written.Text, TWO_VERB_BAR), plan.RenderKey);
    }

    /// <summary>
    /// THE BAR ALONE CHANGED UNDER A BURIED LINE WHILE THE SESSION IS RECENT: an edit in place, not a
    /// move. Until 2026-09-30 a changed bar was "news" that moved a buried line; a move is now the
    /// session clock's to decide, and the bar is repainted where the line is.
    /// </summary>
    [Fact]
    public void AChangeToTheBarAlone_UnderABuriedLine_IsEditedWhileTheSessionIsRecent()
    {
        var written = Plan(existingMessageId: STATUS_ID, commandButtonRows: THREE_VERB_BAR);

        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: written.Text, lastWrittenRenderKey: written.RenderKey,
                 commandButtonRows: TWO_VERB_BAR, newestTopicMessage: Newest(STATUS_ID + 20),
                 sessionSilence: Session_Spoke(NOW.AddSeconds(-5))).Action);
    }

    /// <summary>
    /// AND THE BAR'S EDIT OBEYS THE BACK-OFF. It used to be an engine branch after the plan, and a
    /// FAILED edit leaves the last rendering SENT standing on purpose — so without the back-off a 429
    /// is retried at the tick rate (2026-09-10 on the VPS: 357 of 382 refusals were that branch).
    /// </summary>
    [Fact]
    public void AChangeToTheBarAlone_WaitsForTheBackOff()
    {
        var written = Plan(existingMessageId: STATUS_ID, commandButtonRows: THREE_VERB_BAR);

        Assert.Equal(
            TopicStatusActions.None,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: written.Text, lastWrittenRenderKey: written.RenderKey,
                 commandButtonRows: TWO_VERB_BAR, lastFailedAttemptAt: NOW.AddSeconds(-5)).Action);

        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: written.Text, lastWrittenRenderKey: written.RenderKey,
                 commandButtonRows: TWO_VERB_BAR, lastFailedAttemptAt: NOW.AddSeconds(-BACKOFF)).Action);
    }

    /// <summary>
    /// THE HEARTBEAT, UNDER AN UNCHANGED BAR, IS AN EDIT IN PLACE while the session is recent: handing the
    /// bar in must not turn the clock into a reason to move early.
    /// </summary>
    [Fact]
    public void AHeartbeatStep_UnderTheSameBar_EditsABuriedLineInPlace_WhileTheSessionIsRecent()
    {
        var buried = Newest(STATUS_ID + 3);

        var written = TopicStatusLine_Planner.Plan(
            A_Ledger(), [], NOW, STATUS_ID, null, TelegramDeliveryModes.Normal, null, BACKOFF, buried, repostIsImpossible: false,
            commandButtonRows: THREE_VERB_BAR);

        var fiveMinutesLater = TopicStatusLine_Planner.Plan(
            A_Ledger(), [], NOW.AddMinutes(5), STATUS_ID, written.Text, TelegramDeliveryModes.Normal, null, BACKOFF, buried, repostIsImpossible: false,
            commandButtonRows: THREE_VERB_BAR, lastWrittenRenderKey: written.RenderKey,
            sessionSilence: Session_Spoke(NOW.AddMinutes(5).AddSeconds(-20)));

        Assert.NotEqual(written.Text, fiveMinutesLater.Text);
        Assert.Equal(TopicStatusActions.Edit, fiveMinutesLater.Action);
    }

    /// <summary>
    /// The predicate on its own, at the edges Plan cannot show as clearly. EQUAL ids are the subtle one:
    /// the newest message the app knows of IS the status line itself — which, since every send is
    /// recorded (2026-09-30), is the ordinary state right after a post — so nothing came after it.
    /// </summary>
    [Fact]
    public void TheRepostPredicateAtItsEdges()
    {
        var quiet = Session_Spoke(NOW.AddMinutes(-5));

        Assert.False(TopicStatusLine_Planner.Is_RepostDue(null, Newest(9999), quiet));
        Assert.False(TopicStatusLine_Planner.Is_RepostDue(STATUS_ID, null, quiet));
        Assert.False(TopicStatusLine_Planner.Is_RepostDue(STATUS_ID, Newest(STATUS_ID), quiet));
        Assert.True(TopicStatusLine_Planner.Is_RepostDue(STATUS_ID, Newest(STATUS_ID + 1), quiet));
        Assert.False(TopicStatusLine_Planner.Is_RepostDue(STATUS_ID, Newest(STATUS_ID + 1), Session_Spoke(NOW.AddSeconds(-59))));
        Assert.True(TopicStatusLine_Planner.Is_RepostDue(STATUS_ID, Newest(STATUS_ID + 1), null));
    }

    /// <summary>
    /// A SESSION MESSAGE STAMPED IN THE FUTURE is not a quiet session. Both stamps come from the same
    /// clock, so this can only come from a clock step — and it must hold the move rather than treat a
    /// negative elapsed as "long enough".
    /// </summary>
    [Fact]
    public void ASessionMessageStampedInTheFutureDoesNotCountAsQuiet()
    {
        Assert.False(TopicStatusLine_Planner.Is_RepostDue(
            STATUS_ID, Newest(STATUS_ID + 1), Session_Spoke(NOW.AddMinutes(5))));
    }

    /// <summary>
    /// A REPOST STILL HAS TO HAVE SOMETHING TO SEND — nothing happens when there is nothing to show. The
    /// route to an empty repost is closed by construction (a repost needs an existing id, which makes
    /// the builder fall back to the literal `PULSE`), and the planner's emptiness guard is the belt
    /// behind those braces. Asserted on the TEXT as well as the action: the body is the half Telegram
    /// rejects.
    /// </summary>
    [Fact]
    public void ARepostAlwaysHasSomethingToSend()
    {
        var plan = Plan(
            members: [],
            existingMessageId: STATUS_ID,
            newestTopicMessage: Newest(STATUS_ID + 20));

        Assert.Equal("PULSE", plan.Text);
        Assert.Equal(TopicStatusActions.Repost, plan.Action);
    }

    /// <summary>
    /// THE LATCH, rev-1 F1. A delete that is REFUSED rather than failed loops forever and starves the
    /// edit with it: `Is_MessageGone` matches none of the refusal wordings, so the id is never
    /// cleared, the delete throws before the send every time, and because the repost overrides the
    /// decider the Edit never runs either. Latched, the topic stops trying to MOVE its line and keeps
    /// updating it in place — degrading to master's behaviour rather than to nothing.
    /// </summary>
    [Fact]
    public void ATopicWhereTheRepostIsImpossibleKeepsEditingInPlace()
    {
        Assert.Equal(
            TopicStatusActions.Edit,
            Plan(existingMessageId: STATUS_ID, lastWrittenText: "an older line",
                 newestTopicMessage: Newest(STATUS_ID + 20), repostIsImpossible: true).Action);
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
                 newestTopicMessage: Newest(STATUS_ID + 20), repostIsImpossible: true).Action);
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
                 newestTopicMessage: Newest(STATUS_ID + 20), repostIsImpossible: false).Action);
    }

    /// <summary>A buried line whose session last spoke exactly this many seconds before NOW.</summary>
    static TopicStatusLine_Planner.TopicStatusPlan Plan_WhenTheSessionSpoke(int secondsAgo)
    {
        return Plan(
            existingMessageId: STATUS_ID,
            lastWrittenText: "an older line",
            newestTopicMessage: Newest(STATUS_ID + 20),
            sessionSilence: Session_Spoke(NOW.AddSeconds(-secondsAgo)));
    }

    static TopicStatusLine_Planner.TopicNewestMessage Newest(long messageId)
    {
        return new TopicStatusLine_Planner.TopicNewestMessage(messageId);
    }

    /// <summary>The session's last message at <paramref name="spokeAt"/>, read against the fixture's NOW on the same clock.</summary>
    static TopicStatusLine_Planner.TopicSessionSilence Session_Spoke(DateTime spokeAt)
    {
        return new TopicStatusLine_Planner.TopicSessionSilence(spokeAt, NOW);
    }

    static TopicStatusLine_Planner.TopicStatusPlan Plan(
        IReadOnlyList<ITopicStatusMember>? members = null,
        IPlanProgress? progress = null,
        long? existingMessageId = null,
        string? lastWrittenText = null,
        TelegramDeliveryModes mode = TelegramDeliveryModes.Normal,
        DateTime? lastFailedAttemptAt = null,
        TopicStatusLine_Planner.TopicNewestMessage? newestTopicMessage = null,
        bool repostIsImpossible = false,
        IReadOnlyList<IReadOnlyList<(string Data, string Label)>>? commandButtonRows = null,
        string? lastWrittenRenderKey = null,
        TopicStatusLine_Planner.TopicSessionSilence? sessionSilence = null)
    {
        return TopicStatusLine_Planner.Plan(
            progress,
            members ?? [Member("imp-1", "fix the parser", "2026-08-12 14:50")],
            NOW,
            existingMessageId,
            lastWrittenText,
            mode,
            lastFailedAttemptAt,
            BACKOFF,
            newestTopicMessage,
            repostIsImpossible,
            commandButtonRows: commandButtonRows,
            lastWrittenRenderKey: lastWrittenRenderKey,
            sessionSilence: sessionSilence);
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
            TopicStatusLine_Planner.Pick_LastEvent_OrNull([solo], NOW)?.Subject);
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

        Assert.Equal("fix landed", TopicStatusLine_Planner.Pick_LastEvent_OrNull([solo], NOW)?.Subject);
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

        Assert.Equal("brief — TASK 2", TopicStatusLine_Planner.Pick_LastEvent_OrNull([imp], NOW)?.Subject);
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
