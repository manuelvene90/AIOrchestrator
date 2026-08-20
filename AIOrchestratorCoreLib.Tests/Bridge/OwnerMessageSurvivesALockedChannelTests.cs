using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;
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
/// THE OWNER'S MESSAGE MUST SURVIVE A CHANNEL IT CANNOT BE WRITTEN TO.
/// <para>
/// <c>Take_ReadyDeliveries</c> REMOVES the message from the buffer before the append is attempted, so
/// once the flush is under way the local variable is the only copy in the process. When the appender
/// began returning false instead of throwing, "fail the write" implemented as a plain fall-through
/// would have destroyed the owner's text — worse than the collision the lock exists to prevent, and
/// the one place on this branch where a failed append costs something that cannot be reconstructed.
/// </para>
/// <para>
/// THIS SEAM WAS DECLARED UNTESTABLE BY ME, THREE TIMES, AND THE PREMISE WAS WRONG ON EVERY COUNT.
/// I claimed it needed a fake Telegram client the engine had no seam for, that the existing fakes were
/// private to another test file, and that the engine's 30 s retry backoff had to be waited out.
/// <see cref="BridgeEngine_Factory.Create_WithTelegramClient"/> is a production-shipped test seam that
/// says in its own docstring it exists for exactly this; <c>FailableTelegram_Fake</c> and
/// <c>RecordingLog_Fake</c> are <c>internal</c> top-level types in this assembly, usable unchanged; and
/// the 30 s backoff governs the MIRROR path, not this one, whose window is 4 s. A stated gap with an
/// unverified premise reads as an instruction to stop looking, which is why this file exists.
/// </para>
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class OwnerMessageSurvivesALockedChannelTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    /// <summary>Distinctive enough that finding it in the channel cannot be a coincidence.</summary>
    const string OWNER_TEXT = "did the overnight rebuild finish or not";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    public OwnerMessageSurvivesALockedChannelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-ownerlock-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // The inbound loop needs the chat and owner ids. The Italian layer is pinned OFF because it
        // defaults ON and would hand the owner's text to the real translator.
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
    /// THE TWO HALVES FAIL FOR DISJOINT REASONS, which is what stops either passing for the other's
    /// reason. A message missing from a locked channel is otherwise indistinguishable from a message
    /// that was never routed at all — and those have opposite conclusions.
    /// <list type="bullet">
    /// <item>locked — the text is NOT in the channel, and the engine says it kept it</item>
    /// <item>released — the SAME text arrives intact, which can only happen if it was put back</item>
    /// </list>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ALockedChannelDoesNotDestroyTheOwnersMessage_AndTheNextTickDeliversIt()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        var ownerChannel = _paths.Get_OwnerChannelFile(session.OrchId);
        var lockDirectory = Hold_Locked(ownerChannel);

        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(OWNER_TEXT));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Line_Containing("Owner message NOT delivered"), 40_000),
            "the flush never reached a blocked append, so nothing below means anything."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        Assert.DoesNotContain(OWNER_TEXT, File.ReadAllText(ownerChannel));

        // Now the only copy is whatever the engine kept. If the failure path had fallen through, the
        // owner's message is already gone and no amount of unlocking brings it back.
        Directory.Delete(lockDirectory, recursive: true);

        Assert.True(
            await Run_Until_Async(() => File.ReadAllText(ownerChannel).Contains(OWNER_TEXT), 40_000),
            "THE DEFECT: the owner's message was destroyed by a locked channel. Take_ReadyDeliveries had "
            + "already removed it from the buffer, so a failed append that fell through was the end of it."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");
    }

    /// <summary>
    /// WHY THE PUT-BACK CALLS <c>Release</c>, pinned on its own because the engine test above cannot
    /// see it: a generous timeout absorbs a second aggregation window, so only the buffer's own
    /// contract shows the difference.
    /// <para>
    /// <c>Add_Segment</c> refreshes <c>LastArrivalUtc</c>, so putting the message back ALONE would make
    /// the owner serve a second four-second window for a lock they know nothing about. They have
    /// already waited one out. <c>Release</c> is what makes the retry immediate.
    /// </para>
    /// </summary>
    [Fact]
    public void PuttingAMessageBackWithoutReleasing_WouldMakeTheOwnerWaitASecondWindow()
    {
        var now = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

        var withoutRelease = OwnerDeliveryBuffer_Factory.Create(aggregationSeconds: 4);
        withoutRelease.Add_Segment("channel.md", OWNER_TEXT, now);

        Assert.Empty(withoutRelease.Take_ReadyDeliveries(now));

        var withRelease = OwnerDeliveryBuffer_Factory.Create(aggregationSeconds: 4);
        withRelease.Add_Segment("channel.md", OWNER_TEXT, now);
        withRelease.Release("channel.md");

        var ready = withRelease.Take_ReadyDeliveries(now);

        Assert.Equal(OWNER_TEXT, Assert.Contains("channel.md", ready).Text);
    }

    /// <summary>
    /// Holds the channel's lock the way a foreign writer would, so the append genuinely fails for the
    /// whole budget. Returns the directory so the test can release it.
    /// </summary>
    static string Hold_Locked(string channelFile)
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(channelFile);
        Directory.CreateDirectory(lockDirectory);

        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow, "session", "another-writer"));

        return lockDirectory;
    }

    /// <summary>
    /// The tailer registers an unseen file at its CURRENT END, so the channel must exist before the
    /// first poll or everything appended to it starts behind the offset.
    /// </summary>
    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    static string Build_OwnerMessageJson(string text)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":2001,\"message\":{\"message_id\":88,"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    /// <summary>
    /// Runs the real engine loops until the condition holds, then stops them. Driven rather than
    /// poked: the buffer is private engine state whose only reader is the flush path.
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
