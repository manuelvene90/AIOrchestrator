using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// CLOSING AN ORCHESTRATION REALLY REMOVES ITS TOPIC — brief E1, driven through the front door.
///
/// <para>
/// The delete was one fire-and-forget call whose failure became a log line and nothing else: the
/// owner's ruling is that closed topics go away (they will have thousands), and a delete nobody
/// retried and nobody recorded was that ruling silently not kept. Every case here is about the
/// FAILURE paths, because the success path already worked and is not what stranded topics.
/// </para>
/// <para>
/// Through <see cref="BridgeEngine_Factory"/>, which is public and takes interfaces only —
/// <c>BridgeEngineModel</c> is <c>internal sealed</c> and this repo has twice refused
/// <c>InternalsVisibleTo</c>, so the retry loop and the start-up sweep are observed by their effects
/// on the fake client and on session.json rather than called directly.
/// </para>
/// </summary>
public class ClosingATopicReallyDeletesItTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 9091;
    const string ORCH_ID = "repo-1";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IOrchestrationLauncher _launcher;
    readonly RecordingLog_Fake _log = new();
    readonly DeletingTelegram_Fake _telegram = new();
    readonly ITestOutputHelper _out;

    public ClosingATopicReallyDeletesItTests(ITestOutputHelper output)
    {
        _out = output;

        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-topicdelete-{Guid.NewGuid():N}");
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

        _store.Create_Orchestration(ORCH_ID, "repo", _tempRepo);
        _store.Set_TelegramTopicId(ORCH_ID, TOPIC_ID);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A detached retry may still hold a handle; the temp folder is disposable.
        }
    }

    /// <summary>
    /// THE PROBE THE BRIEF ASKS FOR: 429 then 200. The old code made one call, took the 429 as a
    /// failure, logged it and left the topic up for ever — and a rate limit is an ORDINARY event in
    /// this app, not an exotic one, because it edits topic names on every tick.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ARateLimitedDelete_IsRetriedUntilItLands_AndTheSessionRecordsIt()
    {
        _telegram.Fail_NextDeletesWith(new TelegramApiException(429, "Telegram 'deleteForumTopic' failed with HTTP 429: too many requests", retryAfterSeconds: 1));

        var settled = await Run_Engine_Until_Async(
            beforeWait: engine => engine.Close_Orchestration_ByOwner(ORCH_ID, "the owner closed it from the app"),
            until: () => _store.Get_Session_OrNull(ORCH_ID)?.TelegramTopicDeletedUtc != null,
            timeoutMilliseconds: 15_000);

        Assert.True(settled,
            "the 429 was never retried, so the topic is still on the owner's phone and nothing on disk says so."
            + $"{Environment.NewLine}delete attempts: {_telegram.DeleteAttempts}{Environment.NewLine}{_log.Dump()}");

        Assert.Equal(2, _telegram.DeleteAttempts);
        Assert.Contains(TOPIC_ID, _telegram.DeletedTopicIds);

        // And the pending stamp was written BEFORE the attempt — that record is the only thing a
        // later start could have read if this process had died between the two calls.
        var session = _store.Get_Session(ORCH_ID);
        Assert.NotNull(session.TelegramTopicDeletePendingUtc);
        Assert.False(session.TelegramTopicDeleteFailureReported, "a delete that succeeded must never alert the owner");
    }

    /// <summary>
    /// THE OTHER PROBE THE BRIEF ASKS FOR: a permission failure. One message in General, EVER — and
    /// a fresh log line at every start, because the topic really is still there.
    ///
    /// <para>
    /// The restart half is the half that matters. The sweep runs at every start and a revoked right
    /// survives restarts by definition, so an alert without the persisted flag would arrive every
    /// time the app came up — decision 14's waterfall, on something the owner can act on once.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ADeleteTelegramWillNeverAccept_TellsTheOwnerOnce_AndNeverAgainAfterARestart()
    {
        _telegram.Refuse_AllDeletes(new TelegramApiException(400, "Telegram 'deleteForumTopic' failed with HTTP 400: {\"description\":\"Bad Request: not enough rights to manage topics\"}"));

        var reported = await Run_Engine_Until_Async(
            beforeWait: engine => engine.Close_Orchestration_ByOwner(ORCH_ID, "the owner closed it from the app"),
            until: () => _store.Get_Session_OrNull(ORCH_ID)?.TelegramTopicDeleteFailureReported == true,
            timeoutMilliseconds: 15_000);

        Assert.True(reported, $"a refused delete told the owner nothing.{Environment.NewLine}{_log.Dump()}");

        // A refusal is not retried inside the process: it cannot change while this process runs.
        Assert.Equal(1, _telegram.DeleteAttempts);
        Assert.Null(_store.Get_Session(ORCH_ID).TelegramTopicDeletedUtc);
        Assert.Equal(1, Count_GeneralAlerts());

        _out.WriteLine($"first run said: {_log.Dump()}");

        // THE RESTART. A second engine on the same root — which is what the app does every morning.
        var secondLog = new RecordingLog_Fake();
        var sweptAgain = await Run_Engine_Until_Async(
            engine: Build_Engine(secondLog),
            beforeWait: null,
            until: () => _telegram.DeleteAttempts >= 2,
            timeoutMilliseconds: 15_000);

        Assert.True(sweptAgain,
            "the start-up sweep never retried a delete that was still owed — the topic is stranded for good."
            + $"{Environment.NewLine}{secondLog.Dump()}");

        Assert.Equal(1, Count_GeneralAlerts());
        Assert.True(secondLog.Has_Line_Containing("refused to delete topic"), $"the restart said nothing in the log either.{Environment.NewLine}{secondLog.Dump()}");
    }

    /// <summary>
    /// THE CASE AN IN-PROCESS RETRY CANNOT COVER, and the one that actually strands topics: the app
    /// was closed or killed while the delete was still failing. Written as the state such a process
    /// leaves behind — a pending stamp, no deleted stamp — because that is exactly what is on disk.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ADeleteLeftPendingByAPreviousProcess_IsPaidOffAtTheNextStart()
    {
        _store.Close_Orchestration(ORCH_ID);
        _store.Mark_TopicDeletePending(ORCH_ID);

        var settled = await Run_Engine_Until_Async(
            beforeWait: null,
            until: () => _store.Get_Session_OrNull(ORCH_ID)?.TelegramTopicDeletedUtc != null,
            timeoutMilliseconds: 15_000);

        Assert.True(settled,
            $"a topic left pending by a dead process was never deleted.{Environment.NewLine}{_log.Dump()}");

        Assert.Contains(TOPIC_ID, _telegram.DeletedTopicIds);
    }

    /// <summary>
    /// THE MIGRATION CASE, at the engine rather than the planner: an orchestration closed before this
    /// feature existed carries no pending stamp, and its topic was deleted at the time. Sweeping it
    /// would fire a delete at a thread id that is long gone, for every orchestration the owner ever
    /// closed, on the first start after the upgrade.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnOrchestrationClosedBeforeThisFeature_IsNotDeletedAgainAtStartUp()
    {
        _store.Close_Orchestration(ORCH_ID);

        await Run_Engine_Until_Async(
            beforeWait: null,
            until: () => false,
            timeoutMilliseconds: BridgeTestTiming.Window_ForTicks(10));

        Assert.Equal(0, _telegram.DeleteAttempts);
        Assert.Null(_store.Get_Session(ORCH_ID).TelegramTopicDeletePendingUtc);
    }

    /// <summary>
    /// THE TWO PATHS COLLIDE, AND ONE OF THEM STANDS DOWN. A close fires the delete itself while the
    /// start-up sweep is still reading the sessions, so a pending stamp written in that window is
    /// picked up by BOTH — and a REFUSED delete attempted twice breaks the one promise this family
    /// makes: it is not retried inside the process, because a revoked right cannot change while the
    /// process runs.
    ///
    /// <para>
    /// FOUND BY <c>ADeleteTelegramWillNeverAccept_TellsTheOwnerOnce_AndNeverAgainAfterARestart</c>,
    /// which failed about one run in four under a full suite and passed alone — the shape of a race.
    /// Its assertion was right; the code was not. This probe forces the collision instead of waiting
    /// for it: the pending stamp is already on disk (so the sweep will take it) AND the owner closes
    /// the same orchestration (so the close path takes it too).
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheCloseAndTheStartUpSweep_NeverAttemptTheSameDeleteTwice()
    {
        _telegram.Refuse_AllDeletes(new TelegramApiException(400, "Telegram 'deleteForumTopic' failed with HTTP 400: {\"description\":\"Bad Request: not enough rights to manage topics\"}"));

        // On disk before the engine starts: the sweep will find this and take the delete on.
        _store.Mark_TopicDeletePending(ORCH_ID);

        // ...and the owner closes the same orchestration as the engine comes up, which fires the
        // delete down the other path.
        await Run_Engine_Until_Async(
            beforeWait: engine => engine.Close_Orchestration_ByOwner(ORCH_ID, "the owner closed it from the app"),
            until: () => _telegram.DeleteAttempts >= 2,
            timeoutMilliseconds: BridgeTestTiming.Window_ForTicks(20));

        // ONE attempt, whichever path got there first — and one alert, which is what the owner sees.
        Assert.Equal(1, _telegram.DeleteAttempts);
        Assert.Equal(1, Count_GeneralAlerts());
    }

    IBridgeEngine Build_Engine(IOrchestrationLog log)
    {
        return BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, _configProvider, _store, _launcher, log, _telegram, BridgeTestTiming.Fast());
    }

    Task<bool> Run_Engine_Until_Async(Action<IBridgeEngine>? beforeWait, Func<bool> until, int timeoutMilliseconds)
    {
        return Run_Engine_Until_Async(Build_Engine(_log), beforeWait, until, timeoutMilliseconds);
    }

    /// <summary>
    /// Starts the engine, lets the caller poke it, and polls the predicate — then cancels and waits
    /// for the loops to end so the next engine in the same test starts on a quiet root.
    /// </summary>
    static async Task<bool> Run_Engine_Until_Async(IBridgeEngine engine, Action<IBridgeEngine>? beforeWait, Func<bool> until, int timeoutMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();
        var running = engine.Run_Async(cancellation.Token);

        beforeWait?.Invoke(engine);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        var satisfied = false;

        while (DateTime.UtcNow < deadline)
        {
            if (until())
            {
                satisfied = true;
                break;
            }

            await Task.Delay(25);
        }

        await cancellation.CancelAsync();

        try
        {
            await running;
        }
        catch (OperationCanceledException)
        {
            // The ordinary way this method ends.
        }

        return satisfied;
    }

    /// <summary>
    /// How many times the owner was told this topic will not delete. Counted from the General
    /// channel file, which is where the owner actually reads it — not from a flag the code sets.
    /// </summary>
    int Count_GeneralAlerts()
    {
        if (!File.Exists(_paths.GeneralChannelFile))
            return 0;

        var text = File.ReadAllText(_paths.GeneralChannelFile);
        var needle = $"Telegram would not delete the topic of '{ORCH_ID}'";
        var count = 0;

        for (var index = text.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = text.IndexOf(needle, index + 1, StringComparison.Ordinal))
            count++;

        return count;
    }
}

