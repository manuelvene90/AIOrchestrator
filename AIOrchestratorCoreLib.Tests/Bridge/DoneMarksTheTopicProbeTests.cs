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
/// /done MUST PUT THE TICK ON THE TOPIC NAME — that name is the whole point of the command.
///
/// Owner, 2026-08-24: *"another problem with the /done command. The check emoji is not being added
/// to the topic name, so in the topic list it's not evident that it is finished"*.
///
/// The command's own confirmation says "marked finished", and the flag really is persisted — so
/// everything on the state side reads correct, and the failure has to be observed where the owner
/// observes it: in the name handed to editForumTopic. That is what this drives.
///
/// It also pins the WAKE, which is the other half of the same glyph and the easier one to break by
/// accident: a finished topic the owner writes into again is not finished any more, so the tick has
/// to come back off. Asserting only the "on" half would let a change that never cleared it pass.
/// </summary>
public class DoneMarksTheTopicProbeTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 5150;

    const string DISPLAY_NAME = "AI-Orch · pc icon stuck";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly RecordingTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;
    readonly ITestOutputHelper _out;

    public DoneMarksTheTopicProbeTests(ITestOutputHelper output)
    {
        _out = output;

        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-done-{Guid.NewGuid():N}");
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
    public async Task Done_PutsTheTickOnTheTopicName_AndWritingAgainTakesItOff()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        // 1 — the owner marks it finished from the topic.
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(1001, "/done"));

        Assert.True(
            await Run_Until_Async(() => _store.Get_Session_OrNull(orchId)?.Done == true, 20_000),
            $"/done never even set the flag, so this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        var marked = await Run_Until_Async(() => Last_NameHasTick(), 20_000);

        _out.WriteLine($"topic names pushed after /done: {_telegram.Dump_TopicNames()}");

        Assert.True(
            marked,
            "THE TICK NEVER REACHED THE TOPIC NAME: the flag is set and the command confirms it is "
            + "finished, but the topic list shows nothing, so a finished endeavour is "
            + $"indistinguishable from a live one.{Environment.NewLine}names: {_telegram.Dump_TopicNames()}");

        // 2 — and it comes back OFF when they write into the finished topic again. Without this half
        // a change that simply never cleared the tick would keep the assertion above green.
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(1002, "something else came up"));

        Assert.True(
            await Run_Until_Async(() => !Last_NameHasTick(), 20_000),
            "the owner wrote into a finished topic and it still reads as finished. "
            + $"names: {_telegram.Dump_TopicNames()}");
    }

    /// <summary>
    /// A SECOND /done MUST NOT UNDO THE FIRST — the command is no longer a toggle.
    ///
    /// It was one, and it never survived contact with the owner. Every use of it in this machine's
    /// history was undone within seconds of being set: 2026-08-21 14:21:48 marked, 14:22:05 cleared;
    /// 2026-08-24 08:53:45 / 08:54:02; 2026-08-24 21:48:32 / 21:48:55. A forum rename takes a moment
    /// to surface in Telegram's topic list, so a second press is the natural response to seeing
    /// nothing — and it silently removed the mark.
    ///
    /// Owner, 2026-08-25, after all three: *"the /done command still doesn't put the check in the
    /// topic name. It simply doesn't happen — it sends a confirmation message that starts with the
    /// check, but doesn't change the topic name."* It had been added, three times, and removed by
    /// their own second press each time.
    ///
    /// Making the two replies distinguishable was the previous fix and it did not hold: the second
    /// press happens before the first reply is read. Repeating the command is now simply harmless.
    /// Un-finishing keeps its own route — writing in the topic — which the test above pins.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task PressingDoneTwice_LeavesItFinished_AndNeverSaysItIsNot()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(2001, "/done"));

        Assert.True(
            await Run_Until_Async(() => Last_NameHasTick(), 20_000),
            $"the first /done never marked it, so the second proves nothing.{Environment.NewLine}{_log.Dump()}");

        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(2002, "/done"));

        Assert.True(
            await Run_Until_Async(() => _telegram.SentTexts().Count(text => text.Contains("marked finished", StringComparison.Ordinal)) >= 2, 20_000),
            $"the second /done was never acknowledged.{Environment.NewLine}{_log.Dump()}");

        // THE POINT OF THE WHOLE TEST. Give the old toggle every chance to fire before asserting.
        await Run_Until_Async(() => !Last_NameHasTick(), BridgeTestTiming.Window_ForTicks(10));

        Assert.True(
            Last_NameHasTick(),
            $"a second /done took the tick back off — it is a toggle again. names: {_telegram.Dump_TopicNames()}");

        Assert.DoesNotContain(
            _telegram.SentTexts(),
            text => text.Contains("NOT finished", StringComparison.Ordinal));
    }

    /// <summary>
    /// The confirmation must QUOTE the name it just set. The owner's report was that the reply and
    /// the topic list disagreed, and they had no way to tell which was right without going to hunt
    /// for the topic — so the reply now carries the answer.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheConfirmation_QuotesTheTopicNameItJustSet()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(2101, "/done"));

        Assert.True(
            await Run_Until_Async(() => Last_NameHasTick(), 20_000),
            $"/done never marked it.{Environment.NewLine}{_log.Dump()}");

        var appliedName = _telegram.Last_TopicName_OrNull();

        Assert.True(
            await Run_Until_Async(
                () => _telegram.SentTexts().Any(text => appliedName != null && text.Contains(appliedName, StringComparison.Ordinal)),
                10_000),
            "the confirmation never quoted the topic name it set, so the owner still has to go and "
            + $"look.{Environment.NewLine}name: {appliedName}{Environment.NewLine}"
            + $"replies: {string.Join(" || ", _telegram.SentTexts())}");
    }

    bool Last_NameHasTick()
    {
        var name = _telegram.Last_TopicName_OrNull();

        return name != null && name.Contains(TelegramDeliveryMode_Glyphs.DONE, StringComparison.Ordinal);
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
