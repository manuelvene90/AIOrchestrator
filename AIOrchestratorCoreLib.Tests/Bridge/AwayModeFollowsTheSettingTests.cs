using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE AWAY DELAY IS OBEYED BY THE ENGINE, not only by the policy (plan 03 task 18). Owner, 2026-09-23
/// (ai-orchestrator-29 entry [95]): <i>"the away mode is triggered too soon all the time. That also should
/// be a setting."</i> <see cref="AwayModePolicyTests"/> pins the rule with the delay handed in; this pins
/// that the engine hands in the CONFIGURED one — read at the point of effect from config.json — and that
/// its silence clock is one a test can move, so an hour of silence costs seconds rather than an hour.
///
/// <para>
/// The orchestration goes QUIET the ordinary way — three supervisor messages reach the phone unanswered
/// (<c>phone.push = everything</c>, so narration is sent and counts) — which is the half of away mode's
/// condition this file does not vary. Only the owner's silence moves.
/// </para>
/// </summary>
public class AwayModeFollowsTheSettingTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445511;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 7474;

    /// <summary>The first words of <see cref="AwayMode_Policy.AWAY_ON_NOTICE"/>, as the phone shows them.</summary>
    const string AWAY_ON = "AWAY MODE ON";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingLog_Fake _log = new();
    readonly PhoneTimelineTelegram_Fake _telegram = new(TOPIC_ID);
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);

    // Set once by Build_Engine, which each fact calls first.
    string _channelFile = "";

    public AwayModeFollowsTheSettingTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-away-setting-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// AN HOUR, STATED IN config.json: twenty minutes of silence — five past today's fifteen — is not away,
    /// and the hour is. Against the compiled fifteen the first half fails; against the wall clock the second
    /// never arrives.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderAConfigStatingAnHour_TwentyMinutesOfSilenceIsNotAway_AndTheHourIs()
    {
        var engine = Build_Engine(awayAfterMinutes: 60);

        await Run_WhileAsync(engine, async () =>
        {
            await Make_TheOrchestrationGoQuiet_Async();

            _clock.Advance(TimeSpan.FromMinutes(20));
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(30));

            Assert.False(_telegram.Has_Sent_Containing(AWAY_ON), $"away mode started after 20 minutes of silence under a 60-minute setting.{Environment.NewLine}{_log.Dump()}");

            _clock.Advance(TimeSpan.FromMinutes(40));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Has_Sent_Containing(AWAY_ON), 20_000),
                $"away mode never started after an hour of silence under a 60-minute setting.{Environment.NewLine}{_telegram.Dump()}{Environment.NewLine}{_log.Dump()}");
        });
    }

    /// <summary>
    /// ZERO, STATED IN config.json: a day of silence with an orchestration quiet the whole time, and away
    /// mode never starts by itself.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderAConfigStatingZero_AwayModeNeverStartsByItself()
    {
        var engine = Build_Engine(awayAfterMinutes: 0);

        await Run_WhileAsync(engine, async () =>
        {
            await Make_TheOrchestrationGoQuiet_Async();

            _clock.Advance(TimeSpan.FromDays(1));
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(30));

            Assert.False(_telegram.Has_Sent_Containing(AWAY_ON), $"away mode started under away.afterMinutes = 0.{Environment.NewLine}{_log.Dump()}");
        });
    }

    IBridgeEngine Build_Engine(int awayAfterMinutes)
    {
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},\"telegramOwnerUserId\":{OWNER_USER_ID},"
            + $"\"phone\":{{\"push\":\"everything\"}},\"away\":{{\"afterMinutes\":{awayAfterMinutes}}}}}");

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);

        var session = launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        _channelFile = channelFile;

        return BridgeEngine_Factory.Create_WithDecisionState(
            _paths, configProvider, _store, launcher, _log, _telegram,
            _engineState, _clock,
            BridgeTestTiming.Fast());
    }

    /// <summary>Three supervisor messages reach the phone and go unanswered: the 🤐 notice is the proof.</summary>
    async Task Make_TheOrchestrationGoQuiet_Async()
    {
        // Past the tailer's first pass, which baselines a channel at its current length.
        await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(3));

        for (var index = 1; index <= 3; index++)
        {
            File.AppendAllText(_channelFile, $"\n## [{index}] FROM supervisor — {DateTime.Now:yyyy-MM-dd HH:mm} — progress {index}\nStep {index} of the rebuild is done.\n");

            var sent = $"Step {index} of the rebuild is done.";

            Assert.True(
                await Wait_Until_Async(() => _telegram.Has_Sent_Containing(sent), 20_000),
                $"supervisor message {index} never reached the phone.{Environment.NewLine}{_log.Dump()}");
        }

        Assert.True(
            await Wait_Until_Async(() => _telegram.Has_Sent_Containing("going quiet here"), 20_000),
            $"the orchestration never went quiet after three unanswered messages.{Environment.NewLine}{_telegram.Dump()}{Environment.NewLine}{_log.Dump()}");

        Assert.False(_telegram.Has_Sent_Containing(AWAY_ON), "away mode started before any silence had passed");
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

    /// <summary>
    /// Polls, and MOVES THE CLOCK WITH REAL TIME while it does. The injected clock also drives the tailer's
    /// trailing-entry quiet window, so a clock that stood still between the test's jumps would hold every
    /// channel entry back for ever — the first run of this file measured exactly that ("supervisor message
    /// 1 never reached the phone"). Only the two deliberate jumps are larger than the time that passed.
    /// </summary>
    async Task<bool> Wait_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
                return true;

            await Task.Delay(100);
            _clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        return condition();
    }
}
