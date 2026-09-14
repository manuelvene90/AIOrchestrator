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
/// A TOPIC EXISTS AS SOON AS THE ORCHESTRATION DOES — the owner's directive of 2026-09-12, after a
/// topic failed to appear for twenty minutes: *"c'è qualcosa che non va con l'apertura dei topic, non
/// so se un topic è stato aperto silente o se le cose si sono perse o cosa."*
///
/// <para>
/// WHAT IT WAS. Topic creation hung off the mirror, and the mirror returns early when an append
/// carries nothing mirrorable. An owner-authored entry is never mirrored — it came FROM Telegram — so
/// a brand-new orchestration, whose channel holds exactly one owner message and nothing else, had no
/// topic until one of its sessions wrote something. A print-run solo that spends twenty minutes on
/// its first turn therefore left the owner reading a topic list in which their own endeavour was
/// missing, with nothing logged and nothing lost: indistinguishable, from the phone, from a failure.
/// </para>
/// <para>
/// PROBED THROUGH THE ENGINE'S REAL TICK, because the defect was in the ORDER of the loop rather than
/// in any decision a pure function could be asked about. Nothing in the suite could see it: the
/// creation code itself was correct and had always worked the moment it was reached.
/// </para>
/// <para>
/// THE SILENCED CASE IS ASSERTED AGAINST A LIVE CONTROL IN THE SAME RUN, deliberately. "No topic was
/// created" has two routes to it — the gate held, or the sweep never ran at all — so a silenced
/// orchestration alone would pin neither. The second orchestration getting its topic in the same
/// ticks is what makes the first one's silence mean something.
/// </para>
/// </summary>
public class ANewOrchestrationGetsItsTopicAtOnceTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly TopicCountingTelegram_Fake _telegram;

    public ANewOrchestrationGetsItsTopicAtOnceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-topic-at-creation-tests-{Guid.NewGuid():N}");
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
        _telegram = new TopicCountingTelegram_Fake();

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
    /// THE OWNER'S MESSAGE IS THE ONLY THING IN THE CHANNEL, which is exactly the state that used to
    /// produce no topic: it is the one entry shape the mirror refuses to push.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnOrchestrationWhoseOnlyEntryIsTheOwnersStillGetsItsTopic()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        Seed_OwnerChannel_WithTheOwnersMessageOnly(session.OrchId);

        var got = await Run_Until_Async(
            () => _store.Get_Session_OrNull(session.OrchId)?.TelegramTopicId != null,
            BridgeTestTiming.Window_ForTicks(6));

        Assert.True(got, $"the orchestration never got a topic. Topics created: {_telegram.TopicsCreated}");
        Assert.Equal(TopicCountingTelegram_Fake.FIRST_TOPIC_ID, _store.Get_Session_OrNull(session.OrchId)!.TelegramTopicId);
    }

    /// <summary>
    /// ONE TOPIC, NOT ONE PER TICK. The sweep runs on every tick, so the persisted id is what stops it
    /// asking Telegram again — and a sweep that re-created the topic would have shown as a working
    /// feature in the test above while quietly minting a thread every two seconds.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheSweepCreatesTheTopicOnceAndThenLeavesItAlone()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        Seed_OwnerChannel_WithTheOwnersMessageOnly(session.OrchId);

        await Run_Until_Async(
            () => false,
            BridgeTestTiming.Window_ForTicks(8));

        Assert.Equal(1, _telegram.TopicsCreated);
    }

    /// <summary>
    /// SILENCED MEANS THE OWNER IS READING THIS ONE IN THE TERMINAL, so the sweep does not put a new
    /// thread on their phone — while the orchestration beside it, in the same ticks, does get one.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASilencedOrchestrationIsSkipped_WhileTheOneBesideItIsServed()
    {
        var silenced = _launcher.Start_Orchestration("Silenced", _tempRepo);
        var live = _launcher.Start_Orchestration("Live", _tempRepo);

        Seed_OwnerChannel_WithTheOwnersMessageOnly(silenced.OrchId);
        Seed_OwnerChannel_WithTheOwnersMessageOnly(live.OrchId);

        _store.Set_TelegramMode(silenced.OrchId, TelegramDeliveryModes.Silenced);

        var liveGotOne = await Run_Until_Async(
            () => _store.Get_Session_OrNull(live.OrchId)?.TelegramTopicId != null,
            BridgeTestTiming.Window_ForTicks(6));

        Assert.True(liveGotOne, $"the control orchestration never got a topic, so this test proves nothing about the silenced one. Topics created: {_telegram.TopicsCreated}");
        Assert.Null(_store.Get_Session_OrNull(silenced.OrchId)!.TelegramTopicId);
    }

    /// <summary>
    /// Exactly what a freshly created orchestration holds: the bridge's own header and the owner's
    /// message, appended by the bridge when it came in from Telegram. No agent has written yet.
    /// </summary>
    void Seed_OwnerChannel_WithTheOwnersMessageOnly(string orchId)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.WriteAllText(
            _paths.Get_OwnerChannelFile(orchId),
            "# OWNER CHANNEL\n\n---\n\n"
            + $"## [1] FROM owner — {stamp} — via Telegram\n\nWork on the topic thing.\n");
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
/// Counts topic creations and hands out a distinct id for each, which is what lets a test tell "one
/// topic, kept" from "a topic a tick".
/// </summary>
internal sealed class TopicCountingTelegram_Fake : ITelegramApiClient
{
    public const long FIRST_TOPIC_ID = 9101;

    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    readonly object _lock = new();

    int _topicsCreated;
    long _nextTopicId = FIRST_TOPIC_ID;
    long _nextMessageId = 1;

    public int TopicsCreated
    {
        get { lock (_lock) return _topicsCreated; }
    }

    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _topicsCreated++;
            return Task.FromResult(_nextTopicId++);
        }
    }

    public Task<string> Get_BotUsername_Async(CancellationToken cancellationToken) => Task.FromResult("test_bot");

    public Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult<long?>(Next_MessageId());

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult<long?>(Next_MessageId());

    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult<long?>(Next_MessageId());

    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult<long?>(Next_MessageId());

    public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult<long?>(Next_MessageId());

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        // THE DELAY IS NOT OPTIONAL — a fake that answers instantly spins the inbound loop as fast as
        // the scheduler allows and starves the machine.
        await Task.Delay(50, cancellationToken);

        return EMPTY_UPDATES;
    }

    public Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken) => Task.CompletedTask;

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

    public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken) => Task.FromResult<byte[]>([]);

    long Next_MessageId()
    {
        lock (_lock)
            return _nextMessageId++;
    }
}
