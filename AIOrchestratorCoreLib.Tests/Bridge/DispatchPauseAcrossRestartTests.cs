using System.Globalization;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A DISPATCH PAUSE THAT OUTLIVES A RESTART, driven through the real engine.
///
/// <para>
/// 2026-09-15: the weekly window crossed 95% at 11:39 and the pause was persisted until Monday
/// 21 Sep 08:00 UTC — six days out — while the alert said only "Resuming automatically at 08:00
/// UTC". At 12:48 the machine ran out of memory and every session died. The owner restarted the app
/// twice: the pause was restored from engine state, the watchdog is skipped while paused, and the
/// log said nothing but "Bridge started". Every session stayed down with no reason on screen. (The lift
/// itself is the /resume_dispatch lever of origin/master's 2026-09-18 design, covered in
/// HighRiskAndDeadlineProbeTests; this file keeps the restored notice and the app's button.)
/// </para>
/// </summary>
public class DispatchPauseAcrossRestartTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const string WEEKLY_REASON = "the rate_limits.seven_day.used_percentage window was at 95%";

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly CapturingTelegram_Fake _telegram = new();
    readonly RecordingSpawner_Fake _spawner = new();

    /// <summary>At the wall clock: the usage-probe reader filters windows on <c>DateTime.Now</c>.</summary>
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);

    public DispatchPauseAcrossRestartTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-dispatchpause-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, _spawner, _log);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// The restored pause SAYS SO at startup — in the app's log and on the phone — with the DATE it
    /// lifts and the command that lifts it now. And it still holds: nothing is spawned.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APauseRestoredAtStartup_IsAnnouncedWithItsDateAndTheWayToLiftIt_AndStillHolds()
    {
        var pausedUntilUtc = _clock.UtcNow.AddDays(6);
        Seed_Pause(pausedUntilUtc, WEEKLY_REASON);
        var engine = Create_Engine();
        var dateText = pausedUntilUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.True(
            await Run_Until_Async(engine, () => _telegram.Has_Sent_Containing("still PAUSED"), 20_000),
            "THE DEFECT: the app restarted into a pause persisted six days out and said nothing, so "
            + "every session stayed down with no reason on screen."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}"
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{_telegram.Dump_Sent()}");

        var notice = _telegram.Find_SentContaining("still PAUSED")!;
        Assert.Contains(dateText, notice);
        Assert.Contains("/resume_dispatch", notice);

        // The desktop app shows the log, not the phone — the same line has to be there.
        Assert.True(_log.Has_Line_Containing("still PAUSED"), _log.Dump());
        Assert.True(_log.Has_Line_Containing(dateText), _log.Dump());

        // Once per start, not once per tick.
        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(10));
        Assert.Equal(1, _telegram.Count_Sent_Containing("still PAUSED"));

        // The pause itself is untouched: the watchdog did not run, so nothing was spawned.
        Assert.Empty(_spawner.SpawnedCommands);
        Assert.NotNull(_engineState.Load_OrEmpty().DispatchPausedUntilUtc);
    }

    /// <summary>
    /// THE APP'S BUTTON LIFTS IT through the same lever as /resume_dispatch: the pause is lifted, the
    /// probes written before the lift stop counting, and the sessions come back on the next tick.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheAppButton_LiftsTheRestoredPause_AndTheSessionsComeBack()
    {
        var resetsAtUtc = _clock.UtcNow.AddDays(6);
        Seed_Pause(resetsAtUtc, WEEKLY_REASON);
        var probe = Write_UsageProbe("seven_day", 99, resetsAtUtc);
        File.SetLastWriteTimeUtc(probe, _clock.UtcNow.AddMinutes(-10));
        var engine = Create_Engine();

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(10));
        Assert.Empty(_spawner.SpawnedCommands);

        Assert.True(engine.Lift_DispatchPause_ByOwner(), "the button found nothing paused");

        // The positive control for the Assert.Empty above: the same harness DOES spawn once the gate
        // is open, so an empty list there means the pause held, not that nothing could ever spawn.
        Assert.True(
            await Run_Until_Async(engine, () => _spawner.SpawnedCommands.Count > 0, 20_000),
            "the app's button did not lift the dispatch pause — the sessions stay down until the window resets."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}"
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{_telegram.Dump_Sent()}");

        Assert.Null(_engineState.Load_OrEmpty().DispatchPausedUntilUtc);
        Assert.NotNull(_engineState.Load_OrEmpty().LimitProbeCutoffUtc);
        Assert.False(engine.Lift_DispatchPause_ByOwner(), "a second press found a pause that was already lifted");
    }

    IBridgeEngine Create_Engine()
    {
        return BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, _telegram,
            _engineState, _clock,
            BridgeTestTiming.Fast());
    }

    void Seed_Pause(DateTime pausedUntilUtc, string reason)
    {
        _engineState.Save(new EngineStateSnapshot
        {
            DispatchPausedUntilUtc = pausedUntilUtc,
            DispatchPauseReason = reason,
        });
    }

    string Write_UsageProbe(string windowKey, double percent, DateTime resetsAtUtc)
    {
        var unixSeconds = new DateTimeOffset(DateTime.SpecifyKind(resetsAtUtc, DateTimeKind.Utc), TimeSpan.Zero).ToUnixTimeSeconds();

        var path = Path.Combine(_paths.Root, $"{Guid.NewGuid():N}.usage.json");

        File.WriteAllText(
            path,
            $"{{\"rate_limits\":{{\"{windowKey}\":{{\"used_percentage\":{percent},\"resets_at\":{unixSeconds}}}}}}}");

        return path;
    }

    static async Task<bool> Run_Until_Async(IBridgeEngine engine, Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);
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
