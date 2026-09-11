using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.Decisions;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE TWO RULES THAT CHANGE WHAT A TAP MEANS, driven through the real engine.
///
/// <para>
/// The pure deciders behind them are pinned in their own tests, and those tests would stay green if
/// nothing ever called them — which is the failure this file exists to rule out. Three separate
/// defects in this repo's history lived in the WIRING rather than in the rule, and the note that
/// records them says it plainly: extracting a decision into a testable seam does not pin the call
/// site that uses it.
/// </para>
/// <para>
/// So these drive the engine's own loops: a real question written into a channel, real buttons, a
/// real callback tap, and a clock the test moves.
/// </para>
/// </summary>
public class HighRiskAndDeadlineProbeTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4343;

    /// <summary>Carries "push", which is on the default high-risk list — the owner's own example.</summary>
    const string RISKY_QUESTION = "Ready to push the release branch to main?";
    const string RISKY_OPTION = "Yes, push it now";
    const string SAFE_OPTION = "No, hold";

    const string TIMED_QUESTION = "Which report format do you want?";
    const string TIMED_FIRST_OPTION = "A short summary";
    const string TIMED_SECOND_OPTION = "The full breakdown";

    /// <summary>
    /// The three lines <see cref="OwnerQuestion_Contract"/> requires of every question the app will
    /// forward. <c>RISK: low</c> on purpose: these tests are about the PATTERN classifier, so the
    /// lock they assert has to come from the words, never from the asker's own declaration.
    /// </summary>
    const string CONTRACT_LINES = "\nRECOMMEND: hold, unless you have a reason not to.\nRISK: low\nROW: none";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly CapturingTelegram_Fake _telegram = new();

    /// <summary>
    /// Started at the WALL CLOCK rather than at a fixed date, and that is deliberate: the usage-probe
    /// selection this app has always done reads <c>DateTime.Now</c> directly, so a fixture window has
    /// to be live on the real clock for the file to be considered at all. Only the deadline sweep and
    /// the pause read the injected clock; everything moved here is moved through it.
    /// </summary>
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);

    readonly IBridgeEngine _engine;

    public HighRiskAndDeadlineProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-highrisk-tests-{Guid.NewGuid():N}");
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
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// A3.5 — A TAP NEVER APPROVES A PUSH. The tap opens a read-back instead; only the code the
    /// message shows releases the answer to the session, and a wrong code releases nothing.
    ///
    /// <para>
    /// The assertion that carries the weight is the NEGATIVE one: after the tap, and again after the
    /// wrong code, the option's text is still absent from the channel — meaning the session was
    /// never told to push. A test that only checked the happy path would stay green if the code
    /// prompt were decorative.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATapOnAPushQuestion_DeliversNothing_UntilTheCodeShownInTheMessageIsTypedBack()
    {
        var session = await Start_WithQuestion_Async($"QUESTION: {RISKY_QUESTION}\nOPTION: {RISKY_OPTION}\nOPTION: {SAFE_OPTION}{CONTRACT_LINES}", RISKY_OPTION);

        var button = _telegram.Find_ButtonFor(RISKY_OPTION)
            ?? throw new Exception("the risky option never reached the phone");

        var questionMessageId = _telegram.LastButtonMessageId
            ?? throw new Exception("the question was sent with no message id");

        // ── the tap ──────────────────────────────────────────────────────────────────────────
        _telegram.Queue_Updates(Build_CallbackTapJson(button, questionMessageId));

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Edited_Containing("You are about to"), 20_000),
            "the tap on a high-risk option did not open the read-back at all."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // COUNTED, NOT SEARCHED FOR, and the difference is the whole assertion. The supervisor's own
        // entry contains "OPTION: Yes, push it now" — it is where the option came from — so
        // "the channel does not contain the option" is false before anything happens and would make
        // this test pass by accident. What DELIVERY looks like is a SECOND occurrence, written by
        // the app as the owner's answer.
        Assert.Equal(1, Count_Occurrences(Read_OwnerChannel(session.OrchId), RISKY_OPTION));

        var confirmationText = _telegram.Find_EditedContaining("You are about to")
            ?? throw new Exception("unreachable — asserted above");

        var code = Regex.Match(confirmationText, @"\b\d{4}\b").Value;
        Assert.Equal(ConfirmationCode.DIGITS, code.Length);

        // ── a wrong code ─────────────────────────────────────────────────────────────────────
        var wrongCode = code == "0000" ? "1111" : "0000";
        _telegram.Queue_Updates(Build_OwnerMessageJson(wrongCode, updateId: 3002, messageId: 78));

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing("not the code"), 20_000),
            "a wrong code was not reported back to the owner, so they would be left waiting on a "
            + "decision that silently never happened."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        Assert.Equal(1, Count_Occurrences(Read_OwnerChannel(session.OrchId), RISKY_OPTION));

        // ── the right code ───────────────────────────────────────────────────────────────────
        _telegram.Queue_Updates(Build_OwnerMessageJson(code, updateId: 3003, messageId: 79));

        Assert.True(
            await Run_Until_Async(() => Count_Occurrences(Read_OwnerChannel(session.OrchId), RISKY_OPTION) > 1, 20_000),
            "the correct read-back code did not release the decision, which makes a high-risk "
            + "question unanswerable rather than merely harder to answer."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // AND THE CODE NEVER REACHES THE CHANNEL. It is shown in one Telegram message on purpose;
        // an append-only file that agents read is where a ten-minute code becomes a permanent one.
        Assert.DoesNotContain(code, Read_OwnerChannel(session.OrchId), StringComparison.Ordinal);
        Assert.False(_log.Has_Line_Containing(code), "the read-back code was written to the orchestrator log");
    }

    /// <summary>
    /// A3.6 — a question that declares a deadline and a default takes that default when the owner
    /// never answers, edits its own message to say so (never sends a second one), and writes the
    /// outcome into the channel so the catch-up burst shows what was decided in their absence.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AQuestionWithADeadline_TakesItsDefault_AndSaysSoWhereTheOwnerWillReadItBack()
    {
        var session = await Start_WithQuestion_Async(
            $"QUESTION: {TIMED_QUESTION}\nOPTION: {TIMED_FIRST_OPTION}\nOPTION: {TIMED_SECOND_OPTION}\nDEADLINE: 60m\nDEFAULT: 2{CONTRACT_LINES}",
            TIMED_SECOND_OPTION);

        // The terms are IN the message, because a deadline the owner cannot see is a decision taken
        // behind their back.
        Assert.True(_telegram.Has_Sent_Containing("If you do not answer by"), _telegram.Dump_Sent());

        // ── half the window ──────────────────────────────────────────────────────────────────
        _clock.Advance(TimeSpan.FromMinutes(31));

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Edited_Containing("Still waiting"), 20_000),
            "no reminder was issued at the half-way point."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // A REMINDER IS AN EDIT, NEVER A SECOND MESSAGE (decision 14): a repeat that arrives as a
        // notification is the waterfall this whole system exists to prevent.
        Assert.False(_telegram.Has_Sent_Containing("Still waiting"));

        // ── past the deadline ────────────────────────────────────────────────────────────────
        _clock.Advance(TimeSpan.FromMinutes(31));

        Assert.True(
            await Run_Until_Async(() => Read_OwnerChannel(session.OrchId).Contains(TIMED_SECOND_OPTION, StringComparison.Ordinal), 20_000),
            "THE DEFECT: the deadline passed and the declared default was never taken — the question "
            + "the owner was promised would resolve itself simply stalled."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        var channel = Read_OwnerChannel(session.OrchId);

        // The channel is the record the owner reads back and the digest is built from, so the fact
        // that this was a TIMEOUT and not them has to be in it — not only in a Telegram edit.
        Assert.Contains("DEFAULTED on timeout", channel, StringComparison.Ordinal);
    }

    /// <summary>
    /// A3.6, THE HALF THAT MUST NOT BE SYMMETRIC — a high-risk question expires as a DENY even when
    /// the agent declared a default for it, and the option is never delivered.
    ///
    /// <para>
    /// This is the one property in the stage that a plausible-looking implementation gets wrong by
    /// being consistent: applying the declared default is what every other question does. A push
    /// approved because a phone was in a pocket is the outcome the second gesture exists to prevent,
    /// and a default would walk straight around it.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AHighRiskQuestionThatLapses_IsDenied_EvenThoughItsAgentDeclaredADefault()
    {
        var session = await Start_WithQuestion_Async(
            $"QUESTION: {RISKY_QUESTION}\nOPTION: {RISKY_OPTION}\nOPTION: {SAFE_OPTION}\nDEADLINE: 30m\nDEFAULT: 1{CONTRACT_LINES}",
            RISKY_OPTION);

        // The message says DENIED rather than naming an option, so the owner is never told a default
        // that will not be honoured.
        Assert.True(_telegram.Has_Sent_Containing("this is DENIED"), _telegram.Dump_Sent());
        Assert.False(_telegram.Has_Sent_Containing($"option 1 ({RISKY_OPTION}) is taken"));

        _clock.Advance(TimeSpan.FromMinutes(31));

        Assert.True(
            await Run_Until_Async(() => Read_OwnerChannel(session.OrchId).Contains("DENIED on timeout", StringComparison.Ordinal), 20_000),
            "the lapsed high-risk question was never resolved, so the session blocked on it is still "
            + "blocked and nothing says why."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // Still exactly once — the supervisor's own entry, and nothing delivered. See the counted
        // assertion in the tap test for why a plain "does not contain" would prove nothing here.
        Assert.Equal(1, Count_Occurrences(Read_OwnerChannel(session.OrchId), RISKY_OPTION));
    }

    /// <summary>
    /// A3.8 — over the threshold the dispatcher PAUSES: no new session is started or respawned, one
    /// alert carries the resume time, and the app resumes by itself when the window comes back.
    ///
    /// <para>
    /// Sixty sessions hitting the same limit are sixty identical errors today. What is asserted here
    /// is the visible half — the single alert and the automatic resume — plus the invariant that the
    /// alert is not repeated on every tick, which would be the waterfall arriving through the fix.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task OverTheLimit_TheDispatcherPauses_ThenResumesOnItsOwnWhenTheWindowReturns()
    {
        var resetsAtUtc = _clock.UtcNow.AddMinutes(20);

        Write_UsageProbe("five_hour", 97, resetsAtUtc);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing("Dispatch PAUSED"), 20_000),
            "THE DEFECT: the account was at 97% and the app kept starting work into a wall, with "
            + "nothing but the per-threshold alerts to show for it."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // THE PAUSE IS PERSISTED, so a crash — which a limit crash-looping the sessions makes far
        // more likely — cannot lift it by restarting.
        Assert.NotNull(_engineState.Load_OrEmpty().DispatchPausedUntilUtc);

        var alertsAfterTheFirst = _telegram.Count_Sent_Containing("Dispatch PAUSED");

        // THE CLOCK IS MOVED PAST THE PROBE-READING THROTTLE, and without that this assertion was
        // satisfied by two routes: the 60-second interval alone kept the count at one, so deleting
        // the already-paused guard it is meant to pin left the test green. Moved forward — but not
        // past the resume time — only the guard can hold the count.
        _clock.Advance(TimeSpan.FromMinutes(2));

        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(10));

        Assert.Equal(alertsAfterTheFirst, _telegram.Count_Sent_Containing("Dispatch PAUSED"));

        // ── the window returns ───────────────────────────────────────────────────────────────
        _clock.Advance(TimeSpan.FromMinutes(21));

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing("Dispatch resumed"), 20_000),
            "the pause never lifted by itself — a pause a human has to lift is a pause that outlives "
            + "its cause, and the owner is asleep when a five-hour window rolls over."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // Settled, not sampled on the tick the alert happened to land on.
        Assert.True(
            await Run_Until_Async(() => _engineState.Load_OrEmpty().DispatchPausedUntilUtc == null, 10_000),
            "the resume alert went out but the pause was still recorded in the persisted state");
    }

    /// <summary>
    /// A TAP MUST NOT CONVERT A BOUNDED QUESTION INTO AN UNBOUNDED ONE — the worst thing this stage
    /// could have done, and it did it for one commit.
    ///
    /// <para>
    /// Opening the read-back used to remove the question from the open set, which is the only place
    /// the deadline sweep looks. So a high-risk question carrying `DEADLINE: 30m` — whose entire
    /// contract is "denied at thirty minutes" — became invisible the moment the owner tapped it. The
    /// code lapsed ten minutes later and nothing noticed; the awaiting-answer flag was never cleared,
    /// so the supervisor's hook denied every tool call for ever, and /pending printed "the code has
    /// EXPIRED" on every restart with no way to clear it.
    /// </para>
    /// <para>
    /// The owner here does exactly what a person does: taps, means to type the code, and never gets
    /// back to it.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATapThatIsNeverConfirmed_StillLetsTheQuestionLapse_InsteadOfBlockingForever()
    {
        var session = await Start_WithQuestion_Async(
            $"QUESTION: {RISKY_QUESTION}\nOPTION: {RISKY_OPTION}\nOPTION: {SAFE_OPTION}\nDEADLINE: 30m{CONTRACT_LINES}",
            RISKY_OPTION);

        var button = _telegram.Find_ButtonFor(RISKY_OPTION)
            ?? throw new Exception("the risky option never reached the phone");

        var questionMessageId = _telegram.LastButtonMessageId
            ?? throw new Exception("the question was sent with no message id");

        _telegram.Queue_Updates(Build_CallbackTapJson(button, questionMessageId));

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Edited_Containing("You are about to"), 20_000),
            $"the read-back never opened.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // The code lapses first (10 minutes), the question's own deadline second (30).
        _clock.Advance(TimeSpan.FromMinutes(31));

        Assert.True(
            await Run_Until_Async(() => Read_OwnerChannel(session.OrchId).Contains("DENIED on timeout", StringComparison.Ordinal), 20_000),
            "THE DEFECT: a tapped-but-unconfirmed high-risk question was never resolved — the session "
            + "blocked on it stays blocked, and nothing anywhere says why."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // And the option was never delivered: still the supervisor's own OPTION: line, nothing else.
        Assert.Equal(1, Count_Occurrences(Read_OwnerChannel(session.OrchId), RISKY_OPTION));

        // The lapsed read-back is gone from the state, so /pending cannot print it for ever.
        Assert.Empty(_engineState.Load_OrEmpty().PendingConfirmations);
    }

    /// <summary>
    /// The high-risk list is matched against the OPTIONS too, not only the question line.
    ///
    /// <para>
    /// This is the shape an agent naturally writes — a neutral question with the dangerous verb in
    /// the choice — and it walked straight past the gate: one tap on a pocketed phone delivered
    /// "Push the release branch to main" with no code asked for.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AHarmlessQuestionWithAPushOption_IsStillHighRisk_BecauseTheDangerIsInTheChoice()
    {
        await Start_WithQuestion_Async(
            // SHORT ENOUGH TO STAY ON THE BUTTON. Past ~28 characters OptionButtons_Layout moves the
            // full text into the message body and numbers the buttons, so a longer label would make
            // this test fail on the layout rule rather than on the rule it is about.
            "QUESTION: How should I proceed?\nOPTION: Push to main\nOPTION: Hold" + CONTRACT_LINES,
            "Push to main");

        var button = _telegram.Find_ButtonFor("Push to main")
            ?? throw new Exception("the option never reached the phone");

        var questionMessageId = _telegram.LastButtonMessageId
            ?? throw new Exception("the question was sent with no message id");

        _telegram.Queue_Updates(Build_CallbackTapJson(button, questionMessageId));

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Edited_Containing("You are about to"), 20_000),
            "THE DEFECT: the question line said nothing dangerous, so one tap delivered the push."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ANarrativeThatMERELYMENTIONSADeploy_DoesNotLockAProductQuestion()
    {
        // Measured on one topic, 2026-09-07: four pure product questions arrived under 🔐 because
        // the prose around them said "the deployed engine crashes on these keys" and "a deploy
        // check already blocks this from shipping". Nothing was being deployed. The surface is now
        // the question and its options; the body it came from is not read.
        await Start_WithQuestion_Async(
            "The engine added 27 allocation methods on 2026-08-26 and the deployed engine rejects them;\n"
            + "a deploy check already blocks this from shipping by accident.\n"
            + "QUESTION: Which plan gets the tail-risk methods?\nOPTION: Advanced\nOPTION: Ultimate" + CONTRACT_LINES,
            "Advanced");

        var button = _telegram.Find_ButtonFor("Advanced")
            ?? throw new Exception("the option never reached the phone");

        var questionMessageId = _telegram.LastButtonMessageId
            ?? throw new Exception("the question was sent with no message id");

        _telegram.Queue_Updates(Build_CallbackTapJson(button, questionMessageId));

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Edited_Containing("Advanced"), 20_000),
            $"the tap never resolved.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        Assert.False(
            _telegram.Has_Edited_Containing("You are about to"),
            "THE DEFECT: a pricing question was locked behind a 4-digit code because its NARRATIVE "
            + $"mentioned a deploy.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");
    }

    /// <summary>
    /// Starts an orchestration, gets the engine past its first pass over the channel, then appends
    /// the question. THE ORDER IS LOad-BEARING: the tailer baselines a channel it has never seen at
    /// its CURRENT length, so anything written before that pass is absorbed as history and never
    /// mirrored at all.
    /// </summary>
    async Task<IOrchestrationSession> Start_WithQuestion_Async(string questionBody, string expectedOptionLabel)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        _telegram.Queue_Updates(Build_OwnerMessageJson("what is happening", updateId: 3001, messageId: 77));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Owner message buffered"), 15_000),
            $"the engine never made its first pass.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        File.AppendAllText(
            channelFile,
            $"\n## [3] FROM supervisor — {DateTime.Now:yyyy-MM-dd HH:mm} — a question\n{questionBody}\n");

        Assert.True(
            await Run_Until_Async(() => _telegram.Find_ButtonFor(expectedOptionLabel) != null, 20_000),
            $"the question never reached the phone.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        return session;
    }

    void Write_UsageProbe(string windowKey, double percent, DateTime resetsAtUtc)
    {
        var unixSeconds = new DateTimeOffset(DateTime.SpecifyKind(resetsAtUtc, DateTimeKind.Utc), TimeSpan.Zero).ToUnixTimeSeconds();

        File.WriteAllText(
            Path.Combine(_paths.Root, $"{Guid.NewGuid():N}.usage.json"),
            $"{{\"rate_limits\":{{\"{windowKey}\":{{\"used_percentage\":{percent},\"resets_at\":{unixSeconds}}}}}}}");
    }

    string Read_OwnerChannel(string orchId) => File.ReadAllText(_paths.Get_OwnerChannelFile(orchId));

    static int Count_Occurrences(string text, string fragment)
    {
        var count = 0;
        var index = text.IndexOf(fragment, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
        }

        return count;
    }

    static string Build_OwnerMessageJson(string text, long updateId = 3001, long messageId = 77)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    static string Build_CallbackTapJson(string callbackData, long questionMessageId)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":3010,\"callback_query\":{\"id\":\"cbq-1\","
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
