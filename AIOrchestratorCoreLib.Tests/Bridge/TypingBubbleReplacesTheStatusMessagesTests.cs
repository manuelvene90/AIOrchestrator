using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE OWNER'S MESSAGE IS ANSWERED BY ONE MESSAGE, NOT THREE (owner, 2026-09-07). Every exchange on the
/// phone arrived as "✓✓ · thinking…", then "✓✓ · done for now — turn ended", then the actual answer:
/// two status messages per real one. What every chat app does instead is the typing bubble — a chat
/// action, not a message — and the answer itself is the completion signal.
///
/// Two cases, each pinned against the engine's REAL loops with a fake client that records the two
/// things this is about separately: the messages that reach the topic and the typing actions that do
/// not. Both went red on the code they replace, which is what makes them evidence rather than décor.
/// </summary>
public class TypingBubbleReplacesTheStatusMessagesTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4343;

    /// <summary>Distinctive, and free of anything that would push on its own merits: only the owner's wait pushes it.</summary>
    const string ANSWER_TEXT = "Yes. The rebuild finished and the branch is clean.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly TypingRecordingTelegram_Fake _telegram;
    readonly TypingProbeLog_Fake _log;

    public TypingBubbleReplacesTheStatusMessagesTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-typing-tests-{Guid.NewGuid():N}");
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
        _log = new TypingProbeLog_Fake();
        _telegram = new TypingRecordingTelegram_Fake();

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// The session is mid-turn when the owner's message lands. The old receipt wrote "thinking…" onto
    /// the tick; now the bubble carries the wait, refreshed while the turn runs, and no message says it.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WhileTheSessionWorks_TheOwnerSeesTheTypingBubble_AndNoMessageSaysThinking()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();
        Mark_SessionMidTurn(orchId);

        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson("how is the rebuild going"));

        Assert.True(
            await Run_Until_Async(() => _telegram.Count_TypingActions() >= 2, 25_000),
            $"the typing bubble was never refreshed while the session stayed busy.{Environment.NewLine}{_log.Dump()}");

        Assert.False(
            _telegram.Has_Sent_Containing("thinking"),
            $"a message still says 'thinking' — that is the bubble's job now.{Environment.NewLine}{_telegram.Dump_Sent()}");
    }

    /// <summary>
    /// The session answers and goes quiet. The answer IS the completion; a "done for now — turn ended"
    /// message after it told the owner nothing they were not already reading.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WhenTheAnswerArrives_NothingFollowsIt()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson("is the rebuild done"));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Owner message delivered"), 15_000),
            $"the owner's message was never delivered, so this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        Append_SupervisorEntry(orchId, 1, "the answer", ANSWER_TEXT);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(ANSWER_TEXT), 15_000),
            $"the answer never reached the phone, so this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        // The turn-ended resolver runs on the mirror tick AFTER the answer is counted; give it several.
        await Run_For_Async(BridgeTestTiming.Window_ForTicks(10));

        Assert.False(
            _telegram.Has_Sent_Containing("turn ended") || _telegram.Has_Sent_Containing("done for now"),
            $"a status message followed the answer.{Environment.NewLine}{_telegram.Dump_Sent()}");

        Assert.False(
            _telegram.Has_Sent_Containing("thinking"),
            $"a message still says 'thinking'.{Environment.NewLine}{_telegram.Dump_Sent()}");
    }

    /// <summary>
    /// A usage file with a fresh mtime and no transcript path is how <c>SessionActivity_Probe</c>
    /// reads "working right now" — the same fixture <c>SessionActivityProbeTests</c> uses.
    /// </summary>
    void Mark_SessionMidTurn(string orchId)
    {
        var usageFile = OwnerFacingSession_Locator.Get_UsageFile(_paths, orchId, _store.Get_Session_OrNull(orchId));
        Directory.CreateDirectory(Path.GetDirectoryName(usageFile)!);
        File.WriteAllText(usageFile, """{"version":"2.1.100"}""");
    }

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    /// <summary>An unseen channel is registered at its current end; one short run baselines it first.</summary>
    async Task<string> Start_WithChannelAlreadySeen_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(3));

        return session.OrchId;
    }

    void Append_SupervisorEntry(string orchId, int index, string subject, string body)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(channelFile, $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
    }

    static string Build_OwnerMessageJson(string text)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":1001,\"message\":{\"message_id\":77,"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    async Task Run_For_Async(int milliseconds)
    {
        await Run_Until_Async(() => false, milliseconds);
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

/// <summary>
/// Records what reaches the topic (sends AND edits — an edit that writes "thinking…" onto the tick is
/// still the owner reading "thinking…") separately from the typing actions, which reach no message.
/// </summary>
internal sealed class TypingRecordingTelegram_Fake : ITelegramApiClient
{

    // The startup handshake (see ITelegramApiClient): a fake not testing it answers with a name and
    // a cleared webhook, so the inbound loop starts exactly as it does in production.
    public Task<string> Get_BotUsername_Async(CancellationToken cancellationToken) => Task.FromResult("test_bot");

    public Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken) => Task.CompletedTask;
    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    readonly object _lock = new();
    readonly List<string> _shownTexts = [];
    string? _queuedUpdatesJson;
    int _typingActions;
    long _nextMessageId = 9000;

    public void Queue_OwnerMessage(string updatesJson)
    {
        lock (_lock)
            _queuedUpdatesJson = updatesJson;
    }

    public int Count_TypingActions()
    {
        lock (_lock)
            return _typingActions;
    }

    public bool Has_Sent_Containing(string fragment)
    {
        lock (_lock)
            return _shownTexts.Any(text => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    public string Dump_Sent()
    {
        lock (_lock)
            return string.Join(Environment.NewLine, _shownTexts.Select(text => $"  · {text}"));
    }

    public Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken)
    {
        lock (_lock)
            _typingActions++;

        return Task.CompletedTask;
    }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Task.FromResult<long?>(Record(text));
    }

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Task.FromResult<long?>(Record(html));
    }

    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Task.FromResult<long?>(Record(text));
    }

    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Task.FromResult<long?>(Record(text));
    }

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
    {
        Record(text);
        return Task.CompletedTask;
    }

    public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Task.FromResult<long?>(Record(html));
    }

    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken)
    {
        Record(html);
        return Task.CompletedTask;
    }

    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken)
    {
        Record(text);
        return Task.CompletedTask;
    }

    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
    {
        Record(text);
        return Task.CompletedTask;
    }

    long Record(string text)
    {
        lock (_lock)
        {
            _shownTexts.Add(text);
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

        await Task.Delay(50, cancellationToken);

        return EMPTY_UPDATES;
    }

    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken)
    {
        return Task.FromResult(7777L);
    }

    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken)
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

    public Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken) => Task.CompletedTask;


    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Array.Empty<byte>());
    }
}

/// <summary>Captures the engine's log — the only thing that tells a defect from a setup that never got there.</summary>
internal sealed class TypingProbeLog_Fake : IOrchestrationLog
{
    readonly object _lock = new();
    readonly List<string> _infos = [];
    readonly List<string> _allLines = [];

    public bool Has_Info_Containing(string fragment)
    {
        lock (_lock)
            return _infos.Any(message => message.Contains(fragment, StringComparison.Ordinal));
    }

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
