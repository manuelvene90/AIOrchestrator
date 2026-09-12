using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// /merge IS AN OWNER REQUEST, AND ITS REPORT MUST REACH THE PHONE. The owner, 2026-09-10: *"when I
/// request the /merge command, I don't get confirmation at the end of the procedure. The solo/sup
/// does merge, clean, etc, but doesn't tell me anything at completion so I never quite know if the
/// merge has actually been done or not."*
///
/// The command landed the ritual as an agent-tagged app entry and tracked nothing: no wait was
/// raised, no reply was watched. The session's report — "merged as 3f2a1c9, 214 tests green" — has
/// no question in it, so OwnerPush_Policy filed it as narration, and the only route left to the
/// phone was the five-minute silent-deadlock release, wearing a "nothing has moved" warning.
///
/// ADAPTED TO THIS BUILD, and the adaptation is the honest half of the re-port. Master could pin
/// this through the PUSH — the report carries no question mark and no marker, so the owner waiting
/// was the only route to the phone. Here everything the supervisor writes is pushed (owner's ruling,
/// 2026-09-09), so "the report arrived" proves nothing about the tracker and the narration that
/// follows it is pushed too. What /merge opening the tracker still decides, and nothing else does,
/// is that the request is FOLLOWED TO ITS END: the report is counted as the answer and the turn end
/// is resolved. That is what the assertions read. The phone-visible "turn ended" wording master
/// asserted is the suppression digest's, and comes back with it in plan 03.
/// </summary>
public class MergeCommandOpensTheReplyTrackerTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445588;
    const long OWNER_USER_ID = 555000113;
    const long TOPIC_ID = 4444;

    const string REPORT_TEXT = "Merged as 3f2a1c9, 214 tests green on the merged tree, worktree and branch removed.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    public MergeCommandOpensTheReplyTrackerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-merge-tracker-tests-{Guid.NewGuid():N}");
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
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheSessionsMergeReport_ReachesThePhone_AndTheTurnEndIsAnnounced()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        // 1 — the owner taps /merge in the topic.
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson("/merge"));

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing("Asked."), 15_000),
            $"/merge was never acknowledged, so this test reached nothing.{Environment.NewLine}{_log.Dump()}");

        Assert.Contains(MergeRitual_Wording.SUBJECT, File.ReadAllText(_paths.Get_OwnerChannelFile(orchId)));

        // 2 — the session does the ritual and reports. No question in it.
        // Indices follow the ritual entry, which the app numbered [1].
        Append_SupervisorEntry(orchId, 2, "merged and cleaned up", REPORT_TEXT);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(REPORT_TEXT), 20_000),
            "the session's completion report never reached the phone at all."
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{string.Join(Environment.NewLine, _telegram.Sent_Texts())}"
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // 3 — THE DEFECT. The request is tracked to its end: the report is counted as the answer and
        // the turn end is resolved, exactly as it is for a typed owner message. Without the tracker
        // there is no pending reply for /merge at all, so nothing here ever runs.
        //
        // Read from the log rather than from a sent message BECAUSE this build says nothing on the
        // phone once the owner has been answered — Build_TurnEndedText returns null and the resolver
        // logs that it did (owner, 2026-09-07: the second of two status messages per exchange). The
        // line is only ever written from inside Announce_SupervisorFree_Async, which only a tracked
        // reply reaches.
        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Turn ended after the owner was answered"), 20_000),
            "the report was pushed but /merge's reply was never resolved — /merge is not being "
            + "tracked as a reply the way an owner message is."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");
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
        return "{\"ok\":true,\"result\":[{\"update_id\":4001,\"message\":{\"message_id\":107,"
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
