using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A TOPIC THAT IS GONE IS NOT RENAMED AGAIN — measured in production, 2026-09-10 20:56, six
/// `editForumTopic` calls answered 400 TOPIC_ID_INVALID at start, for four CLOSED orchestrations.
///
/// <para>
/// IT IS A REGRESSION I INTRODUCED, and naming that is the point of this file. Stage 8d changed the
/// name-sync loop's skip from "closed" to "closed AND the topic is recorded deleted", so that the
/// 🏁 glyph the owner asked for could ever be drawn — without that change it was unreachable dead
/// code, which the same branch was busy criticising elsewhere. But those four topics HAD been
/// deleted, before E1 existed to write `TelegramTopicDeletedUtc`; carrying no record, they stopped
/// being skipped and were renamed into nothing, once per revalidation, for as long as the app ran.
/// </para>
/// <para>
/// THE FIX WRITES THE MISSING RECORD. TOPIC_ID_INVALID is terminal — no number of retries makes a
/// deleted topic exist — so the first attempt learns the truth, marks the session, and the loop skips
/// it for good. That closes the gap for every session predating E1 rather than only for these four.
/// </para>
/// </summary>
public class AGoneTopicIsNotRenamedForeverTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long DEAD_TOPIC_ID = 7311;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly GoneTopicTelegram_Fake _telegram;

    public AGoneTopicIsNotRenamedForeverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-gone-topic-{Guid.NewGuid():N}");
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
        _telegram = new GoneTopicTelegram_Fake();

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, log, _telegram, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
    }

    /// <summary>
    /// THE EXACT PRODUCTION SHAPE: a CLOSED orchestration that still carries a topic id and NO
    /// delete record — a session closed before E1 existed. The rename is attempted once, refused as
    /// TOPIC_ID_INVALID, and never attempted again however long the app runs.
    ///
    /// ASSERTED ON THE COUNT ACROSS MANY TICKS, not on "it stopped": the defect was one call per
    /// revalidation for ever, so the claim is that the number does not GROW. One attempt is correct —
    /// the app cannot know a topic is gone without asking once.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AClosedSessionWhoseTopicWasDeletedBeforeE1_IsRenamedOnceAndThenLeftAlone()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, DEAD_TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, "a finished endeavour");

        // Closed, and with NO TelegramTopicDeletedUtc — which is what a session closed before E1 looks
        // like, and is precisely the state stage 8d stopped skipping.
        _store.Close_Orchestration(session.OrchId);

        Assert.Null(_store.Get_Session_OrNull(session.OrchId)!.TelegramTopicDeletedUtc);

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(6));

        var afterSixTicks = _telegram.Count_RenameAttempts();

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(6));

        Assert.True(
            afterSixTicks <= 1,
            $"the gone topic was renamed {afterSixTicks} times in six ticks — production saw six of these at one start.");

        Assert.Equal(
            afterSixTicks,
            _telegram.Count_RenameAttempts());
    }

    /// <summary>
    /// AND THE REASON IT STOPS IS RECORDED, not merely observed. The session is marked as having had
    /// its topic deleted, which is the missing fact: without it the skip could only ever be "we tried
    /// and it failed", which is state this loop does not keep across a restart. With it, the next
    /// process skips the session before it calls Telegram at all.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheSessionIsMarkedSoTheNextProcessNeverAsksAtAll()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, DEAD_TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, "a finished endeavour");
        _store.Close_Orchestration(session.OrchId);

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(6));

        Assert.NotNull(_store.Get_Session_OrNull(session.OrchId)!.TelegramTopicDeletedUtc);
    }

    /// <summary>
    /// A LIVE TOPIC IS STILL RENAMED. Without this the tests above would pass against a build that
    /// had simply stopped renaming anything — one route to "no calls", two very different behaviours,
    /// which is decision 20's second clause.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ALiveTopicIsStillRenamed()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, GoneTopicTelegram_Fake.LIVE_TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, "a live endeavour");

        var renamed = await Run_Until_Async(() => _telegram.Count_RenameAttempts() > 0, 20_000);

        Assert.True(renamed, "a live topic was never renamed, so the tests above prove nothing about the gone one.");
        Assert.Null(_store.Get_Session_OrNull(session.OrchId)!.TelegramTopicDeletedUtc);
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
/// Answers <c>editForumTopic</c> with Telegram's real 400 for a topic that no longer exists, for one
/// id, and normally for another — so a test can tell "stopped calling" from "stopped calling about
/// THIS one".
/// </summary>
internal sealed class GoneTopicTelegram_Fake : ITelegramApiClient
{
    public const long LIVE_TOPIC_ID = 7312;

    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    readonly object _lock = new();
    int _renameAttempts;
    long _nextMessageId = 8200;

    public int Count_RenameAttempts()
    {
        lock (_lock)
            return _renameAttempts;
    }

    public Task<string> Get_BotUsername_Async(CancellationToken cancellationToken) => Task.FromResult("test_bot");

    public Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken)
    {
        lock (_lock)
            _renameAttempts++;

        if (messageThreadId == LIVE_TOPIC_ID)
            return Task.CompletedTask;

        // Telegram's own wording, which TelegramError_Table classifies as TopicGone.
        throw new TelegramApiException(
            400, "Telegram 'editForumTopic' failed with HTTP 400: {\"ok\":false,\"error_code\":400,\"description\":\"Bad Request: TOPIC_ID_INVALID\"}", null);
    }

    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        // The delay is not optional: an instant answer spins the inbound loop and starves the machine.
        await Task.Delay(50, cancellationToken);

        return EMPTY_UPDATES;
    }

    long Next() { lock (_lock) return _nextMessageId++; }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.FromResult<long?>(Next());

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.FromResult<long?>(Next());

    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.FromResult<long?>(Next());

    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.FromResult<long?>(Next());

    public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.FromResult<long?>(Next());

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken) => Task.FromResult(LIVE_TOPIC_ID);

    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken) => Task.FromResult<byte[]>([]);

    // Brief D's reaction receipt. Nothing here is about receipts, so it answers and records nothing.
    public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken) => Task.CompletedTask;
}
