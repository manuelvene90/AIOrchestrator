using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// WHAT RINGS THE OWNER'S PHONE, AND WHAT DOES NOT — driven through the real engine.
///
/// <para>
/// THE OWNER'S REQUEST, 2026-09-09: *"Half of what reaches my phone is not for me — STATUS every 30
/// minutes, false 'waiting on your reply' alerts, the same receipt sentence every time... Make the
/// noise stop. If the supervisor writes to me, I must know it — that rings. Status, receipts and app
/// bookkeeping do not ring."*
/// </para>
/// <para>
/// Every claim here is about what ARRIVES: whether it arrives at all, whether it makes a sound, and
/// whether it arrives rendered. None of that is visible to a unit test of a policy — the fake client
/// records the notification flag on each send, which is the only place the answer exists.
/// </para>
/// </summary>
public class ThePhoneRingsOnlyForTheSupervisorTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 6161;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly SoundRecordingTelegram_Fake _telegram = new();
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);
    readonly IBridgeEngine _engine;

    CancellationTokenSource? _runCancellation;
    Task? _runLoop;

    public ThePhoneRingsOnlyForTheSupervisorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-rings-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);

        _engine = BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, _telegram, _engineState, _clock, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        if (_runCancellation != null && _runLoop != null)
        {
            Stop_Async(_runCancellation, _runLoop).GetAwaiter().GetResult();
            _runCancellation.Dispose();
        }

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

    /// <summary>
    /// A PLAIN REPORT — no question, no marker — REACHES THE PHONE IMMEDIATELY, RENDERED, AND RINGS.
    ///
    /// <para>
    /// Before this it was suppressed as narration and surfaced only if the whole orchestration then
    /// went idle for five minutes, through the deadlock net, as PLAIN TEXT. The owner's own quoted
    /// example of a message they needed took that route.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APlainSupervisorReport_RingsImmediately_AndArrivesRendered()
    {
        var orchId = await Start_Async();

        // FIRST, SPEND THE OWNER'S WAIT. Start_Async sends a message, which arms "the owner is
        // waiting for a reply" — and that flag was the one condition under which the OLD filter
        // pushed narration too. Without spending it, this probe would pass with the filter back in
        // place, which is exactly what a mutation check caught it doing.
        Append_Supervisor(orchId, 3, "Answering you: the rebuild is done.");

        Assert.True(
            await Wait_Until_Async(() => _telegram.Find_Sent_Containing("the rebuild is done") != null, 25_000),
            $"the answer never reached the phone, so the owner's wait was never spent.{Environment.NewLine}{_log.Dump()}");

        // NOW the entry under test: a plain report, with nobody waiting for anything.
        Append_Supervisor(orchId, 4, "**Two fixes** landed\n\n## Your pass\nthe `matrix` values are numbers");
        Append_Supervisor(orchId, 5, "and one more line, so the entry above is not the trailing one");

        Assert.True(
            await Wait_Until_Async(() => _telegram.Find_Sent_Containing("Two fixes") != null, 25_000),
            $"a plain supervisor report never reached the phone.{Environment.NewLine}{_log.Dump()}");

        var sent = _telegram.Find_Sent_Containing("Two fixes")!.Value;

        // IT RINGS: the supervisor's own words are the one thing the owner asked to be interrupted for.
        Assert.Equal(TelegramSendSounds.Rings, sent.Sound);

        // AND IT IS HTML: no literal Markdown markers on the phone.
        Assert.Contains("<b>", sent.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("**Two fixes**", sent.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("## Your pass", sent.Text, StringComparison.Ordinal);

        // AND IT CAME STRAIGHT AWAY: no five-minute deadlock release, no "nothing has moved" suffix.
        Assert.DoesNotContain("nothing has moved", sent.Text, StringComparison.Ordinal);
        Assert.False(_log.Has_Line_Containing("releasing it in case it was a question"), _log.Dump());
    }

    /// <summary>
    /// AN HOUR OF APP BOOKKEEPING MAKES NO SOUND. The owner's first complaint, measured: STATUS every
    /// thirty minutes, ten messages in five and a half hours, three of them identical.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WithNoSupervisorEntry_NothingTheAppWritesEverRings()
    {
        var orchId = await Start_Async();

        // The app's own traffic: an owner message (receipt), and a member's entries in its spoke.
        _telegram.Queue_Updates(Message_Json("how is it going", 6001, 301));

        Assert.True(
            await Wait_Until_Async(() => _telegram.Reactions.Count >= 1, 20_000),
            $"the receipt never arrived.{Environment.NewLine}{_log.Dump()}");

        // Give the tick room to do everything else it does per pass.
        await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(20));

        var loud = _telegram.Sent.Where(sent => sent.Sound == TelegramSendSounds.Rings).ToList();

        Assert.True(
            loud.Count == 0,
            $"the app rang the owner's phone {loud.Count} time(s) with its own bookkeeping: "
            + string.Join(" | ", loud.Select(sent => sent.Text.Length > 60 ? sent.Text[..60] : sent.Text)));

        // AND THE HALF-HOURLY STATUS IS GONE ENTIRELY — not merely silent.
        Assert.False(_telegram.Has_Sent_Containing("STATUS"), _telegram.Dump_Sent());
    }

    /// <summary>
    /// THE RECEIPT IS ONE REACTION AND NOTHING ELSE — no busy sentence at delivery, no second
    /// message about the same state. The owner counted fifteen identical "mid-task" sentences in
    /// one export.
    ///
    /// <para>
    /// The claim is unchanged; its SPELLING moved with brief D. It used to be "exactly one more ✓
    /// message, and silent"; the receipt is now a reaction on the owner's own bubble, so the
    /// stronger form is available and is asserted: exactly one more reaction, and NO message at
    /// all bearing a tick.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnOwnerMessage_GetsOneSilentReaction_AndNoBusySentence()
    {
        var orchId = await Start_Async();

        // The priming message in Start_Async has already earned its own receipt, so the claim is
        // about the NEXT one: exactly one more.
        // COUNTED AS RECEIPTS, NOT AS REACTIONS. One message earns TWO reactions over its life —
        // 👀 when the bridge has it and 👌 when a session picks it up — so counting reactions
        // counts the pickup as a second receipt, and whether the pickup lands inside this window
        // is a matter of timing. The claim is about how many times the owner was ACKNOWLEDGED.
        var receiptsBefore = Count_Receipts();

        _telegram.Queue_Updates(Message_Json("here is a thought", 6101, 302));

        Assert.True(
            await Wait_Until_Async(() => Count_Receipts() > receiptsBefore, 20_000),
            $"the receipt never arrived.{Environment.NewLine}{_log.Dump()}");

        await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(15));

        Assert.Equal(receiptsBefore + 1, Count_Receipts());

        // AND NOT AS A MESSAGE: the line in the topic the reaction replaced must not come back.
        Assert.Empty(_telegram.Sent.Where(sent => sent.Text.Contains('✓', StringComparison.Ordinal)));

        // The sentence the owner struck: it may only appear as a LATER EDIT, never as a message.
        Assert.False(_telegram.Has_Sent_Containing("mid-task"), _telegram.Dump_Sent());
        Assert.False(_telegram.Has_Sent_Containing("pick it up when this turn ends"), _telegram.Dump_Sent());
    }

    /// <summary>
    /// How many times the owner has been acknowledged — 👀 only. The 👌 that follows is the same
    /// receipt changing state, not a second one.
    /// </summary>
    int Count_Receipts()
    {
        return _telegram.Reactions.Count(reaction => reaction.Emoji == AIOrchestratorCoreLib.Telegram.OwnerReaction_Emoji.RECEIVED);
    }

    async Task<string> Start_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        _runCancellation = new CancellationTokenSource();
        _runLoop = _engine.Run_Async(_runCancellation.Token);

        _telegram.Queue_Updates(Message_Json("what is happening", 6000, 300));

        Assert.True(
            await Wait_Until_Async(() => _log.Has_Info_Containing("Owner message buffered"), 15_000),
            $"the engine never made its first pass.{Environment.NewLine}{_log.Dump()}");

        return session.OrchId;
    }

    void Append_Supervisor(string orchId, int entryNumber, string body)
    {
        File.AppendAllText(
            _paths.Get_OwnerChannelFile(orchId),
            $"\n## [{entryNumber}] FROM supervisor — {DateTime.Now:yyyy-MM-dd HH:mm} — a report\n{body}\n");
    }

    static string Message_Json(string text, long updateId, long messageId)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":" + updateId + ",\"message\":{\"message_id\":" + messageId
            + ",\"message_thread_id\":" + TOPIC_ID + ",\"from\":{\"id\":" + OWNER_USER_ID
            + "},\"chat\":{\"id\":" + SUPERGROUP_CHAT_ID + "},\"text\":\"" + text + "\"}}]}";
    }

    static async Task<bool> Wait_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
                return true;

            await Task.Delay(100);
        }

        return condition();
    }

    static async Task Stop_Async(CancellationTokenSource cancellation, Task loop)
    {
        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }
    }
}

