using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

using static AIOrchestratorCoreLib.Tests.Bridge.SendNowSkipsTheWindowTests;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE REAL ENGINE SERVES THE OWNER'S WINDOW (plan 03 task 13). Owner, 2026-09-23 (ai-orchestrator-29
/// entry [9]): <i>"the buffer time … now is only very short, but it should be a buffer of 6 seconds,
/// giving me the time to press wait if I need."</i> Classic states <c>phone.aggregationSeconds</c> 6 with
/// no finished-message discount; the engine must read it from config.json on every flush.
///
/// <para>
/// THE TIMING CARRIES NO WINDOW HERE — <c>Create_Custom_WindowFromSettings</c>, fast ticks and the
/// production shape for the window — because a window named by the timing outranks the setting, which is
/// what keeps every other engine test on its 1 s or 60 s window. The harness is
/// <see cref="SendNowSkipsTheWindowTests"/>', whose helpers are internal so this file shares them.
/// </para>
/// </summary>
public class OwnerAggregationWindowFollowsTheSettingsTests : IDisposable
{
    const string FINISHED_MESSAGE = "ok, go ahead.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly ScriptedInbound_Fake _telegram = new();
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);

    public OwnerAggregationWindowFollowsTheSettingsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-aggregation-window-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // NO PRESET, which is classic: the ✓ receipt is a message (phone.receipts = ticks), so "buffered"
        // is observable as the ✓ being sent.
        Write_Config(phoneBlockOrNull: null);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);
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

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// UNDER CLASSIC, "ok, go ahead." IS STILL IN THE BUFFER TWO SECONDS AFTER ITS ✓ — where the fork's
    /// discount had already taken it — so ⏸ Wait can still reach it; and it IS delivered once the six
    /// seconds are out. Both bounds, because only the pair says "six, not two and not never".
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_AFinishedMessage_IsStillBufferedAfterTwoSeconds_AndDeliveredAfterSix()
    {
        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json(FINISHED_MESSAGE, 9801, 801)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Sent_WithIds.Any(sent => sent.Text == "✓"), 20_000),
                $"the message was never acknowledged, so nothing below measures the window.{Environment.NewLine}{_telegram.Dump_Sent()}");

            await Wait_Until_Async(() => false, 2_500);

            Assert.False(
                Channel_Contains(channelFile, FINISHED_MESSAGE),
                $"a finished message left the buffer inside 2.5 s under classic — the 2-second discount is still being served.{Environment.NewLine}{_log.Dump()}");

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, FINISHED_MESSAGE), 12_000),
                $"the message was never delivered under classic's 6-second window.{Environment.NewLine}{_log.Dump()}");
        });
    }

    /// <summary>
    /// CONFIG.JSON BEATS CLASSIC, AND AN EDIT IS OBEYED ON THE NEXT FLUSH. A 60-second window in the file
    /// holds a fragment past classic's 6 s (the third rung, at the point of effect); rewriting the file to
    /// 1 s releases it within seconds, with the same engine — a window cached anywhere would hold it for
    /// the rest of the minute.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AWindowInConfigJson_BeatsClassic_AndARewriteIsObeyedWithoutARestart()
    {
        Write_Config("""{"aggregationSeconds":60,"finishedMessageSeconds":60}""");

        var engine = Build_Engine();
        var channelFile = Start_Orchestration();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json(FRAGMENT, 9811, 811)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Sent_WithIds.Any(sent => sent.Text == "✓"), 20_000),
                $"the message was never acknowledged.{Environment.NewLine}{_telegram.Dump_Sent()}");

            await Wait_Until_Async(() => false, 7_500);

            Assert.False(
                Channel_Contains(channelFile, FRAGMENT),
                $"the message left after classic's 6 s although config.json names 60 — the file's window is not the one served.{Environment.NewLine}{_log.Dump()}");

            Write_Config("""{"aggregationSeconds":1,"finishedMessageSeconds":1}""");

            Assert.True(
                await Wait_Until_Async(() => Channel_Contains(channelFile, FRAGMENT), 10_000),
                $"config.json was rewritten to a 1-second window and the message stayed held — the engine is serving a cached window.{Environment.NewLine}{_log.Dump()}");
        });
    }

    /// <summary>
    /// The fixture's config, with an optional <c>phone</c> block. The stamp is pushed forward explicitly
    /// because the provider reloads on the file's write stamp, and two writes inside one timestamp tick
    /// would otherwise read as no change at all.
    /// </summary>
    void Write_Config(string? phoneBlockOrNull)
    {
        var phone = phoneBlockOrNull == null ? "" : $",\"phone\":{phoneBlockOrNull}";

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}{phone}}}");

        File.SetLastWriteTimeUtc(_paths.ConfigFile, DateTime.UtcNow.AddSeconds(Interlocked.Increment(ref _configWrites)));
    }

    int _configWrites;

    IBridgeEngine Build_Engine()
    {
        return BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, _telegram,
            _engineState, _clock,
            BridgeEngineTiming_Factory.Create_Custom_WindowFromSettings(
                BridgeTestTiming.TICK_MILLISECONDS,
                BridgeTestTiming.RETRY_BACKOFF_SECONDS,
                BridgeTestTiming.TICK_LOCK_ALLOWANCE_MILLISECONDS,
                BridgeTestTiming.TRAILING_ENTRY_QUIET_MILLISECONDS));
    }

    string Start_Orchestration()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        return channelFile;
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
            await Stop_Async(cancellation, loop);
        }
    }
}
