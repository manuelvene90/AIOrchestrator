using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// R1 — THE OWNER'S ANSWER MUST SURVIVE A FAILED SEND.
///
/// The owner asks something; the supervisor answers; the Telegram send fails. The append is left
/// UNCONFIRMED on purpose so the tailer re-emits it — that retry is the whole safety net. But the
/// "the owner is waiting for an answer" flag was consumed BEFORE the send, so on the re-emission the
/// very same entry re-evaluated as ordinary narration and `OwnerPush_Policy` suppressed it. The
/// answer to a question the owner actually asked was dropped silently, with nothing anywhere saying
/// so. The owner reported exactly this once.
///
/// WHY THIS TEST IS SHAPED THE EXPENSIVE WAY. Two constraints in the engine leave no cheaper honest
/// option, and both were verified in the source before this file was written:
///
///   - `Mirror_Append_Async` returns early when there is no Telegram client, ABOVE the code under
///     test. A file-only harness — which every other engine test uses — cannot reach this defect at
///     all, so the send must come from a fake client that can be made to fail.
///   - `Is_MirrorAttemptDue` holds a failed channel back for MIRROR_RETRY_BACKOFF_SECONDS (30), and
///     the waiting flag is per-instance in-memory state. So both attempts must happen on the SAME
///     engine, 30 s apart. A fresh engine starts with an empty flag set and would go red with or
///     without the fix — a green that pins nothing.
///
/// THE ANSWER TEXT CARRIES NO QUESTION MARK, NO QUESTION:/OPTION: MARKER AND NO BLOCKED MARKER, on
/// purpose: under `OwnerPush_Policy.Should_Push` that leaves exactly ONE route to it being pushed —
/// the owner waiting. An answer that would push on its own merits would keep this test green with the
/// flag cleared, which is the "two routes to the asserted state" trap.
/// </summary>
public class OwnerAnswerSurvivesFailedSendTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    /// <summary>The engine holds a failed channel for 30 s; the margin is for a loaded machine.</summary>
    const int RETRY_BACKOFF_WAIT_MILLISECONDS = 33_000;

    /// <summary>Distinctive, and deliberately free of anything that would push on its own merits.</summary>
    const string ANSWER_TEXT = "Yes. The rebuild finished and the branch is clean.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    public OwnerAnswerSurvivesFailedSendTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-r1-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // The inbound loop reads the chat and owner ids and throws without them. The Italian layer is
        // pinned OFF because it defaults ON and would hand the owner's text to the real translator.
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"telegramItalianLayer\":false}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _log = new RecordingLog_Fake();
        _telegram = new FailableTelegram_Fake();

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AFailedSend_LeavesTheOwnerStillWaiting_SoTheAnswerIsPushedOnTheReEmission()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        // The tailer registers a file it has never seen at its CURRENT END and emits nothing for it,
        // so the channel has to exist before the first poll — otherwise the answer appended below is
        // behind the starting offset and is never mirrored at all.
        Seed_OwnerChannel(session.OrchId);

        // 1 — the owner asks. This is the only thing that raises the waiting flag.
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson("is the rebuild done"));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Owner message buffered"), 10_000),
            "the owner's message never reached the router, so the waiting flag was never raised");

        // 2 — the supervisor answers, and the send fails.
        Append_SupervisorAnswer(session.OrchId);
        _telegram.Fail_Sends_Containing(ANSWER_TEXT);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Attempted_Containing(ANSWER_TEXT), 15_000),
            "the answer was never even attempted, so this test never reached the code it is about."
            + $"{Environment.NewLine}Owner channel:{Environment.NewLine}"
            + File.ReadAllText(_paths.Get_OwnerChannelFile(session.OrchId))
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        Assert.False(_telegram.Has_Sent_Containing(ANSWER_TEXT), "the send was supposed to fail");

        // 3 — the retry. The tailer re-emits the unconfirmed append; the engine holds the channel for
        // MIRROR_RETRY_BACKOFF_SECONDS first, which is what this wait is buying.
        await Task.Delay(RETRY_BACKOFF_WAIT_MILLISECONDS);
        _telegram.Succeed_All_Sends();

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(ANSWER_TEXT), 20_000),
            "THE DEFECT: the failed send consumed the owner's wait, so on the re-emission the answer "
            + "re-evaluated as narration and was suppressed — the owner never got the answer to the "
            + "question they asked");
    }

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    /// <summary>
    /// A supervisor entry that says nothing ask-shaped, so the ONLY thing that can push it is the
    /// owner waiting for it.
    /// </summary>
    void Append_SupervisorAnswer(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(channelFile, $"\n## [1] FROM supervisor — {stamp} — the answer\n{ANSWER_TEXT}\n");
    }

    static string Build_OwnerMessageJson(string text)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":1001,\"message\":{\"message_id\":77,"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    /// <summary>
    /// Runs the real engine loops until the condition holds, then stops them. The engine is driven
    /// rather than poked because the state under test — the waiting flag — has no getter: its only
    /// reader is the mirror path's push decision.
    /// </summary>
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