/// <summary>
/// A CLIENT THAT REMEMBERS WHETHER EACH SEND MADE A SOUND. Every other fake in this suite records
/// the TEXT; the whole subject of brief C is the notification, which lives in the argument beside
/// it — so a fake that drops the sound cannot see the defect or the fix.
/// </summary>
internal sealed class SoundRecordingTelegram_Fake : ITelegramApiClient
{
    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    readonly object _lock = new();
    string? _queuedUpdatesJson;
    long _nextMessageId = 7000;

    public List<(string Text, TelegramSendSounds Sound)> Sent { get; } = [];

    public void Queue_Updates(string updatesJson)
    {
        lock (_lock)
            _queuedUpdatesJson = updatesJson;
    }

    public (string Text, TelegramSendSounds Sound)? Find_Sent_Containing(string fragment)
    {
        lock (_lock)
        {
            foreach (var sent in Sent)
            {
                if (sent.Text.Contains(fragment, StringComparison.Ordinal))
                    return sent;
            }

            return null;
        }
    }

    public bool Has_Sent_Containing(string fragment) => Find_Sent_Containing(fragment) != null;

    public int Count_Sent_Containing(string fragment)
    {
        lock (_lock)
            return Sent.Count(sent => sent.Text.Contains(fragment, StringComparison.Ordinal));
    }

