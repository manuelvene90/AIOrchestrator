using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;
using Xunit.Abstractions;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE OWNER ANSWERED BY TYPING INSTEAD OF TAPPING, AND THE QUESTION NEVER CLOSED.
///
/// Owner, 2026-08-24: *"In a topic I received a multiple-choice question, but I answered with a
/// message because I needed to give a more detailed response. And I think that's what made the
/// question mark persist."*
///
/// Two different facts hide behind that one sentence and they need separate pins, because one of
/// them is innocent:
///
///   - the QUESTION GLYPH is driven by OwnerQuestionPending_Decider, which clears on ANY owner
///     entry. A typed answer clears it exactly like a tap, and replaying the real channels through
///     that decider confirms it. That half must STAY true, so it is pinned here rather than assumed;
///   - the QUESTION MESSAGE ITSELF is only ever closed on the tap path. Handle_CallbackTap_Async
///     rewrites the text and drops the keyboard; a typed answer runs Route_OwnerMessage_Async and
///     touches neither. So the buttons stay live under an answered question, and a later stray tap
///     re-enters the tap handler and injects the tapped label as a SECOND owner message —
///     contradicting the answer the owner actually gave.
///
/// The engine is driven end to end rather than poked because neither fact has a getter: the glyph is
/// only observable as the name handed to editForumTopic, and the cleanup only as the calls made
/// against the question's message id.
/// </summary>
public class TypedAnswerClearsTheQuestionProbeTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    const string DISPLAY_NAME = "IS · capital injection";

    /// <summary>Carries the markers, so the mirror turns it into a button message.</summary>
    const string QUESTION_BODY =
        "QUESTION: What starts the N-year clock?\nOPTION: Reserve idle\nOPTION: Per-deposit\nOPTION: Pick later\n"
        + "RECOMMEND: Reserve idle — it is the one the ledger already assumes.\nRISK: low\nROW: none";

    /// <summary>
    /// The detailed reply the owner said they needed to write. Deliberately free of any question mark
    /// so it cannot re-light the glyph on its own merits and hand back a false green.
    /// </summary>
    const string TYPED_ANSWER = "at 5 years maximum since the last time you were 100 percent invested";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly RecordingTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;
    readonly ITestOutputHelper _out;

    public TypedAnswerClearsTheQuestionProbeTests(ITestOutputHelper output)
    {
        _out = output;

        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-typedanswer-{Guid.NewGuid():N}");
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
        _log = new RecordingLog_Fake();
        _telegram = new RecordingTelegram_Fake();

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
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
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATypedAnswer_ClearsTheGlyph_AndTakesTheButtonsDown()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        // 1 — the session asks, with options. This is what puts the glyph on the topic and the
        // buttons on the phone.
        Append_SupervisorEntry(orchId, 1, "one question", QUESTION_BODY);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_ButtonMessage(), 20_000),
            $"the question never became a button message, so this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        Assert.True(
            await Run_Until_Async(() => Glyph_IsOn(), 20_000),
            "the glyph was never applied while a question was genuinely open, so the clear below "
            + $"would prove nothing.{Environment.NewLine}names: {_telegram.Dump_TopicNames()}");

        var questionMessageId = _telegram.ButtonMessageId_OrNull();

        // 2 — the owner types a detailed reply instead of tapping.
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(TYPED_ANSWER));

        Assert.True(
            await Run_Until_Async(() => Owner_ChannelContains(orchId, TYPED_ANSWER), 20_000),
            $"the typed answer never reached the owner channel.{Environment.NewLine}{_log.Dump()}");

        // 3 — THE GLYPH. This half is expected to hold: any owner entry clears the decider.
        var cleared = await Run_Until_Async(() => !Glyph_IsOn(), 20_000);

        _out.WriteLine($"topic names pushed: {_telegram.Dump_TopicNames()}");
        _out.WriteLine($"question message id: {questionMessageId}");
        _out.WriteLine($"buttons removed for: {_telegram.Dump_ButtonRemovals()}");
        _out.WriteLine($"messages edited: {_telegram.Dump_EditedMessageIds()}");

        Assert.True(
            cleared,
            "THE GLYPH STUCK: the owner answered by typing and the topic still carries the question "
            + $"glyph. names: {_telegram.Dump_TopicNames()}");

        // 4 — THE CLEANUP. The tap path rewrites the question and drops the keyboard; the typed path
        // does neither, which is the defect this probe exists to pin.
        Assert.True(questionMessageId != null, "the question message had no id, so nothing below can be checked");

        Assert.True(
            _telegram.Buttons_WereRemovedFor(questionMessageId!.Value),
            "THE BUTTONS STAYED LIVE: the owner answered in writing and the multiple-choice keyboard "
            + "was never taken down, so the answered question is still tappable and a later tap would "
            + $"inject a second, contradictory answer.{Environment.NewLine}"
            + $"removals: {_telegram.Dump_ButtonRemovals()} edits: {_telegram.Dump_EditedMessageIds()}");

        // 5 — AND THE RECORD. Taking the keyboard down is not enough on its own: the owner scrolls
        // back to the question, and without the tick under it, it still reads as unanswered. That is
        // the half they reported, so it gets its own assertion rather than riding on the removal.
        var record = _telegram.EditedTextFor_OrNull(questionMessageId.Value);

        Assert.True(
            record != null && record.Contains("✅", StringComparison.Ordinal),
            "NO RECORD: the question was closed but nothing on it says so, so scrolling back shows a "
            + $"question that still reads as open.{Environment.NewLine}text: {record}");

        Assert.Contains(TYPED_ANSWER, record!, StringComparison.Ordinal);
    }

    bool Glyph_IsOn()
    {
        var name = _telegram.Last_TopicName_OrNull();

        return name != null && name.StartsWith(TelegramDeliveryMode_Glyphs.REPLY_WANTED, StringComparison.Ordinal);
    }

    bool Owner_ChannelContains(string orchId, string fragment)
    {
        var file = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(file))
            return false;

        try
        {
            return File.ReadAllText(file).Contains(fragment, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    /// <summary>
    /// The tailer registers a file it has never seen at its CURRENT END, so an entry appended before
    /// the first poll is behind the starting offset and never mirrors — a setup mistake that looks
    /// exactly like the defect under test. One short run baselines the file first.
    /// </summary>
    async Task<string> Start_WithChannelAlreadySeen_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, DISPLAY_NAME);
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
/// Records the two things this probe is about and nothing else: every name handed to
/// editForumTopic, and every call made against a message id that was sent with buttons.
/// </summary>
internal sealed class RecordingTelegram_Fake : ITelegramApiClient
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
    readonly List<string> _topicNames = [];
    readonly List<long> _buttonRemovals = [];
    readonly List<long> _editedMessageIds = [];
    readonly Dictionary<long, string> _editedTextByMessageId = [];
    long? _buttonMessageId;
    string? _queuedUpdatesJson;
    long _nextMessageId = 9000;

    public void Queue_OwnerMessage(string updatesJson)
    {
        lock (_lock)
            _queuedUpdatesJson = updatesJson;
    }

    public bool Has_ButtonMessage()
    {
        lock (_lock)
            return _buttonMessageId != null;
    }

    public long? ButtonMessageId_OrNull()
    {
        lock (_lock)
            return _buttonMessageId;
    }

    public string? Last_TopicName_OrNull()
    {
        lock (_lock)
            return _topicNames.Count == 0 ? null : _topicNames[^1];
    }

    public bool Buttons_WereRemovedFor(long messageId)
    {
        lock (_lock)
            return _buttonRemovals.Contains(messageId) || _editedMessageIds.Contains(messageId);
    }

    public string Dump_TopicNames()
    {
        lock (_lock)
            return string.Join(" | ", _topicNames);
    }

    public string Dump_ButtonRemovals()
    {
        lock (_lock)
            return string.Join(",", _buttonRemovals);
    }

    public string Dump_EditedMessageIds()
    {
        lock (_lock)
            return string.Join(",", _editedMessageIds);
    }

    readonly List<string> _sentTexts = [];

    public IReadOnlyList<string> SentTexts()
    {
        lock (_lock)
            return [.. _sentTexts];
    }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _sentTexts.Add(text);

            return Task.FromResult<long?>(_nextMessageId++);
        }
    }

    // RECORDED SINCE 2026-09-07, when the mirror started sending every entry as HTML. It used to
    // ignore this call because only ASCII mockups came through it; leaving it blind now would hide
    // the whole conversation from a probe that counts what reached the phone.
    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Send_Message_Async(messageThreadId, html, sound, cancellationToken);
    }

    public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Send_MessageWithButtons_Async(messageThreadId, html, buttons, sound, cancellationToken);
    }

    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken)
    {
        return Edit_MessageText_Async(messageId, html, cancellationToken);
    }

    // The General topic's name is not this probe's subject; accepting it silently keeps the rename
    // from showing up as traffic in whatever this fake counts.
    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<long?> Send_MessageWithButtons_Async(
        long? messageThreadId,
        string text,
        IReadOnlyList<(string Data, string Label)> buttons,
        TelegramSendSounds sound,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var id = _nextMessageId++;
            _buttonMessageId ??= id;

            return Task.FromResult<long?>(id);
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

    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken)
    {
        return Task.FromResult(7777L);
    }

    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken)
    {
        lock (_lock)
            _topicNames.Add(newName);

        return Task.CompletedTask;
    }

    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public string? EditedTextFor_OrNull(long messageId)
    {
        lock (_lock)
            return _editedTextByMessageId.TryGetValue(messageId, out var text) ? text : null;
    }

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _editedMessageIds.Add(messageId);
            _editedTextByMessageId[messageId] = text;
        }

        return Task.CompletedTask;
    }

    // THE ROW-AWARE PAIR IS THE STATUS LINE, NOT A DECISION KEYBOARD, so it delegates to the PLAIN
    // send and edit — which is exactly what the status line called before it started carrying the
    // owner's standing command bar. Routing it to the button-recording pair instead made the status
    // line look like a question: this probe's ButtonMessageId_OrNull then returned the status
    // message's id and the run failed claiming the question's keyboard was never taken down.
    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
    {
        return Edit_MessageText_Async(messageId, text, cancellationToken);
    }

    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Send_Message_Async(messageThreadId, text, sound, cancellationToken);
    }

    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken)
    {
        lock (_lock)
            _editedMessageIds.Add(messageId);

        return Task.CompletedTask;
    }

    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken)
    {
        lock (_lock)
            _buttonRemovals.Add(messageId);

        return Task.CompletedTask;
    }

    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken) => Task.CompletedTask;


    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
}
