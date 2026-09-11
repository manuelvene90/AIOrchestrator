using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Time.Clock;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE ACCEPTANCE TEST OF THE DURABLE BRIDGE: kill the app with decisions pending, start it again,
/// and nothing the owner was asked has been forgotten.
///
/// <para>
/// WHAT IT USED TO DO. Every decisional dictionary in the engine lived in memory and only in
/// memory. A restart therefore silently discarded: the open questions (so away mode could no longer
/// tell an answered question from an unanswered one), the button registry (so every keyboard already
/// on the owner's phone answered "expired" to a tap they had every reason to believe in — with no
/// way to discover that except by trying), the "I already nudged this member about this" memory
/// (so every reopen re-nudged every member), and the crash-loop counters (so the alert, which fires
/// at exactly the threshold, simply never fired for the machine-wide cause that was producing the
/// restarts). None of these announced themselves. The channel files stayed intact, which is what
/// made the loss invisible.
/// </para>
/// <para>
/// WHY IT IS SHAPED THIS WAY. A restart cannot be simulated inside one engine — the whole claim is
/// about what a SECOND engine sees — so this drives two, over one shared
/// <see cref="IEngineStateStore"/>. That store is the in-memory implementation rather than the file
/// one on purpose: the property being pinned is "the second engine reads what the first wrote", and
/// a real file adds a second thing that can fail without adding anything to the assertion. The file
/// implementation's own shape is pinned separately, in EngineStateSerializerTests.
/// </para>
/// <para>
/// AND THE TAP IS THE POINT. Asserting the state came back would be satisfied by a snapshot nobody
/// can use; what the owner cares about is that the button on their phone still works. So the last
/// act is a real callback tap, on a payload captured from the FIRST engine, delivered to the SECOND.
/// </para>
/// </summary>
public class DecisionStateSurvivesARestartTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    const string QUESTION_TEXT = "Which way do you want the cache invalidated?";
    const string FIRST_OPTION = "Invalidate on write";
    const string SECOND_OPTION = "Invalidate on a timer";

    /// <summary>
    /// A SECOND question, so the snapshot holds more than one decision. Deliberately free of any
    /// word on the high-risk list: what is being pinned here is durability, and a question that also
    /// demanded a read-back code would be testing two things through one assertion.
    /// </summary>
    const string SECOND_QUESTION_TEXT = "Where should the report land?";
    const string THIRD_OPTION = "Into the ledger";
    const string FOURTH_OPTION = "Into the topic only";

    /// <summary>
    /// A member channel key and a watchdog slot key that belong to NO session in this test, so the
    /// engine's own sweeps cannot clear them. A key the watchdog might legitimately reset would make
    /// a green here mean "nothing touched it", which is a different claim from "it was restored".
    /// </summary>
    const string NUDGED_MEMBER_KEY = "not-a-real-orch/imp-9";
    const string RESPAWN_SLOT_KEY = "sup:not-a-real-orch";
    const string NUDGED_ABOUT = "the entry it was already nudged about";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly FixedClock_Fake _clock = new(new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc));
    readonly RecordingLog_Fake _log = new();

    public DecisionStateSurvivesARestartTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-restart-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // The inbound loop reads the chat and owner ids and throws without them.
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task FiveDecisionsPending_SurviveTheProcessThatWasHoldingThem_AndTheButtonsStillWork()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        // The two memories that have no visible surface of their own are seeded straight into the
        // store, because what is being asserted about them is precisely that a fresh engine picks
        // them up — not how they came to be written.
        _engineState.Save(_engineState.Load_OrEmpty() with
        {
            NudgedAboutEntry = new Dictionary<string, string> { [NUDGED_MEMBER_KEY] = NUDGED_ABOUT },
            ConsecutiveRespawns = new Dictionary<string, int> { [RESPAWN_SLOT_KEY] = 2 },
        });

        // ── The first process ────────────────────────────────────────────────────────────────
        var firstTelegram = new CapturingTelegram_Fake();
        var firstEngine = Build_Engine(firstTelegram);

        // The owner speaks, which is what raises the "they are waiting for an answer" flag.
        firstTelegram.Queue_Updates(Build_OwnerMessageJson("what is the state of the cache work"));

        Assert.True(
            await Run_Until_Async(firstEngine, () => _log.Has_Info_Containing("Owner message buffered"), 15_000),
            "the owner's message never reached the router, so the waiting flag was never raised."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // THE OWNER'S OUTSTANDING WAIT IS PERSISTED, asserted here rather than at the end because
        // the supervisor's very next entry legitimately consumes it — that is R1's rule, and this is
        // the only window in which the flag is both raised and outstanding. Before this stage it
        // lived in memory alone, so closing the app while an answer was in flight dropped that
        // answer silently: the same failure R1 names, reached by a restart instead of a failed send.
        Assert.Contains(session.OrchId, _engineState.Load_OrEmpty().OwnerAwaitingAnswer);

        // THE QUESTION IS APPENDED ONLY NOW, and that ordering is not incidental: the tailer
        // baselines a channel it has never seen at its CURRENT length, so anything written before
        // the engine's first pass is absorbed as history and never mirrored at all.
        Append_SupervisorQuestion(session.OrchId, 3, QUESTION_TEXT, FIRST_OPTION, SECOND_OPTION);
        Append_SupervisorQuestion(session.OrchId, 4, SECOND_QUESTION_TEXT, THIRD_OPTION, FOURTH_OPTION);

        Assert.True(
            await Run_Until_Async(
                firstEngine,
                () => firstTelegram.Find_ButtonFor(FIRST_OPTION) != null && firstTelegram.Find_ButtonFor(THIRD_OPTION) != null,
                25_000),
            "the question never reached the phone, so this test never reached the code it is about."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        var capturedButton = firstTelegram.Find_ButtonFor(FIRST_OPTION)
            ?? throw new Exception("unreachable — asserted non-null above");

        var questionMessageId = firstTelegram.LastButtonMessageId
            ?? throw new Exception("the question was sent with no message id, so no tap could ever refer to it");

        // THE PAYLOAD IS A NONCE, not a counter. Asserted here rather than in a unit test because
        // this is the only place that proves the ENGINE mints one: a counter restarting at zero is
        // half of what made a stale keyboard dangerous, and CallbackToken alone cannot show the
        // engine uses it.
        var parsedButton = CallbackToken.Parse_OrNull(capturedButton);

        Assert.NotNull(parsedButton);

        // A NONCE, NOT A COUNTER — asserted on the SHAPE rather than on the absence of "opt-1",
        // which is what this line said first and which passed or failed on whether the random nonce
        // happened to begin with a 1. A full-width hex name cannot be produced by a sequence.
        Assert.Equal(CallbackToken.NONCE_HEX_LENGTH, parsedButton.Value.Nonce.Length);
        Assert.True(parsedButton.Value.Nonce.All(Uri.IsHexDigit), $"the payload's name is not a nonce: {capturedButton}");

        var snapshotBeforeTheCrash = _engineState.Load_OrEmpty();

        var tappedGroupId = snapshotBeforeTheCrash.PendingButtons
            .Single(button => button.Data == capturedButton)
            .GroupId;

        // FIVE DECISIONS ARE OUTSTANDING at this point, which is the acceptance criterion's own
        // number: two questions on the phone, two seeded memories that stop the app repeating itself
        // (a nudge already sent, a crash-loop count already reached), and — asserted above, in the
        // only window where it is outstanding — the owner's own wait.
        //
        // COUNTED, not spot-checked, so a partial save cannot pass. Six buttons for two questions:
        // two options each, plus the ONE the app adds to every question — "💬 Let's talk". It was
        // eight until the app's two buttons collapsed into one: "❔ Explain the options" and
        // "💬 Let's talk" became the same gesture the moment a talk tap started closing its
        // question, which is what the owner asked for on 2026-09-09.
        Assert.Equal(2, snapshotBeforeTheCrash.OpenQuestions.Count);
        Assert.Equal(6, snapshotBeforeTheCrash.PendingButtons.Count);
        Assert.Equal(NUDGED_ABOUT, Assert.Contains(NUDGED_MEMBER_KEY, snapshotBeforeTheCrash.NudgedAboutEntry));
        Assert.Equal(2, Assert.Contains(RESPAWN_SLOT_KEY, snapshotBeforeTheCrash.ConsecutiveRespawns));

        // ── kill -9: the process ends with all of that in memory ─────────────────────────────
        // Nothing is disposed and nothing is flushed here, deliberately. A save that only happens on
        // a clean shutdown is exactly the save that is not there when it is needed.

        // ── The second process, on the same store ────────────────────────────────────────────
        var secondTelegram = new CapturingTelegram_Fake();
        var secondEngine = Build_Engine(secondTelegram);

        // /pending is answered by the APP from this state, so it doubles as the assertion that the
        // decisions came back — and as the acceptance test for the command itself.
        secondTelegram.Queue_Updates(Build_OwnerMessageJson("/pending"));

        Assert.True(
            await Run_Until_Async(secondEngine, () => secondTelegram.Has_Sent_Containing(QUESTION_TEXT), 20_000),
            "THE DEFECT: after a restart the bridge no longer knew the owner had been asked anything — "
            + "/pending reported nothing was waiting on them."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // THE TAP THE OWNER ACTUALLY MAKES: the payload minted by the first process, arriving at the
        // second. Before the restore this answered "expired" and the owner's choice was lost.
        secondTelegram.Queue_Updates(Build_CallbackTapJson(capturedButton, questionMessageId));

        Assert.True(
            await Run_Until_Async(secondEngine, () => Read_OwnerChannel(session.OrchId).Contains(FIRST_OPTION, StringComparison.Ordinal), 20_000),
            "THE DEFECT: a button minted before the restart no longer answered anything — the owner "
            + "tapped their choice and it was refused as expired."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        var snapshotAfterTheTap = _engineState.Load_OrEmpty();

        // THE TAP CONSUMED ITS OWN DECISION, in state as well as on screen, so the restored keyboard
        // cannot answer twice. Asserted on the tapped nonce and its question rather than on the
        // collections being empty: the OTHER question is closed a moment later by the same owner
        // message, on the mirror loop's own schedule, and asserting emptiness here would be
        // asserting a race — which passes or fails for a reason that has nothing to do with the
        // property being pinned.
        Assert.DoesNotContain(snapshotAfterTheTap.PendingButtons, button => button.Data == capturedButton);
        Assert.DoesNotContain(snapshotAfterTheTap.PendingButtons, button => button.GroupId == tappedGroupId);
        Assert.DoesNotContain(snapshotAfterTheTap.OpenQuestions, question => question.MessageId == questionMessageId);

        // AND THE TWO SILENT MEMORIES SURVIVED. Both are seeded rather than produced, so what this
        // asserts is the restore path: a fresh engine that dropped either would rewrite the snapshot
        // without it on its very first save, which the tap above has just forced.
        Assert.Equal(NUDGED_ABOUT, Assert.Contains(NUDGED_MEMBER_KEY, snapshotAfterTheTap.NudgedAboutEntry));
        Assert.Equal(2, Assert.Contains(RESPAWN_SLOT_KEY, snapshotAfterTheTap.ConsecutiveRespawns));
    }

    IBridgeEngine Build_Engine(ITelegramApiClient telegram)
    {
        return BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, telegram,
            _engineState, _clock,
            BridgeTestTiming.Fast());
    }

    string Read_OwnerChannel(string orchId) => File.ReadAllText(_paths.Get_OwnerChannelFile(orchId));

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    void Append_SupervisorQuestion(string orchId, int index, string question, string firstOption, string secondOption)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(
            channelFile,
            $"\n## [{index}] FROM supervisor — {stamp} — a question\n"
            // RECOMMEND/RISK/ROW are what OwnerQuestion_Contract requires of every question the app
            // will forward; `RISK: low` keeps the high-risk classification coming from the patterns,
            // which is what these tests are about.
            + $"QUESTION: {question}\nOPTION: {firstOption}\nOPTION: {secondOption}\n"
            + "RECOMMEND: whichever you prefer — this fixture takes no view.\nRISK: low\nROW: none\n");
    }

    static string Build_OwnerMessageJson(string text)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":2001,\"message\":{\"message_id\":77,"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    static string Build_CallbackTapJson(string callbackData, long questionMessageId)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":2002,\"callback_query\":{\"id\":\"cbq-1\","
            + $"\"data\":\"{callbackData}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{questionMessageId},\"message_thread_id\":{TOPIC_ID},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}]}}";
    }

    /// <summary>
    /// Runs the engine's real loops until the condition holds or the budget is spent, then stops
    /// them — the same shape OwnerAnswerSurvivesFailedSendTests uses, and for the same reason: the
    /// defect lives in the loops, so the loops are what has to run.
    /// </summary>
    static async Task<bool> Run_Until_Async(IBridgeEngine engine, Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);
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