/// <summary>
/// A Telegram client that can be switched between delivering and failing, and that hands the inbound
/// loop one canned update. It records ATTEMPTS separately from successful SENDS — the difference
/// between the two is the whole subject of this test.
/// </summary>
internal sealed class FailableTelegram_Fake : ITelegramApiClient
{
    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    readonly object _lock = new();
    readonly List<string> _attemptedTexts = [];
    readonly List<string> _sentTexts = [];
    string? _queuedUpdatesJson;
    string? _failFragment;
    long _nextMessageId = 9000;

    public void Queue_OwnerMessage(string updatesJson)
    {
        lock (_lock)
            _queuedUpdatesJson = updatesJson;
    }

    /// <summary>
    /// Fails ONLY the send carrying this text. Failing everything would be a worse model of the
    /// outage, not a better one: the mirror stops an append at its FIRST failure, and the engine
    /// writes its own entries (a STATUS line) into the owner channel ahead of the supervisor's, so a
    /// blanket failure never reaches the answer at all.
    /// </summary>
    public void Fail_Sends_Containing(string fragment)
    {
        lock (_lock)
            _failFragment = fragment;
    }

    public void Succeed_All_Sends()
    {
        lock (_lock)
            _failFragment = null;
    }

    public bool Has_Attempted_Containing(string fragment)
    {
        lock (_lock)
            return _attemptedTexts.Any(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    public bool Has_Sent_Containing(string fragment)
    {
        lock (_lock)
            return _sentTexts.Any(text => text.Contains(fragment, StringComparison.Ordinal));
    }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, CancellationToken cancellationToken)
    {
        return Task.FromResult<long?>(Record_AndMaybeFail(text));
    }

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, CancellationToken cancellationToken)
    {
        return Task.FromResult<long?>(Record_AndMaybeFail(html));
    }

    public Task<long?> Send_MessageWithButtons_Async(
        long? messageThreadId,
        string text,
        IReadOnlyList<(string Data, string Label)> buttons,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<long?>(Record_AndMaybeFail(text));
    }

    long Record_AndMaybeFail(string text)
    {
        lock (_lock)
        {
            _attemptedTexts.Add(text);

            if (_failFragment != null && text.Contains(_failFragment, StringComparison.Ordinal))
                throw new Exception($"Simulated Telegram outage while sending: {text}");

            _sentTexts.Add(text);

            return _nextMessageId++;
        }
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

        // Stands in for the long poll. Without it this loop spins hot for the whole run.
        await Task.Delay(50, cancellationToken);

        return EMPTY_UPDATES;
    }

    public Task<long> Create_ForumTopic_Async(string topicName, CancellationToken cancellationToken)
    {
        return Task.FromResult(7777L);
    }

    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Send_Photo_Async(long? messageThreadId, string filePath, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Array.Empty<byte>());
    }
}

/// <summary>Captures the log lines the test uses as a progress probe.</summary>
internal sealed class RecordingLog_Fake : IOrchestrationLog
{
    readonly object _lock = new();
    readonly List<string> _infos = [];
    readonly List<string> _allLines = [];

    public bool Has_Info_Containing(string fragment)
    {
        lock (_lock)
            return _infos.Any(message => message.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// Everything the engine said, for the assertion messages. A test that drives real loops fails
    /// for a dozen reasons that all look identical from outside; the engine's own log is the only
    /// thing that distinguishes "the defect" from "the setup never got there".
    /// </summary>
    public string Dump()
    {
        lock (_lock)
            return string.Join(Environment.NewLine, _allLines);
    }

    public void Log_Info(string orchId, string message)
    {
        lock (_lock)
        {
            _infos.Add(message);
            _allLines.Add($"INFO  [{orchId}] {message}");
        }
    }

    public void Log_Warning(string orchId, string message)
    {
        lock (_lock)
            _allLines.Add($"WARN  [{orchId}] {message}");
    }

    public void Log_Error(string orchId, string message, Exception? exception)
    {
        lock (_lock)
            _allLines.Add($"ERROR [{orchId}] {message} :: {exception?.Message}");
    }

    public event Action<IOrchestrationLogEntry>? EntryLogged
    {
        add { }
        remove { }
    }
}
