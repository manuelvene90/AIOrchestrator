using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Channels;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE QUEUE MUST BE WIRED IN, NOT MERELY CORRECT.
/// <para>
/// <c>PendingAnnouncementsTests</c> drives the queue directly and pins its ordering and its cap. It
/// cannot see whether the ENGINE queues on failure or whether the tick ever drains — the same gap
/// that made the tick-allowance bound fully tested and never in force until a wiring test caught it.
/// This drives the real path: a real <c>/dnd</c> from the owner, a really locked channel, and a real
/// tick doing the retry.
/// </para>
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class AnnouncementSurvivesALockedChannelTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    /// <summary>Distinctive enough that finding it in the channel cannot be a coincidence.</summary>
    const string ANNOUNCEMENT_MARKER = "Do-Not-Disturb";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    public AnnouncementSurvivesALockedChannelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-announcement-lock-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

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

    /// <summary>
    /// The two halves fail for disjoint reasons: locked, the announcement is absent but the engine
    /// says it kept it; unlocked, the SAME announcement arrives, which can only happen if a later
    /// tick retried it.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ADndAnnouncementBlockedByALockedChannel_ArrivesOnALaterTick()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);
        var lockDirectory = Hold_Locked(ownerChannel);

        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson("/dnd"));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Line_Containing("it is queued and the next tick retries"), 40_000),
            "the /dnd never reached a blocked announcement, so nothing below means anything."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        Assert.DoesNotContain(ANNOUNCEMENT_MARKER, File.ReadAllText(ownerChannel));

        // Now the only copy is whatever the engine kept. Before the queue this announcement was gone
        // for good: the mode had already flipped, so nothing would ever announce it again.
        Directory.Delete(lockDirectory, recursive: true);

        Assert.True(
            await Run_Until_Async(() => File.ReadAllText(ownerChannel).Contains(ANNOUNCEMENT_MARKER), 40_000),
            "THE DEFECT: the announcement was lost to a locked channel. The mode had already flipped, so the "
            + "supervisor is never told the owner went away and keeps asking them questions — which is exactly "
            + "what away mode exists to stop."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");
    }

    /// <summary>
    /// THE EXIT DRAIN. <c>Announce</c> no longer writes, so anything queued when the loops stop would
    /// die with the process — the one real cost of making the drain the single writer. <c>Run_Async</c>
    /// drains once more in a <c>finally</c>, which is what keeps this change an improvement rather
    /// than a straight trade.
    /// <para>
    /// The control is deleting that <c>finally</c>: the announcement then never appears, because
    /// nothing else will ever write it.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnAnnouncementQueuedWhenTheEngineStops_IsStillWrittenOnTheWayOut()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);
        var lockDirectory = Hold_Locked(ownerChannel);

        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson("/dnd"));

        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        // Wait for it to be QUEUED and blocked, so there is something for the exit drain to find.
        for (var waited = 0; waited < 40_000; waited += 100)
        {
            if (_log.Has_Line_Containing("it is queued and the next tick retries"))
                break;

            await Task.Delay(100);
        }

        Assert.True(
            _log.Has_Line_Containing("it is queued and the next tick retries"),
            $"nothing was ever queued, so the exit drain has nothing to prove.{Environment.NewLine}{_log.Dump()}");

        Assert.DoesNotContain(ANNOUNCEMENT_MARKER, File.ReadAllText(ownerChannel));

        // Free the channel and stop the engine in the same breath: the ONLY remaining chance to write
        // this announcement is the drain on the way out.
        Directory.Delete(lockDirectory, recursive: true);

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }

        Assert.Contains(
            ANNOUNCEMENT_MARKER,
            File.ReadAllText(ownerChannel));
    }

    static string Hold_Locked(string channelFile)
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(channelFile);
        Directory.CreateDirectory(lockDirectory);

        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow, "session", "another-writer"));

        return lockDirectory;
    }

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    static string Build_OwnerMessageJson(string text)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":3001,\"message\":{\"message_id\":91,"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
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
