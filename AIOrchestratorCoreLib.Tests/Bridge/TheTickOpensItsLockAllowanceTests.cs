using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Channels;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE MIRROR TICK MUST ACTUALLY OPEN THE ALLOWANCE — the one line that makes the bound real.
/// <para>
/// <c>ChannelWrite_Lock</c> caps a tick's waiting only when somebody opens an allowance for the
/// flow. Five tests pin the capping itself, but they drive the lock directly, so deleting the
/// <c>using</c> line from <c>Execute_MirrorTick_Async</c> reddened none of them: the bound would be
/// perfectly implemented and never switched on. That gap was stated in the commit rather than
/// implied, and this closes it.
/// </para>
/// <para>
/// NO TIMING AND NO CONTENTION. The allowance is observed rather than measured: a log that records
/// whether one was in force whenever the engine logged. An allowance is per-async-flow, so seeing
/// one at all is proof the tick opened it — nothing else in the process can put one there. The
/// trigger is a malformed request file, which the tick's FIRST step reports, so the observation
/// needs neither a lock nor a stopwatch.
/// </para>
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class TheTickOpensItsLockAllowanceTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly IBridgeEngine _engine;
    readonly AllowanceObserving_Log_Fake _log;

    public TheTickOpensItsLockAllowanceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-tick-allowance-wiring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"telegramItalianLayer\":false}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _log = new AllowanceObserving_Log_Fake();

        var store = OrchestrationSessionStore_Factory.Create(_paths);
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(
            _paths, configProvider, store, new RecordingSpawner_Fake(), _log);

        _engine = BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, configProvider, store, launcher, _log, new FailableTelegram_Fake());
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// The control for this is deleting the <c>using var tickAllowance = ...</c> line from
    /// <c>Execute_MirrorTick_Async</c>: the engine still logs, so the run still completes, and the
    /// observation flips to "no allowance was in force".
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WhateverTheTickLogs_ItLogsInsideAnAllowance()
    {
        File.WriteAllText(Path.Combine(_paths.RequestsFolder, "broken.json"), "this is not json");

        Assert.True(
            await Run_Until_Async(() => _log.Observations_Count > 0, 20_000),
            "the tick never logged anything, so there was nothing to observe and this test proves nothing "
            + "either way. The malformed request file is supposed to be reported by the tick's first step.");

        Assert.True(
            _log.Saw_AnAllowanceInForce,
            "THE DEFECT: the mirror tick logged without any lock allowance in force, which means "
            + "Execute_MirrorTick_Async is not opening one. ChannelWrite_Lock's cap is then dead code and the "
            + "tick's waiting is once again bounded only by (appends x the per-call budget).");
    }

    async Task<bool> Run_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
                break;

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

        return condition();
    }
}

/// <summary>
/// Records, at every log call, whether a tick allowance was in force on the calling flow. The log is
/// used because it is already injectable and because the engine logs from inside the tick — the
/// allowance cannot be observed from outside the flow that owns it.
/// </summary>
internal sealed class AllowanceObserving_Log_Fake : IOrchestrationLog
{
    readonly object _lock = new();
    int _observations;
    bool _sawAllowance;

    public int Observations_Count
    {
        get
        {
            lock (_lock)
                return _observations;
        }
    }

    public bool Saw_AnAllowanceInForce
    {
        get
        {
            lock (_lock)
                return _sawAllowance;
        }
    }

    public void Log_Info(string orchId, string message)
    {
        Observe();
    }

    public void Log_Warning(string orchId, string message)
    {
        Observe();
    }

    public void Log_Error(string orchId, string message, Exception? exception)
    {
        Observe();
    }

    /// <summary>Nothing subscribes here; the observation happens on the calls themselves.</summary>
    public event Action<IOrchestrationLogEntry>? EntryLogged
    {
        add { }
        remove { }
    }

    void Observe()
    {
        // Read on the CALLING flow: an allowance is per-async-flow, so this is only non-null when the
        // caller is running inside one.
        var remaining = ChannelWrite_Lock.Get_RemainingTickAllowance();

        lock (_lock)
        {
            _observations++;

            if (remaining != null)
                _sawAllowance = true;
        }
    }
}
