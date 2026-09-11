using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;
using Xunit.Abstractions;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// AN OWNER MODE COMMAND MUST REACH TELEGRAM EVEN WHEN THE APP THINKS THERE IS NOTHING TO DO.
///
/// The topic-name sync keeps an in-memory note of the last name it applied, so the periodic tick
/// does not spend an API call every two seconds saying the same thing. That memo is right almost
/// always — but when it is wrong it is wrong in the one direction that cannot heal, because being
/// wrong means SKIPPING, and every later tick agrees there is nothing to do.
///
/// It can be wrong two ways, both live: the map was written by two loops with no ordering (the
/// mirror tick and the inbound command loop both call the sync), and a genuine Telegram refusal
/// records the refused name as applied on purpose.
///
/// The owner, 2026-08-24, on a topic switched back to Normal six minutes earlier with no sync error
/// in the log: *"the topic still has the moon even though I removed the DND"*.
///
/// So this pins the rule that makes their action reliable regardless of what the memo believes: a
/// command they typed by hand always re-pushes. The case chosen is the one where the memo and the
/// wanted name AGREE — /unmute on a topic already Normal — because that is the only shape where the
/// old code takes the skip, and therefore the only shape that can tell the fix from its absence.
/// </summary>
public class OwnerModeCommandAlwaysRepushesTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 6120;

    const string DISPLAY_NAME = "IS regime rules";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly RecordingTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;
    readonly ITestOutputHelper _out;

    public OwnerModeCommandAlwaysRepushesTests(ITestOutputHelper output)
    {
        _out = output;

        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-repush-{Guid.NewGuid():N}");
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
    public async Task Unmute_OnATopicTheAppAlreadyBelievesIsNormal_StillPushesTheName()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        // The startup sync applies the undecorated name and remembers it. From here the memo and the
        // wanted name agree, which is exactly the state that used to make the command a no-op.
        Assert.True(
            await Run_Until_Async(() => _telegram.Dump_TopicNames().Length > 0, 20_000),
            $"the name was never applied at all, so this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        var before = Pushed_NameCount();

        Assert.Equal(TelegramDeliveryModes.Normal, _store.Get_Session_OrNull(orchId)!.TelegramMode);

        // The owner takes DND off a topic that is already Normal — the app has nothing new to say,
        // and that is the whole point: they asked, so it goes.
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(3001, "/unmute"));

        var repushed = await Run_Until_Async(() => Pushed_NameCount() > before, 20_000);

        _out.WriteLine($"names pushed: {_telegram.Dump_TopicNames()}");

        Assert.True(
            repushed,
            "THE COMMAND WAS SWALLOWED: the owner cleared DND and the app pushed no name, because its "
            + "in-memory note already claimed that name was applied. When that note is wrong — a lost "
            + "write between the two loops, or a name Telegram refused — the topic can never be "
            + $"corrected, which is exactly what they reported.{Environment.NewLine}"
            + $"names: {_telegram.Dump_TopicNames()}");

        // And what it pushed is the undecorated name: no moon, no other glyph.
        Assert.Equal(DISPLAY_NAME, _telegram.Last_TopicName_OrNull());
    }

    int Pushed_NameCount()
    {
        var dump = _telegram.Dump_TopicNames();

        return dump.Length == 0 ? 0 : dump.Split('|').Length;
    }

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    async Task<string> Start_WithChannelAlreadySeen_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, DISPLAY_NAME);
        Seed_OwnerChannel(session.OrchId);

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(3));

        return session.OrchId;
    }

    static string Build_OwnerMessageJson(long updateId, string text)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{updateId},"
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