    public string Dump_Sent()
    {
        lock (_lock)
            return string.Join($"{Environment.NewLine}--- sent ---{Environment.NewLine}", Sent.Select(sent => $"[{sent.Sound}] {sent.Text}"));
    }

    long? Record(string text, TelegramSendSounds sound)
    {
        lock (_lock)
        {
            Sent.Add((text, sound));
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

        // The real call is a long poll; returning instantly spins the inbound loop at full speed and
        // starves the mirror loop it shares a machine with.
        await Task.Delay(200, cancellationToken);

        return EMPTY_UPDATES;
    }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult(Record(text, sound));

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult(Record(html, sound));

    public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult(Record(html, sound));

    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult(Record(text, sound));

    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult(Record(text, sound));

    public Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        Record(captionHtml, sound);
        return Task.CompletedTask;
    }

    public Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        Record($"[photo] {filePath}", sound);
        return Task.CompletedTask;
    }

    // Edits raise no notification at all, which is why they carry no sound: recorded as text only.
    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<string> Get_BotUsername_Async(CancellationToken cancellationToken) => Task.FromResult("test_bot");
    public Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken) => Task.CompletedTask;
    readonly List<(long MessageId, string? Emoji)> _reactions = [];

    /// <summary>Since brief D the receipt is a reaction, so this is where a receipt is observed.</summary>
    public IReadOnlyList<(long MessageId, string? Emoji)> Reactions
    {
        get { lock (_lock) return [.. _reactions]; }
    }

    public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken)
    {
        lock (_lock)
            _reactions.Add((messageId, emoji));

        return Task.CompletedTask;
    }

    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken) => Task.FromResult(1L);
    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken) => Task.CompletedTask;
}
