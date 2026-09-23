using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.TopicDeletion;
using AIOrchestratorCoreLib.Configuration;
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
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// WHAT CLOSING AN ORCHESTRATION DOES TO ITS TOPIC — <c>topic.onClose</c>, plan 03 Task 10.
///
/// <para>
/// <c>delete</c> IS THE SHIPPED DEFAULT, the owner's answer D2 (2026-09-14), and it is today's behaviour
/// unchanged: <c>deleteForumTopic</c>, the pending stamp written first, the bounded retry and the start-up
/// sweep. <c>close</c> STAYS AVAILABLE — the owner said close-but-keep "stays available as an option"
/// (ruling R4) — and it is <c>closeForumTopic</c>: the topic is still in the owner's list afterwards,
/// which is the entire point of choosing it, and it carries NONE of the delete's machinery. A failed
/// delete leaves an orphan with no record, which is why the delete retries and is swept; a failed close
/// leaves a topic that is merely still open, which the next close fixes — so a close gets one attempt.
/// </para>
/// <para>
/// Through <see cref="BridgeEngine_Factory"/>, the same front door ClosingATopicReallyDeletesItTests uses,
/// and with its fake: the engine is <c>internal sealed</c>, so the branch is observed by its effects on
/// the fake and on session.json.
/// </para>
/// </summary>
public class TopicOnCloseTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 9191;
    const string ORCH_ID = "repo-1";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingLog_Fake _log = new();
    readonly DeletingTelegram_Fake _telegram = new();

    public TopicOnCloseTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-topiconclose-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _store.Create_Orchestration(ORCH_ID, "repo", _tempRepo);
        _store.Set_TelegramTopicId(ORCH_ID, TOPIC_ID);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // THE DEFAULT
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE SHIPPED DEFAULT IS <c>delete</c> — D2, the owner, 2026-09-14 — and a config that says nothing
    /// resolves to it through classic, which does not state the key. Read through the catalogue row and
    /// the loader rather than asserted as a literal on one of them, so a row and a preset that disagree
    /// cannot both be green.
    /// </summary>
    [Fact]
    public void TheShippedDefault_IsDelete_AndAMachineThatSaysNothingDeletes()
    {
        Assert.Equal(TopicClose_Actions.DELETE_TEXT, Catalog.Find_OrNull("topic.onClose")!.Default_OrNull!.GetValue<string>());

        File.WriteAllText(_paths.ConfigFile, "{\"repos\":[]}");

        Assert.Equal(TopicCloseActions.Delete, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone.TopicOnClose);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // WHO GETS THE RETRY — TopicClose_Decider, pure
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ONE ATTEMPT, AND FEWER THAN THE DELETE'S — the decision the brief asks to be stated in code. The
    /// delete retries because a failed one strands an orphan nothing records; a failed close strands a
    /// topic that is merely still open, and the next close fixes it.
    /// </summary>
    [Fact]
    public void AClose_GetsOneAttempt_AndTheDeleteKeepsItsRetry()
    {
        Assert.Equal(1, TopicClose_Decider.MAXIMUM_ATTEMPTS);
        Assert.True(TopicDelete_Decider.MAXIMUM_ATTEMPTS > TopicClose_Decider.MAXIMUM_ATTEMPTS);
    }

    /// <summary>
    /// WHAT ONE <c>closeForumTopic</c> TOLD US. Closing a closed topic is answered TOPIC_NOT_MODIFIED —
    /// the state we were trying to reach, which is what makes closing twice idempotent — and a topic
    /// that is not there cannot be closed or be in anyone's list. Everything else, a refusal and a
    /// rate limit and silence alike, is "not closed": the log says so, and nothing retries.
    /// </summary>
    [Fact]
    public void Classify_ReadsTelegramsAnswersToAClose()
    {
        Assert.Equal(TopicCloseOutcomes.Closed, TopicClose_Decider.Classify(null));
        Assert.Equal(TopicCloseOutcomes.AlreadyClosed, TopicClose_Decider.Classify(Answered(400, "Bad Request: TOPIC_NOT_MODIFIED")));
        Assert.Equal(TopicCloseOutcomes.AlreadyGone, TopicClose_Decider.Classify(Answered(400, "Bad Request: TOPIC_ID_INVALID")));
        Assert.Equal(TopicCloseOutcomes.NotClosed, TopicClose_Decider.Classify(Answered(400, "Bad Request: not enough rights to manage topics")));
        Assert.Equal(TopicCloseOutcomes.NotClosed, TopicClose_Decider.Classify(new TelegramApiException(429, "Telegram 'closeForumTopic' failed with HTTP 429: too many requests", retryAfterSeconds: 3)));
        Assert.Equal(TopicCloseOutcomes.NotClosed, TopicClose_Decider.Classify(new TaskCanceledException("the HTTP timeout")));
    }

    [Fact]
    public void Is_Settled_IsEveryOutcomeButNotClosed()
    {
        Assert.True(TopicClose_Decider.Is_Settled(TopicCloseOutcomes.Closed));
        Assert.True(TopicClose_Decider.Is_Settled(TopicCloseOutcomes.AlreadyClosed));
        Assert.True(TopicClose_Decider.Is_Settled(TopicCloseOutcomes.AlreadyGone));
        Assert.False(TopicClose_Decider.Is_Settled(TopicCloseOutcomes.NotClosed));
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // UNDER `close` — through the engine
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE POINT OF <c>close</c>: the topic is closed and IS STILL IN THE OWNER'S LIST afterwards. No
    /// delete is attempted, and no pending stamp is written — the stamp is the delete's debt record, and
    /// a close owes nothing.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClose_TheTopicIsClosed_AndIsStillInTheOwnersList()
    {
        Write_Config("close");

        var closed = await Run_Engine_Until_Async(
            beforeWait: engine => engine.Close_Orchestration_ByOwner(ORCH_ID, "the owner closed it from the app"),
            until: () => _telegram.CloseAttempts >= 1,
            timeoutMilliseconds: 15_000);

        Assert.True(closed, $"closeForumTopic was never called.{Environment.NewLine}{_log.Dump()}");

        Assert.True(_telegram.Is_Closed(TOPIC_ID));
        Assert.True(_telegram.Is_InTheOwnersList(TOPIC_ID), "under 'close' the topic must still be in the owner's list — that is the option's whole point");
        Assert.Equal(0, _telegram.DeleteAttempts);

        var session = _store.Get_Session(ORCH_ID);
        Assert.Null(session.TelegramTopicDeletePendingUtc);
        Assert.Null(session.TelegramTopicDeletedUtc);
    }

    /// <summary>
    /// NOTHING IS LEFT FOR THE START-UP SWEEP. A second engine on the same root — the next morning's
    /// start — finds no debt, deletes nothing and does not close again.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClose_TheNextStartSweepsNothing()
    {
        Write_Config("close");

        await Run_Engine_Until_Async(
            beforeWait: engine => engine.Close_Orchestration_ByOwner(ORCH_ID, "the owner closed it from the app"),
            until: () => _telegram.CloseAttempts >= 1,
            timeoutMilliseconds: 15_000);

        await Run_Engine_Until_Async(
            engine: Build_Engine(new RecordingLog_Fake()),
            beforeWait: null,
            until: () => false,
            timeoutMilliseconds: BridgeTestTiming.Window_ForTicks(10));

        Assert.Equal(0, _telegram.DeleteAttempts);
        Assert.Equal(1, _telegram.CloseAttempts);
        Assert.True(_telegram.Is_InTheOwnersList(TOPIC_ID));
    }

    /// <summary>
    /// A FAILED CLOSE IS NOT RETRIED AND LEAVES NO RECORD — the half of "do not let close inherit the
    /// delete's retry machinery by accident". One attempt, however many ticks follow; one warning in the
    /// log saying the topic is still open; nothing in General, because a topic left open is not
    /// something the owner has to be told about (decision 15) — its name already reads 🏁.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClose_AFailedClose_IsNotRetried_AndTellsOnlyTheLog()
    {
        Write_Config("close");
        _telegram.Refuse_AllCloses(Answered(400, "Bad Request: not enough rights to manage topics"));

        await Run_Engine_Until_Async(
            beforeWait: engine => engine.Close_Orchestration_ByOwner(ORCH_ID, "the owner closed it from the app"),
            until: () => false,
            timeoutMilliseconds: BridgeTestTiming.Window_ForTicks(10));

        Assert.Equal(1, _telegram.CloseAttempts);
        Assert.Equal(0, _telegram.DeleteAttempts);
        Assert.True(_log.Has_Line_Containing("was NOT closed"), $"a failed close said nothing in the log.{Environment.NewLine}{_log.Dump()}");
        Assert.Null(_store.Get_Session(ORCH_ID).TelegramTopicDeletePendingUtc);
        Assert.DoesNotContain("would not close", Read_General());
    }

    /// <summary>
    /// CLOSING TWICE IS IDEMPOTENT. The second <c>closeForumTopic</c> is answered TOPIC_NOT_MODIFIED —
    /// Telegram's refusal to close a closed topic — and that is the state wanted, so it is not a failure:
    /// no warning, nothing deleted, the topic still closed and still in the list.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClose_ClosingTwice_IsIdempotent()
    {
        Write_Config("close");

        await Run_Engine_Until_Async(
            beforeWait: engine =>
            {
                engine.Close_Orchestration_ByOwner(ORCH_ID, "the owner closed it from the app");
                engine.Close_Orchestration_ByOwner(ORCH_ID, "and closed it again");
            },
            until: () => _telegram.CloseAttempts >= 2,
            timeoutMilliseconds: 15_000);

        Assert.Equal(2, _telegram.CloseAttempts);
        Assert.True(_telegram.Is_Closed(TOPIC_ID));
        Assert.True(_telegram.Is_InTheOwnersList(TOPIC_ID));
        Assert.Equal(0, _telegram.DeleteAttempts);

        // Give the detached second attempt its log line before reading it.
        await Task.Delay(200);
        Assert.False(_log.Has_Line_Containing("was NOT closed"), $"TOPIC_NOT_MODIFIED on a second close was read as a failure.{Environment.NewLine}{_log.Dump()}");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // UNDER `delete` — today's behaviour, unchanged
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// TODAY'S BEHAVIOUR, STATED EXPLICITLY IN config.json so the branch is selected by the key and not
    /// merely by the default: the pending stamp, <c>deleteForumTopic</c>, the recorded delete, and the
    /// topic gone from the list. The four-attempt backoff and the start-up sweep are
    /// ClosingATopicReallyDeletesItTests', which runs under the default.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderDelete_TheTopicIsDeleted_WithItsPendingStamp_AndNeverClosed()
    {
        Write_Config("delete");

        var deleted = await Run_Engine_Until_Async(
            beforeWait: engine => engine.Close_Orchestration_ByOwner(ORCH_ID, "the owner closed it from the app"),
            until: () => _store.Get_Session_OrNull(ORCH_ID)?.TelegramTopicDeletedUtc != null,
            timeoutMilliseconds: 15_000);

        Assert.True(deleted, $"the topic was never deleted.{Environment.NewLine}{_log.Dump()}");

        Assert.False(_telegram.Is_InTheOwnersList(TOPIC_ID));
        Assert.Equal(0, _telegram.CloseAttempts);
        Assert.NotNull(_store.Get_Session(ORCH_ID).TelegramTopicDeletePendingUtc);
    }

    /// <summary>
    /// CLOSING TWICE IS IDEMPOTENT UNDER <c>delete</c> TOO: one delete (the second path stands down on
    /// the process's taken-on set), no refusal alert, the topic gone once.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderDelete_ClosingTwice_IsIdempotent()
    {
        Write_Config("delete");

        await Run_Engine_Until_Async(
            beforeWait: engine =>
            {
                engine.Close_Orchestration_ByOwner(ORCH_ID, "the owner closed it from the app");
                engine.Close_Orchestration_ByOwner(ORCH_ID, "and closed it again");
            },
            until: () => _store.Get_Session_OrNull(ORCH_ID)?.TelegramTopicDeletedUtc != null,
            timeoutMilliseconds: 15_000);

        Assert.Equal(1, _telegram.DeleteAttempts);
        Assert.Equal(0, _telegram.CloseAttempts);
        Assert.False(_telegram.Is_InTheOwnersList(TOPIC_ID));
        Assert.DoesNotContain("would not delete", Read_General());
    }

    void Write_Config(string onClose)
    {
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},\"telegramOwnerUserId\":{OWNER_USER_ID},\"topic\":{{\"onClose\":\"{onClose}\"}}}}");
    }

    static TelegramApiException Answered(int status, string description)
    {
        return new TelegramApiException(status, $"Telegram 'closeForumTopic' failed with HTTP {status}: {{\"ok\":false,\"error_code\":{status},\"description\":\"{description}\"}}");
    }

    string Read_General()
    {
        return File.Exists(_paths.GeneralChannelFile) ? File.ReadAllText(_paths.GeneralChannelFile) : "";
    }

    IBridgeEngine Build_Engine(IOrchestrationLog log)
    {
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);

        return BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, launcher, log, _telegram, BridgeTestTiming.Fast());
    }

    Task<bool> Run_Engine_Until_Async(Action<IBridgeEngine>? beforeWait, Func<bool> until, int timeoutMilliseconds)
    {
        return Run_Engine_Until_Async(Build_Engine(_log), beforeWait, until, timeoutMilliseconds);
    }

    /// <summary>The same drive loop ClosingATopicReallyDeletesItTests uses: start, poke, poll, cancel, drain.</summary>
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
}
