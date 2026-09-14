using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.SessionRunner;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// /effort FROM THE PHONE, AND WHAT "APPLY" MEANS FOR A SESSION THE BRIDGE DRIVES.
///
/// <para>
/// The dial is one apply path (decision 24): store the override, then kill and respawn so the flag
/// reaches `claude` at spawn. A BRIDGE-DRIVEN session has no spawn to reach and no pid file to kill
/// — the dispatcher runs its turns — so the same path must store the override and stop there.
/// </para>
/// <para>
/// WHAT COUNTS AS BRIDGE-DRIVEN IS TWO FACTS, NOT ONE, and the second is the whole subject of the
/// first case below: the config runner AND a print-session registration. They disagree for as long
/// as a runner flip takes to land — the owner sets the supervisor to `print` while its terminal
/// session is still alive in its window — and in that window a config-only answer would store the
/// override, restart nothing, and tell the owner "nothing was restarted, this role is driven by the
/// app" about a session sitting on their screen whose model never changes.
/// </para>
/// <para>
/// All three runner situations are pinned here on purpose. A "does not kill" claim on its own passes
/// just as well when the dial does nothing at all, so the two that DO restart are what make the one
/// that does not mean something — the same reason a guard must never have two routes to green.
/// </para>
/// </summary>
public class EffortDialOnABridgeDrivenSupervisorTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 6161;

    /// <summary>Not a shell pid, so a kill that DOES run still gets as far as deleting the file.</summary>
    const string PLANTED_PID = "999";

    /// <summary>
    /// An hour, so the dispatcher parks every registered member at its coalesce gate for the whole
    /// test instead of running a turn — which on this host would invoke the REAL `claude`, since the
    /// engine builds its dispatcher with <c>ClaudeInvocation_Resolver.Resolve_ForThisOs()</c> and
    /// exposes no seam. A member's turn sources are its spokes, never the owner channel, so the
    /// owner-breath shortcut in <c>CoalesceWindow_Policy</c> cannot fire for one.
    /// </summary>
    const int PARKED_COALESCE_SECONDS = 3600;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingSpawner_Fake _spawner = new();
    readonly RecordingLog_Fake _log = new();
    readonly SoundRecordingTelegram_Fake _telegram = new();
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);

    IOrchestrationLauncher? _launcher;
    IBridgeEngine? _engine;
    CancellationTokenSource? _runCancellation;
    Task? _runLoop;

    public EffortDialOnABridgeDrivenSupervisorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-effort-dial-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");
        _store = OrchestrationSessionStore_Factory.Create(_paths);
    }

    public void Dispose()
    {
        if (_runCancellation != null && _runLoop != null)
        {
            Stop_Async(_runCancellation, _runLoop).GetAwaiter().GetResult();
            _runCancellation.Dispose();
        }

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
    /// THE FLIP WINDOW — the supervisor was spawned in a window, the owner then set the role to
    /// `print`, and nothing has restarted yet. Config says bridge-driven, the world says otherwise,
    /// and the world is right until that session dies: it has a shell, so the dial kills and respawns
    /// it and the owner is told so. Deciding from the config runner alone gets this backwards and
    /// reports that nothing happened, about a session sitting on the owner's screen.
    ///
    /// <para>
    /// PRODUCED THE WAY THE OWNER PRODUCES IT — a config edit after the spawn — and not by starting
    /// with `print` configured, which would make the print runner REGISTER the supervisor and so
    /// describe the other case entirely.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SlashEffort_OnASupervisorFlippedToPrintButNeverRegistered_StillRestartsIt()
    {
        var orchId = await Start_Async(supervisorRunner: SessionRunners.Terminal, implementerRunner: SessionRunners.Terminal);
        _spawner.SpawnedCommands.Clear();

        Write_Config(SessionRunners.Print, SessionRunners.Terminal);

        Assert.False(
            PrintSessionState_Store.Exists(_paths, SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID),
            "a registration exists, so this is not the flip window");

        await Owner_Says_Async("/effort xhigh", 7001, 401, () => _telegram.Has_Sent_Containing("for which role"));
        Owner_Taps(ModelEffortButton_Data.Build(ModelEffortKinds.Effort, orchId, ModelEffortButton_Data.SUPERVISOR_ROLE, "xhigh"), 7002, 402);

        await Wait_ForOverride_Async(orchId, session => session.SupervisorEffortOverride);

        Assert.Equal("xhigh", Session_Now(orchId).SupervisorEffortOverride);

        // No print-session registration was ever written, so this supervisor is a WINDOW whatever the
        // config word says — the pid file is consumed and a new command line carries the new flag.
        Assert.True(
            await Wait_Until_Async(() => _spawner.SpawnedCommands.Count > 0, 20_000),
            $"the supervisor was never respawned.{Environment.NewLine}{_log.Dump()}");

        Assert.False(File.Exists(_paths.Get_SupervisorPidFile(orchId)), "the pid file survived, so nothing was killed");
        Assert.Contains($"--effort xhigh {SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS}", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]));
        Assert.Contains("respawned", Last_OwnerFacingAppEntry(orchId), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// THE ORDINARY TERMINAL SUPERVISOR: the flag reaches `claude` only at spawn, so the dial kills
    /// the pid file and respawns the window.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SlashEffort_OnATerminalSupervisor_KillsThePidFile_AndRespawnsIt()
    {
        var orchId = await Start_Async(supervisorRunner: SessionRunners.Terminal, implementerRunner: SessionRunners.Terminal);
        _spawner.SpawnedCommands.Clear();

        await Owner_Says_Async("/effort xhigh", 7101, 411, () => _telegram.Has_Sent_Containing("for which role"));
        Owner_Taps(ModelEffortButton_Data.Build(ModelEffortKinds.Effort, orchId, ModelEffortButton_Data.SUPERVISOR_ROLE, "xhigh"), 7102, 412);

        await Wait_ForOverride_Async(orchId, session => session.SupervisorEffortOverride);

        Assert.True(
            await Wait_Until_Async(() => _spawner.SpawnedCommands.Count > 0, 20_000),
            $"the terminal supervisor was never respawned.{Environment.NewLine}{_log.Dump()}");

        Assert.False(File.Exists(_paths.Get_SupervisorPidFile(orchId)), "the pid file survived, so nothing was killed");
        Assert.Contains($"--effort xhigh {SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS}", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]));
    }

    /// <summary>
    /// THE REAL BRIDGE-DRIVEN CASE — configured print AND registered — is left alone, while the
    /// terminal reviewer standing beside it in the same crew is restarted by the same dial. One
    /// orchestration, one command, both halves of the predicate, and the per-member resolution that
    /// makes a mixed crew work.
    ///
    /// <para>
    /// IT IS PINNED ON THE IMPLEMENTER SLOT AND NOT ON THE SUPERVISOR, for a reason that is about the
    /// harness rather than the rule: the supervisor is exempt from the dispatcher's turn slots
    /// (<c>PrintTurnDispatcherModel.Is_ExemptFromSlots</c>) and its sources include the owner channel,
    /// so a registered print SUPERVISOR driven by the live engine would take a turn against the real
    /// `claude`. A member parks at the coalesce gate instead — see PARKED_COALESCE_SECONDS. The
    /// predicate under test does not know which role it is asked about.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SlashEffort_OnARegisteredPrintImplementer_LeavesItAlone_AndStillRestartsTheTerminalReviewer()
    {
        var orchId = await Start_Async(supervisorRunner: SessionRunners.Terminal, implementerRunner: SessionRunners.Print);

        Assert.True(
            PrintSessionState_Store.Exists(_paths, SessionRoles.Implementer, orchId, "imp-1"),
            "imp-1 was never registered as a print session, so this case would prove nothing");

        var implementerPidFile = _paths.Get_ImplementerPidFile(orchId, "imp-1");
        File.WriteAllText(implementerPidFile, PLANTED_PID);
        _spawner.SpawnedCommands.Clear();

        // The role word applies the value straight away — no prompt, so the store is the first thing
        // that can be waited on.
        await Owner_Says_Async("/effort imp low", 7201, 421, () => Session_Now(orchId).ImplementerEffortOverride != null);

        Assert.Equal("low", Session_Now(orchId).ImplementerEffortOverride);

        // rev-1 is terminal and WAS restarted, which is what proves the dial ran at all.
        Assert.True(
            await Wait_Until_Async(() => _spawner.SpawnedCommands.Count > 0, 20_000),
            $"the terminal reviewer was never respawned.{Environment.NewLine}{_log.Dump()}");

        Assert.Contains("/reviewer", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]));

        // And imp-1 was not: its pid file is still there, and nothing spawned a window for it.
        Assert.True(File.Exists(implementerPidFile), $"the registered print implementer was killed.{Environment.NewLine}{_log.Dump()}");
        Assert.DoesNotContain(_spawner.SpawnedCommands, command => SpawnCommand_Builder.Decode_SessionScript(command).Contains("/implementer", StringComparison.Ordinal));
    }

    /// <summary>
    /// session.json, read while the engine is writing it. The store opens the file without sharing,
    /// so a read that collides is a share violation and not an answer — asked again rather than
    /// reported as the dial failing.
    /// </summary>
    IOrchestrationSession Session_Now(string orchId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                return _store.Get_Session(orchId);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }

        return _store.Get_Session(orchId);
    }

    /// <summary>
    /// SHARE-TOLERANT, because the engine holds this file open while it appends and a plain
    /// File.ReadAllText loses that race on Windows — a flake that says nothing about the dial.
    /// </summary>
    string Last_OwnerFacingAppEntry(string orchId)
    {
        using var stream = new FileStream(
            _paths.Get_OwnerChannelFile(orchId), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        var text = reader.ReadToEnd();
        var index = text.LastIndexOf("FROM app", StringComparison.Ordinal);

        return index < 0 ? string.Empty : text[index..];
    }

    async Task Wait_ForOverride_Async(string orchId, Func<IOrchestrationSession, string?> read)
    {
        Assert.True(
            await Wait_Until_Async(() => read(Session_Now(orchId)) != null, 20_000),
            $"the effort override never reached session.json.{Environment.NewLine}{_log.Dump()}{Environment.NewLine}{_telegram.Dump_Sent()}");
    }

    async Task<string> Start_Async(SessionRunners supervisorRunner, SessionRunners implementerRunner)
    {
        Write_Config(supervisorRunner, implementerRunner);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        // THE REAL PRINT RUNNER, because its registration file is half the predicate under test — a
        // recording fake would leave every case in the flip window and prove only the easy half.
        List<ISessionRunner> runners = [SessionRunner_Factory.Create_Terminal(_spawner)];

        if (implementerRunner != SessionRunners.Terminal)
            runners.Add(SessionRunner_Factory.Create_Print(_paths, _store, _log));

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, runners, _log);

        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        File.WriteAllText(_paths.Get_SupervisorPidFile(session.OrchId), PLANTED_PID);

        _engine = BridgeEngine_Factory.Create_WithDecisionState(
            _paths, configProvider, _store, _launcher, _log, _telegram,
            EngineStateStore_Factory.Create_InMemory(), _clock, BridgeTestTiming.Fast());

        _runCancellation = new CancellationTokenSource();
        _runLoop = _engine.Run_Async(_runCancellation.Token);

        return session.OrchId;
    }

    void Write_Config(SessionRunners supervisorRunner, SessionRunners implementerRunner)
    {
        var config = new JsonObject
        {
            ["repos"] = new JsonArray(),
            ["telegramSupergroupChatId"] = SUPERGROUP_CHAT_ID,
            ["telegramOwnerUserId"] = OWNER_USER_ID,
            ["runners"] = new JsonObject
            {
                [SessionRole_Names.Get_ConfigKey(SessionRoles.Supervisor)] = new JsonObject { ["runner"] = SessionRunner_Names.Get_Word(supervisorRunner) },
                [SessionRole_Names.Get_ConfigKey(SessionRoles.Implementer)] = new JsonObject { ["runner"] = SessionRunner_Names.Get_Word(implementerRunner) },
            },
            ["printRunner"] = new JsonObject { ["coalesceSeconds"] = PARKED_COALESCE_SECONDS },
        };

        File.WriteAllText(_paths.ConfigFile, config.ToJsonString());

        // The provider reloads on the write stamp, and two writes inside one filesystem tick would
        // otherwise serve the stale config — the same push PrintRunnerTestHarness.Write_Config makes.
        File.SetLastWriteTimeUtc(_paths.ConfigFile, DateTime.UtcNow.AddSeconds(1));
    }

    async Task Owner_Says_Async(string text, long updateId, long messageId, Func<bool> answered)
    {
        _telegram.Queue_Updates(
            "{\"ok\":true,\"result\":[{\"update_id\":" + updateId + ",\"message\":{\"message_id\":" + messageId
            + ",\"message_thread_id\":" + TOPIC_ID + ",\"from\":{\"id\":" + OWNER_USER_ID
            + "},\"chat\":{\"id\":" + SUPERGROUP_CHAT_ID + "},\"text\":\"" + text + "\"}}]}");

        Assert.True(
            await Wait_Until_Async(answered, 20_000),
            $"'{text}' was never acted on.{Environment.NewLine}{_log.Dump()}{Environment.NewLine}{_telegram.Dump_Sent()}");
    }

    void Owner_Taps(string data, long updateId, long messageId)
    {
        _telegram.Queue_Updates(
            "{\"ok\":true,\"result\":[{\"update_id\":" + updateId + ",\"callback_query\":{\"id\":\"cbq-" + updateId + "\","
            + $"\"data\":\"{data}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{messageId},\"message_thread_id\":{TOPIC_ID},\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}]}}");
    }

    /// <summary>
    /// A POLL THAT LOSES A FILE RACE HAS NOT ANSWERED. These conditions read session.json and the
    /// owner channel while the running engine is writing them, and on Windows that is a share
    /// violation rather than a stale read — so it is treated as "not yet" and asked again, which is
    /// the honest reading. Any other exception is the test failing and is left alone.
    /// </summary>
    static async Task<bool> Wait_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (Is_Satisfied(condition))
                return true;

            await Task.Delay(100);
        }

        return Is_Satisfied(condition);
    }

    static bool Is_Satisfied(Func<bool> condition)
    {
        try
        {
            return condition();
        }
        catch (IOException)
        {
            return false;
        }
    }

    static async Task Stop_Async(CancellationTokenSource cancellation, Task loop)
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