/// <summary>
/// A clock the test moves. Only the deadline sweep and the dispatcher pause read it — see
/// <see cref="IClock"/> for why it is deliberately not wired to the engine's other clock reads.
/// </summary>
internal sealed class FixedClock_Fake(DateTime utcNow) : IClock
{
    readonly object _lock = new();
    DateTime _utcNow = utcNow;

    public DateTime UtcNow
    {
        get
        {
            lock (_lock)
                return _utcNow;
        }
    }

    public void Advance(TimeSpan span)
    {
        lock (_lock)
            _utcNow += span;
    }
}

/// <summary>
/// A Telegram client that records what was sent and, crucially, WHAT THE BUTTONS CARRY — the
/// callback payloads are the subject of the restart test and are not observable any other way.
/// </summary>
internal sealed class CapturingTelegram_Fake : ITelegramApiClient
{

    // The startup handshake (see ITelegramApiClient): a fake not testing it answers with a name and
    // a cleared webhook, so the inbound loop starts exactly as it does in production.
    public Task<string> Get_BotUsername_Async(CancellationToken cancellationToken) => Task.FromResult("test_bot");

    public Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken) => Task.CompletedTask;
    // The typing bubble is not this probe's subject; it creates no message, so it is not recorded.
    public Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    readonly object _lock = new();
    readonly List<string> _sentTexts = [];
    readonly List<string> _editedTexts = [];
    readonly List<(string Data, string Label)> _buttons = [];
    string? _queuedUpdatesJson;
    long _nextMessageId = 9000;
    readonly List<(long MessageId, string Text)> _sentWithIds = [];
    readonly Dictionary<long, int> _editAttemptsByMessageId = [];
    int _editsToRefuse;
    int _refusalRetryAfterSeconds;
    long? _refuseOnlyMessageId;

    /// <summary>
    /// FAULT INJECTION: the next <paramref name="count"/> text edits of <paramref name="messageId"/>
    /// (or of any message when null) answer Telegram's rate limit with the given <c>retry_after</c>;
    /// <c>int.MaxValue</c> refuses them all. A refused text is NOT recorded as edited — only an edit
    /// Telegram accepted is. Scoped to one message on purpose: the status line edits too, and a
    /// budget it could spend would let a probe about the tap's rewrite pass without the retry.
    /// </summary>
    public void Refuse_Edits_WithRateLimit(int count, int retryAfterSeconds, long? messageId = null)
    {
        lock (_lock)
        {
            _editsToRefuse = count;
            _refusalRetryAfterSeconds = retryAfterSeconds;
            _refuseOnlyMessageId = messageId;
        }
    }

    /// <summary>Every text edit attempted on this message, accepted or refused.</summary>
    public int Count_EditAttempts(long messageId)
    {
        lock (_lock)
            return _editAttemptsByMessageId.TryGetValue(messageId, out var attempts) ? attempts : 0;
    }

    /// <summary>The id Telegram (this fake) gave the LAST sent message containing the fragment.</summary>
    public long? Find_MessageIdOfSentContaining(string fragment)
    {
        lock (_lock)
        {
            var found = _sentWithIds.LastOrDefault(sent => sent.Text.Contains(fragment, StringComparison.Ordinal));
            return found.Text == null ? null : found.MessageId;
        }
    }

    /// <summary>
    /// The id of the last message sent carrying DECISION buttons — the one a tap refers back to.
    ///
    /// <para>
    /// "Any message with buttons" was the first version of this and it was wrong in a way that made
    /// a probe pass while testing nothing: the topic's standing command bar
    /// (<see cref="TopicCommandButtons"/>) is also a message with an inline keyboard, and it is sent
    /// at startup — so a test waiting for "a question appeared" was satisfied by the shortcut bar
    /// before the question had been written. Keyed on the option-payload prefix instead.
    /// </para>
    /// </summary>
    public long? LastButtonMessageId { get; private set; }

    public void Queue_Updates(string updatesJson)
    {
        lock (_lock)
            _queuedUpdatesJson = updatesJson;
    }

    public bool Has_Sent_Containing(string fragment)
    {
        lock (_lock)
            return _sentTexts.Any(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// EDITS ARE KEPT SEPARATELY FROM SENDS, because the difference is the subject of two rules
    /// here: a reminder and a recorded outcome must EDIT the question already on screen (decision
    /// 14), never arrive as a new message. A single list would make "it was edited" and "a second
    /// message was sent" indistinguishable, which is exactly the confusion being tested against.
    /// </summary>
    public bool Has_Edited_Containing(string fragment)
    {
        lock (_lock)
            return _editedTexts.Any(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>Everything sent, for an assertion message — a probe that fails silently proves nothing.</summary>
    public string Dump_Sent()
    {
        lock (_lock)
            return string.Join($"{Environment.NewLine}--- sent ---{Environment.NewLine}", _sentTexts);
    }

    public int Count_Sent_Containing(string fragment)
    {
        lock (_lock)
            return _sentTexts.Count(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    public string? Find_SentContaining(string fragment)
    {
        lock (_lock)
            return _sentTexts.LastOrDefault(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    public int Count_Edited_Containing(string fragment)
    {
        lock (_lock)
            return _editedTexts.Count(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    public string? Find_EditedContaining(string fragment)
    {
        lock (_lock)
            return _editedTexts.LastOrDefault(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>The callback payload behind the button whose label carries this text, or null.</summary>
    public string? Find_ButtonFor(string labelFragment)
    {
        lock (_lock)
            return _buttons.FirstOrDefault(button => button.Label.Contains(labelFragment, StringComparison.Ordinal)).Data;
    }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Task.FromResult(Record(text));
    }

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Task.FromResult(Record(html));
    }

    // See the note in FailableTelegram_Fake: agent prose leaves as HTML, so the rendered calls have
    // to be recorded by the same hands as the plain ones or a probe sees an empty topic.
    public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Send_MessageWithButtons_Async(messageThreadId, html, buttons, sound, cancellationToken);
    }

    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken)
    {
        return Edit_MessageText_Async(messageId, html, cancellationToken);
    }

    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _buttons.AddRange(buttons);
            _sentTexts.Add(text);

            var messageId = _nextMessageId++;
            _sentWithIds.Add((messageId, text));

            if (buttons.Any(button => button.Data.StartsWith(CallbackToken.PREFIX, StringComparison.Ordinal)))
                LastButtonMessageId = messageId;

            return Task.FromResult<long?>(messageId);
        }
    }

    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Send_MessageWithButtons_Async(messageThreadId, text, [.. buttonRows.SelectMany(row => row)], sound, cancellationToken);
    }

    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        string? queued;

        lock (_lock)
        {
            queued = _queuedUpdatesJson;
            _queuedUpdatesJson = null;
        }

        if (queued != null)
            return queued;

        // The real call is a long poll; returning instantly would spin this loop at full speed and
        // starve the mirror loop it shares a machine with.
        await Task.Delay(200, cancellationToken);
        return EMPTY_UPDATES;
    }

    long? Record(string text)
    {
        lock (_lock)
        {
            _sentTexts.Add(text);
            var messageId = _nextMessageId++;
            _sentWithIds.Add((messageId, text));
            return messageId;
        }
    }

    int Count_EditAttempts_Unlocked(long messageId)
    {
        return _editAttemptsByMessageId.TryGetValue(messageId, out var attempts) ? attempts : 0;
    }

    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken) => Task.FromResult(1L);
    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
    {
        return Attempt_Edit(messageId, text, recordText: true);
    }

    /// <param name="recordText">
    /// Whether an ACCEPTED edit lands in the edited-texts the probes search. The status line's
    /// edits carry the command bar and are NOT recorded — a probe asking "was the second question
    /// stamped" must not find that question's words inside PULSE's "waiting on you" field.
    /// </param>
    Task Attempt_Edit(long messageId, string text, bool recordText)
    {
        lock (_lock)
        {
            _editAttemptsByMessageId[messageId] = Count_EditAttempts_Unlocked(messageId) + 1;

            if (_editsToRefuse > 0 && (_refuseOnlyMessageId == null || _refuseOnlyMessageId == messageId))
            {
                if (_editsToRefuse != int.MaxValue)
                    _editsToRefuse--;

                // Telegram's own shape, which the client would have thrown after its short inline retry.
                throw new TelegramApiException(
                    429,
                    $"Telegram 'editMessageText' failed with HTTP 429: {{\"ok\":false,\"error_code\":429,\"description\":\"Too Many Requests: retry after {_refusalRetryAfterSeconds}\",\"parameters\":{{\"retry_after\":{_refusalRetryAfterSeconds}}}}}",
                    _refusalRetryAfterSeconds);
            }

            if (recordText)
                _editedTexts.Add(text);
        }

        return Task.CompletedTask;
    }

    // THE SAME HANDS AS THE PLAIN EDIT: the status line is edited WITH its command bar, so a probe
    // about how often that line is retried has to see (and be able to refuse) this call too.
    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken)
        => Attempt_Edit(messageId, text, recordText: false);

    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
        => Attempt_Edit(messageId, text, recordText: false);
    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
}
