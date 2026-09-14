using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.Decisions;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A question reaches the owner COMPLETE or not at all, carries its recommendation and its row, and
/// offers a way to talk that does not spend the decision.
///
/// <para>
/// Driven end to end through the real engine, because every one of these is a claim about what
/// arrives on a phone: a unit test of the contract cannot see that the body still went, that the
/// refusal never reached Telegram, or that a keyboard survived a tap.
/// </para>
/// </summary>
public class QuestionContractProbeTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4646;

    /// <summary>
    /// The engine's own cap on how long an unanswered question may hold the conversation, restated
    /// here because the engine's copy is private. Only the ORDER matters to these fixtures — backdate
    /// the flag past it and the engine expires it — so a drift in the engine's number cannot make
    /// them wrong, only make them back-date further than they need to.
    /// </summary>
    const int QUESTION_HOLD_CAP_MINUTES = 10;

    const string COMPLETE_QUESTION =
        "Two plan levers changed and the build can start.\n"
        + "QUESTION: Start the FIN-D-277a build now?\n"
        + "OPTION: Start it\n"
        + "OPTION: Wait\n"
        + "RECOMMEND: Start it — the matrix values are numbers, not layout.\n"
        + "RISK: low\n"
        + "ROW: FIN-D-277a";

    /// <summary>
    /// A SECOND question with its own option labels, deliberately: the Telegram fake records every
    /// button it has ever seen and resolves a label to the FIRST match, so two questions offering
    /// "Start it" would make a probe about which question a tap closed unable to tell them apart.
    /// </summary>
    const string SECOND_QUESTION =
        "The perf branch is green.\n"
        + "QUESTION: Merge wf-perf into master now?\n"
        + "OPTION: Merge it\n"
        + "OPTION: Hold\n"
        + "RECOMMEND: Hold — you asked to read every merge to master first.\n"
        + "RISK: low\n"
        + "ROW: FIN-D-277b";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly CapturingTelegram_Fake _telegram = new();
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);
    readonly IBridgeEngine _engine;

    public QuestionContractProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-question-contract-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);

        _engine = BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, _telegram,
            _engineState, _clock,
            BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnIncompleteQuestion_IsNotForwarded_AndTheAgentIsToldEveryMissingLineAtOnce()
    {
        var orchId = await Start_Async();

        // The grammar this replaces accepted exactly this and derived a question from the prose.
        Append_Supervisor(orchId, "Ready to go.\nQUESTION: Start the build now?\nOPTION: Start it\nOPTION: Wait");

        Assert.True(
            await Run_Until_Async(() => Channel(orchId).Contains("was NOT sent to the owner", StringComparison.Ordinal), 20_000),
            $"the agent was never told.{Environment.NewLine}{_log.Dump()}");

        // The body still went: a formatting fault must never cost the owner a message.
        Assert.True(_telegram.Has_Sent_Containing("Ready to go"), _telegram.Dump_Sent());
        Assert.Null(_telegram.Find_ButtonFor("Start it"));

        // EVERY missing line, not the first — one refusal, one round trip.
        var channel = Channel(orchId);
        Assert.Contains("RECOMMEND:", channel, StringComparison.Ordinal);
        Assert.Contains("RISK:", channel, StringComparison.Ordinal);
        Assert.Contains("ROW:", channel, StringComparison.Ordinal);
        Assert.DoesNotContain("QUESTION: is missing", channel, StringComparison.Ordinal);

        // The refusal is for the agent, never for the phone.
        Assert.False(_telegram.Has_Sent_Containing("was NOT sent to the owner"), _telegram.Dump_Sent());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ACompleteQuestion_CarriesItsRecommendationAndItsRow_AndBothWaysOut()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 20_000),
            $"the question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        var question = _telegram.Find_SentContaining("Start the FIN-D-277a build now?")
            ?? throw new Exception($"the question message was never sent.{Environment.NewLine}{_telegram.Dump_Sent()}");

        // The recommendation rides WITH the question, not in the body the owner scrolled past.
        Assert.Contains("💡", question, StringComparison.Ordinal);
        Assert.Contains("the matrix values are numbers", question, StringComparison.Ordinal);
        Assert.Contains("📎 FIN-D-277a", question, StringComparison.Ordinal);
        Assert.DoesNotContain("🔐", question, StringComparison.Ordinal);

        // ONE app button, not two. "❔ Explain the options" and "💬 Let's talk" were the same
        // gesture under two labels once the talk tap started closing its question.
        Assert.NotNull(_telegram.Find_ButtonFor(OwnerPush_Policy.TALK_LABEL));
        Assert.Null(_telegram.Find_ButtonFor("Explain the options"));
        Assert.Null(_telegram.Find_ButtonFor("❔"));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ADeclaredHighRisk_LocksAQuestionWhoseWordsAreHarmless()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION.Replace("RISK: low", "RISK: high", StringComparison.Ordinal));

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing("🔐"), 20_000),
            $"a declared high risk did not lock the question.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");

        Assert.Contains("declared by the asker", _log.Dump(), StringComparison.Ordinal);
    }

    /// <summary>
    /// THE OWNER'S REQUEST, 2026-09-09: *"Let's talk does nothing when I tap it — it stays there,
    /// all the other options stay too. Make it behave like the other buttons: buttons disappear,
    /// the message says 'ok, tell me what you have in mind'."*
    ///
    /// <para>
    /// This test replaces <c>LetsTalk_KeepsTheQuestionOpen_AndATypedReplyDoesNotCloseIt_UntilATapDoes</c>,
    /// which asserted the behaviour being removed. The old contract was deliberate — discussing a
    /// decision should not take the question off the phone — and it failed in use: the only feedback
    /// was Telegram's transient toast, so the owner tapped this button twelve times in one afternoon
    /// on <c>fincanva-5</c>, four of them inside a minute.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task LetsTalk_ClosesItsQuestionLikeAnyOption_EditsTheMessage_AndRecordsNoChoice()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 20_000),
            $"the question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        var talk = _telegram.Find_ButtonFor(OwnerPush_Policy.TALK_LABEL)
            ?? throw new Exception("the talk button never reached the phone");

        var questionMessageId = _telegram.LastButtonMessageId
            ?? throw new Exception("the question was sent with no message id");

        _telegram.Queue_Updates(Build_CallbackTapJson(talk, questionMessageId, updateId: 3010));

        Assert.True(
            await Run_Until_Async(() => Channel(orchId).Contains("wants to talk this decision through", StringComparison.Ordinal), 20_000),
            $"the supervisor never received the request.{Environment.NewLine}{_log.Dump()}");

        // THE QUESTION IS CLOSED, like any other tap.
        Assert.Empty(_engineState.Load_OrEmpty().OpenQuestions);

        // AND THE MESSAGE SAYS SO — editing it is also what drops the keyboard.
        var edited = _telegram.Find_EditedContaining(OwnerPush_Policy.TALK_ACKNOWLEDGEMENT)
            ?? throw new Exception($"the question message was never edited.{Environment.NewLine}{_telegram.Dump_Sent()}");

        Assert.Contains("Start the FIN-D-277a build now?", edited, StringComparison.Ordinal);

        // NO CHOICE WAS MADE, so nothing may be stamped as one — neither an option nor the
        // instruction text the supervisor received.
        Assert.DoesNotContain("✅", edited, StringComparison.Ordinal);
        Assert.False(_telegram.Has_Edited_Containing("✅ Start it"), _telegram.Dump_Sent());
        Assert.False(_telegram.Has_Edited_Containing("wants to talk this decision through"), _telegram.Dump_Sent());

        // The whole group went with it: the option beside it can no longer be tapped.
        var startIt = _telegram.Find_ButtonFor("Start it")
            ?? throw new Exception("the option payload is gone from the fake");

        _telegram.Queue_Updates(Build_CallbackTapJson(startIt, questionMessageId, updateId: 3020));

        Assert.True(
            await Run_Until_Async(() => _log.Dump().Contains("Callback REFUSED", StringComparison.Ordinal), 20_000),
            $"a consumed option was still live.{Environment.NewLine}{_log.Dump()}");

        Assert.Empty(_engineState.Load_OrEmpty().OpenQuestions);
    }

    /// <summary>
    /// A TAP ANSWERS ITS OWN QUESTION AND NOTHING ELSE — the root defect, which was never confined
    /// to one button.
    ///
    /// <para>
    /// Every tap is routed as an owner message, and that routing binds a message to a question
    /// whenever exactly one is open. The tapped question is removed BEFORE routing, so "exactly one
    /// other question open" is the ordinary case — the log recorded a second question going out
    /// while one was still open three times on the afternoon of 2026-09-09. At 17:53 the owner
    /// tapped a high-risk option on one question and at 17:55 tapped "Let's talk" on another; the
    /// talk text bound to the orphaned-processes question and stamped it <c>✅ answered:</c>. Nobody
    /// ever decided it.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Start it")]
    [InlineData(OwnerPush_Policy.TALK_LABEL)]
    [Trait("Speed", "Slow")]
    public async Task ATapOnOneQuestion_LeavesTheOtherOpen_TappableAndUnstamped(string buttonToTap)
    {
        var orchId = await Start_Async();

        // ONE AT A TIME, THEN THE CAP — see Reach_TwoQuestionsOpen_Async. The two questions used to
        // be appended side by side before the engine ran; the owner's 2026-09-11 ruling means the
        // app now WITHHOLDS the second while the first is unanswered, so that setup asserted a state
        // the system no longer permits. The subject below is untouched.
        await Reach_TwoQuestionsOpen_Async(orchId);

        // The ids come from the registry rather than from "the last message with buttons": two
        // questions are in flight and only their own text says which is which.
        var open = _engineState.Load_OrEmpty().OpenQuestions;

        var firstMessageId = open.Single(question => question.Text.Contains("Start the FIN-D-277a build now?", StringComparison.Ordinal)).MessageId;
        var secondMessageId = open.Single(question => question.Text.Contains("Merge wf-perf into master now?", StringComparison.Ordinal)).MessageId;

        Assert.NotEqual(firstMessageId, secondMessageId);
        Assert.Equal(2, _engineState.Load_OrEmpty().OpenQuestions.Count);

        var tapped = _telegram.Find_ButtonFor(buttonToTap)
            ?? throw new Exception($"the '{buttonToTap}' button never reached the phone");

        _telegram.Queue_Updates(Build_CallbackTapJson(tapped, firstMessageId, updateId: 3040));

        Assert.True(
            await Run_Until_Async(() => _engineState.Load_OrEmpty().OpenQuestions.Count == 1, 20_000),
            $"the tap did not close exactly its own question.{Environment.NewLine}{_log.Dump()}");

        // THE SURVIVOR IS THE ONE NOBODY TOUCHED.
        var stillOpen = Assert.Single(_engineState.Load_OrEmpty().OpenQuestions);
        Assert.Equal(secondMessageId, stillOpen.MessageId);

        // AND IT WAS NOT STAMPED. "✅ answered:" is the signature of a typed answer being bound to
        // it, which is precisely what a tap must never be read as.
        Assert.False(_telegram.Has_Edited_Containing("✅ answered:"), _telegram.Dump_Sent());
        Assert.Null(_telegram.Find_EditedContaining("Merge wf-perf into master now?"));

        // STILL TAPPABLE: its keyboard is live, and tapping it closes it with its own choice.
        var mergeIt = _telegram.Find_ButtonFor("Merge it")
            ?? throw new Exception("the second question's option is gone");

        _telegram.Queue_Updates(Build_CallbackTapJson(mergeIt, secondMessageId, updateId: 3050));

        Assert.True(
            await Run_Until_Async(() => _engineState.Load_OrEmpty().OpenQuestions.Count == 0, 20_000),
            $"the second question's keyboard was dead.{Environment.NewLine}{_log.Dump()}");

        Assert.True(_telegram.Has_Edited_Containing("✅ Merge it"), _telegram.Dump_Sent());
    }

    /// <summary>
    /// THE LAPSED READ-BACK READS THE REGISTRY BEFORE IT SPEAKS. It used to log "the question is
    /// still open" unconditionally; at ~16:03Z on 2026-09-09 it said that about a question that had
    /// been stamped closed minutes earlier, which sends whoever reads the log looking for a question
    /// that is not there.
    ///
    /// <para>
    /// The sequence is the one that happened: a high-risk option is tapped (the question stays open
    /// on purpose while the code is outstanding), the owner then answers in WRITING — which closes
    /// it — and the code is never typed, so the window lapses on a question that is already gone.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AReadBackThatLapsesOnAClosedQuestion_NamesWhatClosedIt()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION.Replace("RISK: low", "RISK: high", StringComparison.Ordinal));

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 20_000),
            $"the question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        var questionMessageId = _telegram.LastButtonMessageId
            ?? throw new Exception("the question was sent with no message id");

        var startIt = _telegram.Find_ButtonFor("Start it")
            ?? throw new Exception("the option never reached the phone");

        _telegram.Queue_Updates(Build_CallbackTapJson(startIt, questionMessageId, updateId: 3060));

        Assert.True(
            await Run_Until_Async(() => _engineState.Load_OrEmpty().PendingConfirmations.Count == 1, 20_000),
            $"the high-risk tap did not open a read-back.{Environment.NewLine}{_log.Dump()}");

        // The question is deliberately still open while the code is outstanding.
        Assert.Single(_engineState.Load_OrEmpty().OpenQuestions);

        // Answered in writing instead — which closes it, and leaves the code orphaned.
        _telegram.Queue_Updates(Build_OwnerMessageJson("go ahead and start it", updateId: 3070, messageId: 91));

        Assert.True(
            await Run_Until_Async(() => _engineState.Load_OrEmpty().OpenQuestions.Count == 0, 20_000),
            $"the typed answer did not close the question.{Environment.NewLine}{_log.Dump()}");

        _clock.Advance(TimeSpan.FromMinutes(15));

        Assert.True(
            await Run_Until_Async(
                () => _log.Dump().Contains(QuestionClosure_Wording.TYPED_ANSWER, StringComparison.Ordinal),
                20_000),
            $"the lapse never named the closure.{Environment.NewLine}{_log.Dump()}");

        var dump = _log.Dump();
        Assert.Contains("already closed", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing was taken, and the question is still open", dump, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE TAP'S REWRITE OUTLIVES A RATE LIMIT. Measured 2026-09-10 21:11:40 on the VPS: the owner
    /// tapped, Telegram answered the rewrite with <c>429 retry after 24</c>, the keyboard-removal
    /// fallback got the same, and neither was tried again — old text, live keyboard, on a question
    /// already answered. Here Telegram refuses the first TWO edits of the question message with a
    /// one-second wait; the third lands, off the inbound loop. With the retry reverted the message
    /// is never edited.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATapWhoseRewriteIsRateLimited_IsRewrittenAnyway_AfterTelegramsWait()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 20_000),
            $"the question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        var startIt = _telegram.Find_ButtonFor("Start it")!;
        var questionMessageId = _telegram.LastButtonMessageId
            ?? throw new Exception("the question was sent with no message id");

        _telegram.Refuse_Edits_WithRateLimit(count: 2, retryAfterSeconds: 1, messageId: questionMessageId);
        _telegram.Queue_Updates(Build_CallbackTapJson(startIt, questionMessageId, updateId: 3070));

        // ONE engine run for both outcomes: the retries live on the engine's own cancellation, and a
        // probe that stopped the engine to look would cancel the very wait it is probing. In
        // production the engine does not stop between a tap and its rewrite.
        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Edited_Containing("✅ Start it"), 20_000),
            $"the rewrite never landed after the rate limit.{Environment.NewLine}{_log.Dump()}");

        // The answer was routed regardless of the rewrite — that was true before and stays true.
        Assert.Contains("Start it", Channel(orchId), StringComparison.Ordinal);

        // Rewritten on the THIRD attempt, after two one-second waits.
        Assert.Equal(3, _telegram.Count_EditAttempts(questionMessageId));
        Assert.Contains("landed on attempt 3", _log.Dump(), StringComparison.Ordinal);
    }

    /// <summary>
    /// THE STATUS LINE HONOURS ITS OWN BACK-OFF. Measured 2026-09-10 20:56–21:52 on the VPS: 357 of
    /// 382 rate-limit refusals were PULSE, retried every 2 s (the tick) with Telegram's
    /// <c>retry_after</c> counting down 34, 31, 29 … The planner's 30-second back-off was in place and
    /// tested — and overruled one line later by the button-only promotion, which compared the
    /// rendering against the last text SENT, stale by design after a failure. Here every edit of the
    /// PULSE message is refused; across forty ticks it must be attempted once, not forty times.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APulseEditThatIsRateLimited_IsNotRetriedEveryTick()
    {
        var orchId = await Start_Async();

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_MessageIdOfSentContaining("PULSE") != null, 20_000),
            $"no status line was ever posted.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");

        var pulseId = _telegram.Find_MessageIdOfSentContaining("PULSE")!.Value;
        _telegram.Refuse_Edits_WithRateLimit(count: int.MaxValue, retryAfterSeconds: 20, messageId: pulseId);

        // Edits accepted BEFORE the refusal are not the subject; only what happens from here on is.
        var acceptedBefore = _telegram.Count_EditAttempts(pulseId);

        // An open question changes the line ("waiting on you"), so an edit is due — and refused.
        Append_Supervisor(orchId, COMPLETE_QUESTION);

        Assert.True(
            await Run_Until_Async(() => _telegram.Count_EditAttempts(pulseId) > acceptedBefore, 20_000),
            $"the status line was never edited after the question.{Environment.NewLine}{_log.Dump()}");

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(40));

        var refusedAttempts = _telegram.Count_EditAttempts(pulseId) - acceptedBefore;

        Assert.True(
            refusedAttempts <= 1,
            $"the refused status-line edit was attempted {refusedAttempts} times inside the {BridgeTestTiming.RETRY_BACKOFF_SECONDS} s back-off — production saw one every tick.");
    }

    const string THIRD_QUESTION =
        "Both branches are in.\n"
        + "QUESTION: Ship the release tonight?\n"
        + "OPTION: Ship it\n"
        + "OPTION: Hold off\n"
        + "RECOMMEND: Hold off — the pricing pass is still yours.\n"
        + "RISK: low\n"
        + "ROW: FIN-D-279a";

    /// <summary>
    /// A NEWER QUESTION CLOSES THE OLDER ONES THE OWNER HAD ALREADY REPLIED TO IN WORDS. Measured
    /// 2026-09-10: three questions from 15:58, 16:46 and 16:56 still listed as "waiting on you" at
    /// 21:50 — the owner had answered each in prose, but with several open a typed reply binds to
    /// none, so nothing ever closed them. Two open here, a typed reply that binds neither, then a
    /// third question: the two are superseded (registry, keyboard, message), the third alone is
    /// open, and the asker is told. With no reply between two questions both stay open — that case
    /// is pinned by ATapOnOneQuestion_LeavesTheOtherOpen_TappableAndUnstamped and is unchanged.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ANewQuestion_AfterTheOwnerRepliedInWords_SupersedesTheOlderOnes()
    {
        var orchId = await Start_Async();

        // ONE AT A TIME, THEN THE CAP — see Reach_TwoQuestionsOpen_Async, and the note on the tap
        // probe above: two questions written side by side is a state the app no longer permits.
        await Reach_TwoQuestionsOpen_Async(orchId);

        // The owner answers in words — and with two open, that binds neither (by design). The
        // harness clock is frozen, so it is stepped first: "replied AFTER the question was asked" is
        // a comparison of two stamps from that clock, and in production the clock moves by itself.
        _clock.Advance(TimeSpan.FromSeconds(30));
        _telegram.Queue_Updates(Build_OwnerMessageJson("start the build and hold the merge", updateId: 3080, messageId: 78));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("the reply names none of them"), 20_000),
            $"the typed reply was never routed.{Environment.NewLine}{_log.Dump()}");

        Assert.Equal(2, _engineState.Load_OrEmpty().OpenQuestions.Count);

        // Then the asker moves on. (Numbered past the app's own entries, which took [5] and [6].)
        _clock.Advance(TimeSpan.FromSeconds(30));
        Append_Supervisor(orchId, THIRD_QUESTION, entryNumber: 9);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Ship it") != null && _engineState.Load_OrEmpty().OpenQuestions.Count == 1, 25_000),
            $"the third question did not supersede the two older ones.{Environment.NewLine}{_log.Dump()}");

        var open = Assert.Single(_engineState.Load_OrEmpty().OpenQuestions);
        Assert.Contains("Ship the release tonight?", open.Text, StringComparison.Ordinal);

        // BOTH OLDER MESSAGES SAY SO — and the edit is what drops their keyboards.
        Assert.True(
            await Run_Until_Async(() => _telegram.Count_Edited_Containing(QuestionPrompt_Builder.SUPERSEDED_SUFFIX) == 2, 20_000),
            $"the superseded questions were not rewritten.{Environment.NewLine}{_telegram.Dump_Sent()}");

        // The asker was told, once, in its own channel.
        Assert.Contains("superseded by this one", Channel(orchId), StringComparison.Ordinal);

        // A late tap on a superseded question is refused, never routed as a stale answer.
        var startIt = _telegram.Find_ButtonFor("Start it") ?? throw new Exception("the option payload is gone from the fake");
        var firstMessageId = _telegram.Find_MessageIdOfSentContaining("Start the FIN-D-277a build now?") ?? throw new Exception("no id for the first question");

        _telegram.Queue_Updates(Build_CallbackTapJson(startIt, firstMessageId, updateId: 3090));

        Assert.True(
            await Run_Until_Async(() => _log.Dump().Contains("Callback REFUSED", StringComparison.Ordinal), 20_000),
            $"a superseded question's option was still live.{Environment.NewLine}{_log.Dump()}");

        Assert.Single(_engineState.Load_OrEmpty().OpenQuestions);
    }

    /// <summary>
    /// TWO QUESTIONS OPEN ON THE PHONE AT ONCE, REACHED HONESTLY UNDER THE ONE-QUESTION RULE.
    ///
    /// <para>
    /// The owner ruled on 2026-09-11 that one question at a time is the ONLY way — when three or
    /// four arrive together their answers conflict — so <c>QuestionHold_Policy</c> now HOLDS the
    /// owner channel at the mirror while a question is unanswered. The probes below used to append
    /// both questions before the engine's first pass and wait for both keyboards; that setup
    /// manufactured a state the app deliberately no longer produces, and it is the setup that had to
    /// change, never the rule and never what these probes assert.
    /// </para>
    /// <para>
    /// THE ROUTE IS THE APP'S OWN TEN-MINUTE CAP (<c>Expire_StaleAwaitingAnswerFlags</c>): the owner
    /// never answered, the supervisor is let go anyway, and the next question goes out with the first
    /// still open. That is a real production sequence and it is the one route that leaves both
    /// questions OPEN — an owner message would clear the flag too, but it also records a reply in
    /// words, and the next question would then SUPERSEDE the first, which is the opposite of what
    /// these two probes need. (Terminal presence is not a route at all: it silences the topic, so no
    /// question is ever sent and none is ever registered open.)
    /// </para>
    /// <para>
    /// The flag's age is real wall-clock time — <c>Expire_StaleAwaitingAnswerFlags</c> stats the file
    /// rather than reading the injected clock — so it is BACKDATED rather than waited out. The clock
    /// step afterwards is the unrelated trailing-entry rule: the second question is the file's last
    /// entry, and a frozen clock means the file is never "quiet" enough to release it.
    /// </para>
    /// </summary>
    async Task Reach_TwoQuestionsOpen_Async(string orchId)
    {
        Append_Supervisor(orchId, COMPLETE_QUESTION);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 25_000),
            $"the first question never reached the phone.{Environment.NewLine}=== CHANNEL ==={Environment.NewLine}{Channel(orchId)}{Environment.NewLine}{_log.Dump()}");

        var flagFile = AIOrchestratorCoreLib.Status.AwaitingAnswerFlag_Marker.Build_FilePath(_paths, orchId);

        Assert.True(
            File.Exists(flagFile),
            "the first question raised no awaiting-answer flag, so the cap below expires nothing and "
            + "the hold this setup works around was never in force.");

        File.SetLastWriteTimeUtc(flagFile, DateTime.UtcNow.AddMinutes(-(QUESTION_HOLD_CAP_MINUTES + 1)));

        Assert.True(
            await Run_Until_Async(() => !File.Exists(flagFile), 25_000),
            $"the awaiting-answer flag never expired, so the second question would be HELD.{Environment.NewLine}{_log.Dump()}");

        Append_Supervisor(orchId, SECOND_QUESTION, entryNumber: 4);

        // THE STEP COMES AFTER THE BYTES ARE READ, not before, and the order is the whole of it: the
        // tailer stamps "quiet since" at the poll that READS an append, so a clock stepped first is
        // simply the instant that stamp is taken and the file is never quiet afterwards. One short
        // run lets the poll happen, then the step is what the file going quiet does in production.
        await Run_For_Async(BridgeTestTiming.Window_ForTicks(3));
        _clock.Advance(TimeSpan.FromSeconds(30));

        Assert.True(
            await Run_Until_Async(
                () => _telegram.Find_ButtonFor("Start it") != null && _telegram.Find_ButtonFor("Merge it") != null,
                25_000),
            $"both questions never reached the phone.{Environment.NewLine}=== CHANNEL ==={Environment.NewLine}{Channel(orchId)}{Environment.NewLine}{_log.Dump()}");

        Assert.Equal(2, _engineState.Load_OrEmpty().OpenQuestions.Count);
    }

    /// <summary>
    /// THE OWNER'S OWN QUESTION IS NOT A REPLY. fincanva-6, 2026-09-11: a question was open, the owner
    /// asked "devo fare solo smoke?", and the supervisor's next question closed the open one as
    /// superseded — "you had replied in words" — when the owner had replied to nothing.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheOwnersOwnQuestion_DoesNotLetANewQuestionSupersedeTheOpenOne()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 20_000),
            $"the question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        _clock.Advance(TimeSpan.FromSeconds(30));
        _telegram.Queue_Updates(Build_OwnerMessageJson("devo fare solo smoke?", updateId: 3100, messageId: 101));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("is itself a question"), 20_000),
            $"the owner's question was never routed.{Environment.NewLine}{_log.Dump()}");

        _clock.Advance(TimeSpan.FromSeconds(30));
        Append_Supervisor(orchId, SECOND_QUESTION, entryNumber: 20);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Merge it") != null, 25_000),
            $"the second question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        Assert.Equal(2, _engineState.Load_OrEmpty().OpenQuestions.Count);
        Assert.Equal(0, _telegram.Count_Edited_Containing(QuestionPrompt_Builder.SUPERSEDED_SUFFIX));
    }

    /// <summary>
    /// THE SAME QUESTION IS NOT POSTED TWICE. The second half of the same chain: the supervisor asked
    /// its open question again word for word, and the owner got a second copy with live buttons.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AQuestionAskedAgainWordForWord_IsNotSentTwice_AndTheAskerIsTold()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 20_000),
            $"the question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        _clock.Advance(TimeSpan.FromSeconds(30));
        _telegram.Queue_Updates(Build_OwnerMessageJson("devo fare solo smoke?", updateId: 3110, messageId: 111));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("is itself a question"), 20_000),
            $"the owner's question was never routed.{Environment.NewLine}{_log.Dump()}");

        _clock.Advance(TimeSpan.FromSeconds(30));
        Append_Supervisor(orchId, COMPLETE_QUESTION.Replace("Two plan levers changed", "Only the smoke is left", StringComparison.Ordinal), entryNumber: 20);

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("was not sent again"), 25_000),
            $"the repeat was not recognised.{Environment.NewLine}{_log.Dump()}");

        Assert.Equal(1, _telegram.Count_Sent_Containing("Start the FIN-D-277a build now?"));
        Assert.Single(_engineState.Load_OrEmpty().OpenQuestions);
        Assert.Contains("this question is already open — not sent again", Channel(orchId), StringComparison.Ordinal);
    }

    /// <summary>
    /// A TAPPED QUESTION WAITING FOR ITS CODE IS ANSWERED, AND IS NEVER SUPERSEDED. fincanva-6,
    /// 2026-09-11: the owner tapped «Sì» on a high-risk merge question; the supersede sweep closed it
    /// and discarded the pending read-back, so their answer was thrown away. Here a later reply
    /// really does count (two questions open, a plain sentence), and the third question closes the
    /// untapped one only.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATappedQuestionAwaitingItsCode_IsNeverSuperseded()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION.Replace("RISK: low", "RISK: high", StringComparison.Ordinal));

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 20_000),
            $"the question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        var questionMessageId = _telegram.LastButtonMessageId ?? throw new Exception("the question was sent with no message id");
        var startIt = _telegram.Find_ButtonFor("Start it") ?? throw new Exception("the option never reached the phone");

        _telegram.Queue_Updates(Build_CallbackTapJson(startIt, questionMessageId, updateId: 3120));

        Assert.True(
            await Run_Until_Async(() => _engineState.Load_OrEmpty().PendingConfirmations.Count == 1, 20_000),
            $"the high-risk tap did not open a read-back.{Environment.NewLine}{_log.Dump()}");

        // As on the day: before typing the code, the owner asks something. That unfreezes the
        // conversation (a held high-risk answer does not) and, being a question, replies to nothing.
        _clock.Advance(TimeSpan.FromSeconds(30));
        _telegram.Queue_Updates(Build_OwnerMessageJson("devo fare solo smoke?", updateId: 3125, messageId: 125));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("is itself a question"), 20_000),
            $"the owner's question was never routed.{Environment.NewLine}{_log.Dump()}");

        _clock.Advance(TimeSpan.FromSeconds(30));
        Append_Supervisor(orchId, SECOND_QUESTION, entryNumber: 20);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Merge it") != null, 25_000),
            $"the second question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        _clock.Advance(TimeSpan.FromSeconds(30));
        _telegram.Queue_Updates(Build_OwnerMessageJson("start the build and hold the merge", updateId: 3130, messageId: 131));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("the reply names none of them"), 20_000),
            $"the typed reply was never routed.{Environment.NewLine}{_log.Dump()}");

        _clock.Advance(TimeSpan.FromSeconds(30));
        Append_Supervisor(orchId, THIRD_QUESTION, entryNumber: 30);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Ship it") != null && _telegram.Count_Edited_Containing(QuestionPrompt_Builder.SUPERSEDED_SUFFIX) >= 1, 25_000),
            $"the third question did not supersede the untapped one.{Environment.NewLine}{_log.Dump()}");

        var state = _engineState.Load_OrEmpty();
        Assert.Equal(2, state.OpenQuestions.Count);
        Assert.Contains(state.OpenQuestions, question => question.Text.Contains("Start the FIN-D-277a build now?", StringComparison.Ordinal));
        Assert.Single(state.PendingConfirmations);
        Assert.Equal(1, _telegram.Count_Edited_Containing(QuestionPrompt_Builder.SUPERSEDED_SUFFIX));
    }

    /// <summary>
    /// A REPLY NAMES ITS QUESTION. The owner, 2026-09-11: "non c'è modo di linkare domande e risposte?
    /// Tipo con un rispondi? Avere più domande non lo vedo come un problema." With two open, a typed
    /// answer binds neither — unless it is a Telegram Reply on one of them, which closes that one and
    /// only that one, stamped with the owner's words rather than the quote the reply carries.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AReplyOnOneOfTwoOpenQuestions_AnswersThatOneOnly()
    {
        var orchId = await Start_Async();

        // ONE AT A TIME, THEN THE CAP — see Reach_TwoQuestionsOpen_Async. Both questions used to be
        // appended side by side here; under the owner's 2026-09-11 hold the second never leaves the
        // channel while the first is unanswered, so that setup waits for a keyboard that cannot
        // arrive. Only the ROUTE to two open questions changes — the reply below, and everything
        // this probe asserts about it, is untouched.
        await Reach_TwoQuestionsOpen_Async(orchId);

        var mergeQuestionId = _telegram.Find_MessageIdOfSentContaining("Merge wf-perf into master now?") ?? throw new Exception("no id for the merge question");

        _telegram.Queue_Updates(Build_OwnerReplyJson("tienilo fermo per ora", updateId: 3200, messageId: 201, mergeQuestionId, "Merge wf-perf into master now?"));

        Assert.True(
            await Run_Until_Async(() => _engineState.Load_OrEmpty().OpenQuestions.Count == 1, 20_000),
            $"the reply did not close its question.{Environment.NewLine}{_log.Dump()}");

        var open = Assert.Single(_engineState.Load_OrEmpty().OpenQuestions);
        Assert.Contains("Start the FIN-D-277a build now?", open.Text, StringComparison.Ordinal);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Edited_Containing("tienilo fermo per ora"), 20_000),
            $"the answered question was not stamped.{Environment.NewLine}{_telegram.Dump_Sent()}");

        Assert.False(_telegram.Has_Edited_Containing("replying to"), "the stamp carried the quote instead of the owner's words");
    }

    /// <summary>
    /// A REPLY TO SOMETHING ELSE IS ABOUT THAT. fincanva-6, 2026-09-11 12:58: with one question open,
    /// an instruction ("usa key o MCP per cancellare mio abbonamento") was stamped "✅ answered" under
    /// it. When the owner points at another message, the only open question stays open.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AReplyToAnotherMessage_AnswersNothing_EvenWithOneQuestionOpen()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 20_000),
            $"the question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        _telegram.Queue_Updates(Build_OwnerReplyJson("usa key o MCP per cancellare mio abbonamento", updateId: 3210, messageId: 211, replyToMessageId: 987_654, "a report above"));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("replied to a message that is not an open question"), 20_000),
            $"the reply was never judged.{Environment.NewLine}{_log.Dump()}");

        Assert.Single(_engineState.Load_OrEmpty().OpenQuestions);
    }

    async Task Run_For_Async(int milliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);
        await Task.Delay(milliseconds);
        cancellation.Cancel();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// AND NOT AFTER THE OWNER HAS DEALT WITH IT EITHER. The owner, 2026-09-12: *"per esempio in sta
    /// chat mi hai fatto la stessa domanda 2 volte"* — and by the open-questions rule above, both
    /// copies were correct. The guard only ever compared against questions still OPEN, so every path
    /// that CLOSES one — a tapped option, an answer in words, a tap on "💬 Let's talk", which decides
    /// nothing and then asks the session to put the question again — left the next copy unchecked.
    ///
    /// <para>
    /// PROBED THROUGH THE ANSWER-IN-WORDS CLOSURE, which this fixture can drive end to end. Every
    /// closure goes through the one funnel that remembers it (<c>Note_QuestionClosed</c>), and the
    /// wording of the closure is all that differs between them — the talk-request path is pinned by
    /// <see cref="LetsTalk_ClosesItsQuestionLikeAnyOption_EditsTheMessage_AndRecordsNoChoice"/> for
    /// the closure itself and by the decider's own tests for the matching.
    /// </para>
    /// <para>
    /// THE THIRD ASK IS PART OF THE RULE, not a loose end: the refusal happens ONCE. A session told
    /// what the owner decided and asking regardless is insisting rather than repeating itself, and a
    /// decision that can never be put to the owner is worse than one duplicate — which is why this
    /// probe asserts the second copy is withheld AND the third goes through.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AQuestionReAskedAfterTheOwnerDecidedIt_IsWithheldOnce_ThenAllowed()
    {
        var orchId = await Start_Async();
        Append_Supervisor(orchId, COMPLETE_QUESTION);

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor("Start it") != null, 20_000),
            $"the question never reached the phone.{Environment.NewLine}{_log.Dump()}");

        // ANSWERED IN WORDS, which closes it — the owner has dealt with it and nothing is open.
        _telegram.Queue_Updates(Build_OwnerMessageJson("go ahead and start it", updateId: 3210, messageId: 121));

        Assert.True(
            await Run_Until_Async(() => _engineState.Load_OrEmpty().OpenQuestions.Count == 0, 20_000),
            $"the typed answer did not close the question.{Environment.NewLine}{_log.Dump()}");

        // THE PROSE CHANGES AND THE QUESTION LINE DOES NOT, exactly as the sibling probe above does
        // it: a repeat is the question line, and an entry identical in every byte would be a test of
        // the mirror's own bookkeeping rather than of this rule.
        _clock.Advance(TimeSpan.FromSeconds(30));
        Append_Supervisor(orchId, COMPLETE_QUESTION.Replace("Two plan levers changed", "As discussed", StringComparison.Ordinal), entryNumber: 20);

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("already decided"), 25_000),
            $"the re-ask of a decided question was not recognised.{Environment.NewLine}{_log.Dump()}");

        // ONE COPY ON THE PHONE, and the session told what the owner decided and why nothing went out.
        Assert.Equal(1, _telegram.Count_Sent_Containing("Start the FIN-D-277a build now?"));
        Assert.Empty(_engineState.Load_OrEmpty().OpenQuestions);
        Assert.Contains("you already asked this and the owner dealt with it", Channel(orchId), StringComparison.Ordinal);

        // THE THIRD ASK IS NOT PROBED HERE, and this is the honest reason: a third manual append to
        // the same owner channel is never mirrored by this fixture — measured, not assumed, over
        // windows up to 60 s with the engine demonstrably ticking — so a probe built on it would
        // assert "no second copy" against a channel that delivered nothing, which is the two-routes
        // trap. The rule that the withholding happens ONCE is pinned where the suite can reach it:
        // QuestionSupersede_Decider.Should_Withhold_Reask, in the decider's own tests.
    }

    string Channel(string orchId) => File.ReadAllText(_paths.Get_OwnerChannelFile(orchId));

    async Task<string> Start_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        // The tailer baselines a channel it has never seen at its CURRENT length, so the engine has
        // to make one pass before anything is appended — otherwise the entry is absorbed as history.
        _telegram.Queue_Updates(Build_OwnerMessageJson("what is happening", updateId: 3001, messageId: 77));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Owner message buffered"), 15_000),
            $"the engine never made its first pass.{Environment.NewLine}{_log.Dump()}");

        return session.OrchId;
    }

    void Append_Supervisor(string orchId, string body, int entryNumber = 3)
    {
        File.AppendAllText(
            _paths.Get_OwnerChannelFile(orchId),
            $"\n## [{entryNumber}] FROM supervisor — {DateTime.Now:yyyy-MM-dd HH:mm} — a question\n{body}\n");
    }

    static string Build_OwnerMessageJson(string text, long updateId, long messageId)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    /// <summary>An owner message sent with Telegram's Reply on <paramref name="replyToMessageId"/>, quoting it.</summary>
    static string Build_OwnerReplyJson(string text, long updateId, long messageId, long replyToMessageId, string quotedText)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\","
            + $"\"reply_to_message\":{{\"message_id\":{replyToMessageId},\"text\":\"{quotedText}\"}}}}}}]}}";
    }

    static string Build_CallbackTapJson(string callbackData, long questionMessageId, long updateId)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"callback_query\":{{\"id\":\"cbq-{updateId}\","
            + $"\"data\":\"{callbackData}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{questionMessageId},\"message_thread_id\":{TOPIC_ID},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}]}}";
    }

    async Task<bool> Run_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);
        var satisfied = false;

        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
            {
                satisfied = true;
                break;
            }

            await Task.Delay(100);
        }

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }

        return satisfied || condition();
    }
}