/// <summary>
/// A Telegram client whose only interesting method is the delete. Scoped to this file rather than
/// shared, because what it has to do — hand out a DIFFERENT answer per attempt and count them — is
/// not what the other engine probes' fakes do; consolidating the six of them is real work and is
/// parked (decision 22), not folded into this row.
/// </summary>
internal sealed class DeletingTelegram_Fake : ITelegramApiClient
{
    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    readonly Lock _lock = new();
    readonly List<long> _deleted = [];

    Exception? _oneOffFailure;
    Exception? _permanentFailure;
    int _deleteAttempts;
    long _nextMessageId = 1;

    public int DeleteAttempts
    {
        get { lock (_lock) return _deleteAttempts; }
    }

    public IReadOnlyList<long> DeletedTopicIds
    {
        get { lock (_lock) return [.. _deleted]; }
    }

    /// <summary>Fails the NEXT delete only; the one after it succeeds.</summary>
    public void Fail_NextDeletesWith(Exception failure)
    {
        lock (_lock)
            _oneOffFailure = failure;
    }

    public void Refuse_AllDeletes(Exception failure)
    {
        lock (_lock)
            _permanentFailure = failure;
    }

    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken)
    {
        Exception? failure;

        lock (_lock)
        {
            _deleteAttempts++;

            if (_permanentFailure != null)
            {
                failure = _permanentFailure;
            }
            else if (_oneOffFailure != null)
            {
                failure = _oneOffFailure;
                _oneOffFailure = null;
            }
            else
            {
                failure = null;
                _deleted.Add(messageThreadId);
            }
        }

        return failure == null ? Task.CompletedTask : Task.FromException(failure);
    }

    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        // Stands in for the long poll; without it the inbound loop spins hot for the whole run.
        await Task.Delay(50, cancellationToken);

        return EMPTY_UPDATES;
    }

    /// <summary>
    /// Both of these are startup calls the bridge makes once (brief B): the bot's own name, for the
    /// message that reports two hosts polling one token, and the webhook clear that stops a stale
    /// registration looking exactly like a second poller. Neither is this probe's subject, so both
    /// answer plainly.
    /// </summary>
    public Task<string> Get_BotUsername_Async(CancellationToken cancellationToken) => Task.FromResult("test_bot");

    public Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<long> Create_ForumTopic_Async(string topicName, CancellationToken cancellationToken) => Task.FromResult(7777L);

    /// <summary>
    /// BOTH SHAPES ON PURPOSE, and the second one is not dead code — it is a merge guard.
    ///
    /// <para>
    /// `stage/9b-telegram-hygiene` gives every topic a colour (brief F1) and changes this interface
    /// method to carry it. Each stage merges into `ours/integration` cleanly on its own, but git
    /// cannot see that a fake written against the OLD signature stops implementing the interface
    /// once the other stage lands: the merge succeeds and the build fails, which is the worst
    /// shape a conflict can take because nothing warns until afterwards. Declaring both means this
    /// file compiles in either world — the extra method is simply unused until F1 arrives, and is
    /// the one Telegram is asked for afterwards.
    /// </para>
    /// </summary>
    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken) => Task.FromResult(7777L);
    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult<long?>(_nextMessageId++);
    }

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken) => Send_Message_Async(messageThreadId, html, sound, cancellationToken);
    public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken) => Send_Message_Async(messageThreadId, html, sound, cancellationToken);
    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken) => Send_Message_Async(messageThreadId, text, sound, cancellationToken);
    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken) => Send_Message_Async(messageThreadId, text, sound, cancellationToken);
    public Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken) => Task.CompletedTask;
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
