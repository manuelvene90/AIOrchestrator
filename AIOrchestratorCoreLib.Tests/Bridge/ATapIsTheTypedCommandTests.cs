using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A TAP ON A BAR BUTTON IS THE TYPED COMMAND IT NAMES (plan 03 Task 5b, ruling R16). The owner,
/// 2026-09-23: <i>"I absolutely need to be able to choose which command and in which order to have the
/// command buttons under the pulse message."</i> Task 5 made the bars configurable and found that only
/// the verbs with a dedicated case in the tap handler could be drawn; every other verb is now handed to
/// the SAME chain a typed command is dispatched by, so a button can never mean something its command
/// does not.
///
/// <para>
/// MEASURED THROUGH THE REAL ENGINE, AGAINST THE TYPED COMMAND, in the same topic. Each fact types the
/// command and taps its button and compares what reached the phone — never a restated expected string,
/// which would pin one of the two routes and let the other drift. <see cref="ScriptedInbound_Fake"/>
/// because it keeps the keyboard a message was sent with (the /model answer IS its buttons).
/// </para>
/// </summary>
public class ATapIsTheTypedCommandTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    /// <summary>The PULSE message's id as far as a tap is concerned — any id; the handler reads the payload.</summary>
    const long PULSE_MESSAGE_ID = 4000;

    /// <summary>The first half of /switch's own two-step reply — the words that ask for the repeat.</summary>
    const string SWITCH_ARMED_FRAGMENT = "Send /switch again within 2 minutes";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly ScriptedInbound_Fake _telegram = new();
    readonly RecordingLog_Fake _log = new();

    long _nextUpdateId = 9000;

    public ATapIsTheTypedCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-tap-is-typed-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// /cost had no tap case, so Task 5 refused it from every bar. Tapped, it must send exactly what the
    /// typed /cost sends — in the orchestration's topic, and in General, where the same command answers a
    /// different question ("per orchestration" rather than "per session"). The General half is hazard 3:
    /// the tap's command is scoped to the topic its button sat in, exactly like typed text there, and the
    /// two scopes' answers are asserted DIFFERENT so the comparison cannot pass by both being the same
    /// text anywhere. The tap is answered (hazard 5: an unanswered callback spins on the phone).
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TappingCost_SendsWhatTypingCostSends_InTheTopicAndInGeneral()
    {
        var engine = Build_Engine();

        IReadOnlyList<(string Text, string Labels)> typedInTopic = [], tappedInTopic = [], typedInGeneral = [], tappedInGeneral = [];

        await Run_WhileAsync(engine, async () =>
        {
            await Wait_ForStartup_Async();

            typedInTopic = await Capture_RepliesTo_Async(() => Owner_Types("/cost", TOPIC_ID), "typed /cost in the topic");
            tappedInTopic = await Capture_RepliesTo_Async(() => Owner_Taps($"cmd:cost:{TOPIC_ID}", TOPIC_ID), "tapped cost in the topic");
            typedInGeneral = await Capture_RepliesTo_Async(() => Owner_Types("/cost", threadId: null), "typed /cost in General");
            tappedInGeneral = await Capture_RepliesTo_Async(() => Owner_Taps("cmd:cost:0", threadId: null), "tapped cost in General");
        });

        Assert.Equal(typedInTopic, tappedInTopic);
        Assert.Equal(typedInGeneral, tappedInGeneral);
        Assert.NotEqual(typedInTopic, typedInGeneral);

        Assert.Equal(2, _telegram.Answered_Callbacks);
        Assert.DoesNotContain(tappedInTopic.Concat(tappedInGeneral), reply => reply.Text.Contains("older version of the app", StringComparison.Ordinal));
    }

    /// <summary>
    /// A bare /model answers with BUTTONS — the reply is its keyboard. The tapped /model must draw the
    /// same text with the same buttons, which is only true if it reached the same handler with the same
    /// words: a second implementation would have to re-derive the choices and the payloads.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TappingModel_DrawsTheButtonsABareTypedModelDraws()
    {
        var engine = Build_Engine();

        IReadOnlyList<(string Text, string Labels)> typed = [], tapped = [];

        await Run_WhileAsync(engine, async () =>
        {
            await Wait_ForStartup_Async();

            typed = await Capture_RepliesTo_Async(() => Owner_Types("/model", TOPIC_ID), "typed /model");
            tapped = await Capture_RepliesTo_Async(() => Owner_Taps($"cmd:model:{TOPIC_ID}", TOPIC_ID), "tapped model");
        });

        Assert.Contains(typed, reply => reply.Labels.Length > 0);
        Assert.Equal(typed, tapped);
        Assert.Equal(1, _telegram.Answered_Callbacks);
    }

    /// <summary>
    /// HAZARD 1 — A COMMAND CONFIRMED BY SENDING IT TWICE. /switch arms on the first send and acts on the
    /// repeat inside two minutes. Two taps must mean exactly two typed sends: the first ARMS and no more
    /// (a tap never counts as its own confirmation), the second is the repeat. Run once per route and
    /// asserted identically, so the routes cannot differ. Both handover entries are written so the arm is
    /// reached whichever direction this orchestration's shape points.
    /// </summary>
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoTapsOnSwitch_MeanExactlyTwoTypedSwitches(bool byTap)
    {
        var engine = Build_Engine();
        var orchId = _store.Load_All().Single().OrchId;

        File.AppendAllText(
            _paths.Get_OwnerChannelFile(orchId),
            "\n## [7] FROM supervisor — 2026-09-23 09:00 — HANDOVER — where the work stands\nnotes\n"
            + "\n## [8] FROM solo — 2026-09-23 09:01 — HANDOVER — where the work stands\nnotes\n");

        IReadOnlyList<(string Text, string Labels)> first = [], second = [];

        await Run_WhileAsync(engine, async () =>
        {
            await Wait_ForStartup_Async();

            Action send = byTap
                ? () => Owner_Taps($"cmd:switch:{TOPIC_ID}", TOPIC_ID)
                : () => Owner_Types("/switch", TOPIC_ID);

            first = await Capture_RepliesTo_Async(send, "the first /switch");
            second = await Capture_RepliesTo_Async(send, "the second /switch");
        });

        Assert.Single(first);
        Assert.Contains(SWITCH_ARMED_FRAGMENT, first[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(second, reply => reply.Text.Contains(SWITCH_ARMED_FRAGMENT, StringComparison.Ordinal));
        Assert.Equal(byTap ? 2 : 0, _telegram.Answered_Callbacks);
    }

    /// <summary>
    /// A TAPPED MODE COMMAND IS NOT "THE OWNER SPEAKING". Any tap in a batch lifts app-wide DND before the
    /// taps are handled — right for an answer, wrong for the toggle that governs DND itself: a tapped
    /// 🌙 /dnd_all while everything is deferred was lifted by its own batch and then toggled straight back
    /// ON, so the button could never turn DND off. The typed /dnd_all never had that problem, because a
    /// command is not a routable message. The rule the engine's own comment states — "tapping ANYTHING
    /// (except a mode command)" — is now what it does.
    /// </summary>
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DndAll_WhileEverythingIsDeferred_TurnsItOff_TappedOrTyped(bool byTap)
    {
        var engine = Build_Engine();
        List<bool> mutedChanges = [];

        IReadOnlyList<(string Text, string Labels)> replies = [];

        await Run_WhileAsync(engine, async () =>
        {
            await Wait_ForStartup_Async();

            engine.Set_TelegramMuted(true);
            engine.MutedChanged += muted => { lock (mutedChanges) mutedChanges.Add(muted); };

            replies = await Capture_RepliesTo_Async(
                byTap ? () => Owner_Taps("cmd:dnd_all:0", threadId: null) : () => Owner_Types("/dnd_all", threadId: null),
                "/dnd_all while deferred");
        });

        Assert.Contains(replies, reply => reply.Text.Contains("everywhere: messages ON", StringComparison.Ordinal));

        lock (mutedChanges)
            Assert.Equal([false], mutedChanges);
    }

    // ---------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------

    IBridgeEngine Build_Engine()
    {
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);

        var session = launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        return BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    /// <summary>PULSE and the dashboard are up and the startup traffic has settled, so what follows is the command's.</summary>
    async Task Wait_ForStartup_Async()
    {
        Assert.True(
            await Wait_Until_Async(() => _telegram.Find_SentContaining("PULSE") != null, 25_000),
            $"PULSE was never posted.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");

        await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(10));
    }

    /// <summary>
    /// What the phone received in answer to one owner action: every NEW message, as its CURRENT text and
    /// keyboard, leaving out the two surfaces the app repaints on its own (PULSE and the General dashboard).
    /// It waits for the first reply and then for the traffic to go quiet, so a reply sent in two chunks is
    /// captured whole.
    /// </summary>
    async Task<IReadOnlyList<(string Text, string Labels)>> Capture_RepliesTo_Async(Action ownerAction, string what)
    {
        var before = _telegram.Sent_WithIds.Count;

        ownerAction();

        Assert.True(
            await Wait_Until_Async(() => Replies_Since(before).Count > 0, 20_000),
            $"{what} got no reply.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");

        await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(10));

        return Replies_Since(before);
    }

    IReadOnlyList<(string Text, string Labels)> Replies_Since(int sentIndex)
    {
        return [.. _telegram.Sent_WithIds
            .Skip(sentIndex)
            .Where(sent => !sent.Text.Contains("PULSE", StringComparison.Ordinal)
                && !sent.Text.Contains(GeneralDashboard_Composer.HEADING, StringComparison.Ordinal))
            .Select(sent => _telegram.Current_Of_OrNull(sent.Id) ?? (sent.Text, ""))];
    }

    void Owner_Types(string text, long? threadId)
    {
        var updateId = _nextUpdateId++;
        var thread = threadId == null ? "" : $"\"message_thread_id\":{threadId},";

        _telegram.Queue_Updates(
            $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{updateId},"
            + $"{thread}\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}");
    }

    void Owner_Taps(string callbackData, long? threadId)
    {
        var updateId = _nextUpdateId++;
        var thread = threadId == null ? "" : $"\"message_thread_id\":{threadId},";

        _telegram.Queue_Updates(
            $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"callback_query\":{{\"id\":\"cbq-{updateId}\","
            + $"\"data\":\"{callbackData}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{PULSE_MESSAGE_ID},{thread}"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}]}}");
    }

    static async Task Run_WhileAsync(IBridgeEngine engine, Func<Task> body)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);

        try
        {
            await body();
        }
        finally
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
}
