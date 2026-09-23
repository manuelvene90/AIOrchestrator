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
/// log said nothing but "Bridge started". Every session stayed down with no reason on screen, and no
/// command could bring them back — /resume woke channels but never touched the pause, and lifting it
/// by hand re-paused on the next tick because the probes still read 99%.
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
        var dateText = pausedUntilUtc.ToString("ddd d MMM", CultureInfo.InvariantCulture);

        Assert.True(
            await Run_Until_Async(engine, () => _telegram.Has_Sent_Containing("still PAUSED"), 20_000),
            "THE DEFECT: the app restarted into a pause persisted six days out and said nothing, so "
            + "every session stayed down with no reason on screen."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}"
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{_telegram.Dump_Sent()}");

        var notice = _telegram.Find_SentContaining("still PAUSED")!;
        Assert.Contains(dateText, notice);
        Assert.Contains("/resume", notice);

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
    /// /resume LIFTS the pause: the sessions come back on the next tick, and the window that caused
    /// it does not re-pause while its reading is still over the threshold — which is what lifting it
    /// by hand did on 2026-09-15.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Resume_LiftsTheRestoredPause_TheSessionsComeBack_AndTheSameWindowDoesNotPauseAgain()
    {
        var resetsAtUtc = _clock.UtcNow.AddDays(6);
        Seed_Pause(resetsAtUtc, WEEKLY_REASON);
        Write_UsageProbe("seven_day", 99, resetsAtUtc);
        var engine = Create_Engine();

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(10));
        Assert.Empty(_spawner.SpawnedCommands);

        _telegram.Queue_Updates(Build_GeneralOwnerMessageJson("/resume"));

        // The positive control for the Assert.Empty above: the same harness DOES spawn once the gate
        // is open, so an empty list there means the pause held, not that nothing could ever spawn.
        Assert.True(
            await Run_Until_Async(engine, () => _spawner.SpawnedCommands.Count > 0, 20_000),
            "/resume did not lift the dispatch pause — the sessions stay down until the window resets."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}"
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{_telegram.Dump_Sent()}");

        Assert.True(_telegram.Has_Sent_Containing("pause lifted"), _telegram.Dump_Sent());
        Assert.Null(_engineState.Load_OrEmpty().DispatchPausedUntilUtc);

        // Past the probe-reading throttle, with the reading still at 99%.
        _clock.Advance(TimeSpan.FromMinutes(2));
        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(10));

        Assert.Null(_engineState.Load_OrEmpty().DispatchPausedUntilUtc);
        Assert.False(_log.Has_Line_Containing("Dispatch PAUSED"), _log.Dump());
    }

    /// <summary>
    /// A LIFT COVERS THE WINDOW IT LIFTED, NOT THE ACCOUNT. A window that comes back LATER than the
    /// lifted one is a new episode and still pauses — otherwise lifting a five-hour pause would
    /// silently disable the weekly guard for the rest of those five hours.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ALiftCoversOnlyTheWindowItLifted_AWindowResettingLaterStillPauses()
    {
        var fiveHourResetUtc = _clock.UtcNow.AddMinutes(40);
        Seed_Pause(fiveHourResetUtc, "the rate_limits.five_hour.used_percentage window was at 97%");
        Write_UsageProbe("five_hour", 97, fiveHourResetUtc);
        var engine = Create_Engine();

        _telegram.Queue_Updates(Build_GeneralOwnerMessageJson("/resume"));

        Assert.True(
            await Run_Until_Async(engine, () => _spawner.SpawnedCommands.Count > 0, 20_000),
            $"setup: /resume did not lift the pause.{Environment.NewLine}{_log.Dump()}");

        var weeklyResetUtc = _clock.UtcNow.AddDays(3);
        Write_UsageProbe("seven_day", 99, weeklyResetUtc);
        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.True(
            await Run_Until_Async(engine, () => _log.Has_Line_Containing("Dispatch PAUSED"), 20_000),
            "the lift of a five-hour pause also swallowed a weekly window that resets days later."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        var pausedUntil = _engineState.Load_OrEmpty().DispatchPausedUntilUtc;
        Assert.NotNull(pausedUntil);
        Assert.True(Math.Abs((pausedUntil.Value - weeklyResetUtc).TotalSeconds) < 2, $"paused until {pausedUntil:o}, expected the weekly reset {weeklyResetUtc:o}");
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

    void Write_UsageProbe(string windowKey, double percent, DateTime resetsAtUtc)
    {
        var unixSeconds = new DateTimeOffset(DateTime.SpecifyKind(resetsAtUtc, DateTimeKind.Utc), TimeSpan.Zero).ToUnixTimeSeconds();

        File.WriteAllText(
            Path.Combine(_paths.Root, $"{Guid.NewGuid():N}.usage.json"),
            $"{{\"rate_limits\":{{\"{windowKey}\":{{\"used_percentage\":{percent},\"resets_at\":{unixSeconds}}}}}}}");
    }

    /// <summary>A message in the General topic — no thread id, which is where /resume is offered.</summary>
    static string Build_GeneralOwnerMessageJson(string text)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":4001,\"message\":{{\"message_id\":91,"
            + $"\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
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
