using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionRunner;
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
/// Killing a pid file that does not exist is harmless by luck; RESPAWNING a print supervisor is
/// not, because it puts a terminal window beside a session the dispatcher is already driving.
/// </para>
/// <para>
/// Both runner kinds are pinned here on purpose. A "does not kill" claim on its own passes just as
/// well when the dial does nothing at all, so the terminal case is what makes the print case mean
/// something — the same reason a guard must never have two routes to green.
/// </para>
/// </summary>
public class EffortDialOnABridgeDrivenSupervisorTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 6161;

    /// <summary>Not a shell pid, so a kill that DOES run still gets as far as deleting the file.</summary>
    const string PLANTED_PID = "999";

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
    /// THE PRINT SUPERVISOR: the override lands on session.json and NOTHING is killed or respawned.
    /// The dispatcher picks it up at its next turn — there is no window to restart.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SlashEffort_OnAPrintSupervisor_StoresTheOverride_AndDoesNotTouchAPidFile()
    {
        var orchId = await Start_Async(SessionRunners.Print);

        // The crew's own imp-1 and rev-1 are terminal and were spawned by the start; the claim below
        // is about what the DIAL spawns, so the start's own three are not part of it.
        _spawner.SpawnedCommands.Clear();

        await Owner_Says_Async(orchId, "/effort xhigh", 7001, 401);
        await Owner_Taps_Async(ModelEffortButton_Data.Build(ModelEffortKinds.Effort, orchId, ModelEffortButton_Data.SUPERVISOR_ROLE, "xhigh"), 7002, 402);

        Assert.True(
            await Wait_Until_Async(() => _store.Get_Session(orchId).SupervisorEffortOverride != null, 20_000),
            $"the effort override never reached session.json.{Environment.NewLine}{_log.Dump()}{Environment.NewLine}{_telegram.Dump_Sent()}");

        Assert.Equal("xhigh", _store.Get_Session(orchId).SupervisorEffortOverride);

        // A print session has no pid file; Apply_Dial must not try. The planted one is the witness:
        // SessionTerminator deletes the file it was pointed at, and so does a respawn.
        Assert.False(Kill_WasRequested(orchId), $"a bridge-driven supervisor was killed.{Environment.NewLine}{_log.Dump()}");
        Assert.Empty(_spawner.SpawnedCommands);

        Assert.Contains("effort", Last_OwnerFacingAppEntry(orchId), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// THE TERMINAL SUPERVISOR, which is what makes the claim above a claim: the flag reaches
    /// `claude` only at spawn, so here the dial DOES kill the pid file and respawn the window.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SlashEffort_OnATerminalSupervisor_KillsThePidFile_AndRespawnsIt()
    {
        var orchId = await Start_Async(SessionRunners.Terminal);
        _spawner.SpawnedCommands.Clear();

        await Owner_Says_Async(orchId, "/effort xhigh", 7101, 411);
        await Owner_Taps_Async(ModelEffortButton_Data.Build(ModelEffortKinds.Effort, orchId, ModelEffortButton_Data.SUPERVISOR_ROLE, "xhigh"), 7102, 412);

        Assert.True(
            await Wait_Until_Async(() => _store.Get_Session(orchId).SupervisorEffortOverride != null, 20_000),
            $"the effort override never reached session.json.{Environment.NewLine}{_log.Dump()}{Environment.NewLine}{_telegram.Dump_Sent()}");

        Assert.True(
            await Wait_Until_Async(() => _spawner.SpawnedCommands.Count > 0, 20_000),
            $"the terminal supervisor was never respawned.{Environment.NewLine}{_log.Dump()}");

        Assert.True(Kill_WasRequested(orchId), "the terminal supervisor's pid file survived the dial");
        Assert.Contains($"--effort xhigh {SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS}", SpawnCommand_Builder.Decode_SessionScript(_spawner.SpawnedCommands[0]));
    }

    /// <summary>
    /// The planted pid file is GONE — which is what a kill leaves behind (SessionTerminator deletes
    /// it in its finally, and the respawn's own stale-file sweep does too). There is no interface
    /// around that static to record against, so the file it consumes is the observation.
    /// </summary>
    bool Kill_WasRequested(string orchId)
    {
        return !File.Exists(_paths.Get_SupervisorPidFile(orchId));
    }

    string Last_OwnerFacingAppEntry(string orchId)
    {
        var text = File.ReadAllText(_paths.Get_OwnerChannelFile(orchId));
        var index = text.LastIndexOf("FROM app", StringComparison.Ordinal);

        return index < 0 ? string.Empty : text[index..];
    }

    async Task<string> Start_Async(SessionRunners supervisorRunner)
    {
        Write_Config(supervisorRunner);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        // The bridge-driven runner is a RECORDER, not the real print runner: registering a print
        // session would hand the dispatcher a live slot and it would start invoking claude for real.
        // Apply_Dial reads the runner KIND, which comes from the config above either way.
        List<ISessionRunner> runners = [SessionRunner_Factory.Create_Terminal(_spawner)];

        if (supervisorRunner != SessionRunners.Terminal)
            runners.Add(new RecordingRunner_Fake(supervisorRunner));

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

    void Write_Config(SessionRunners supervisorRunner)
    {
        var config = new JsonObject
        {
            ["repos"] = new JsonArray(),
            ["telegramSupergroupChatId"] = SUPERGROUP_CHAT_ID,
            ["telegramOwnerUserId"] = OWNER_USER_ID,
            ["runners"] = new JsonObject
            {
                [SessionRole_Names.Get_ConfigKey(SessionRoles.Supervisor)] = new JsonObject
                {
                    ["runner"] = SessionRunner_Names.Get_Word(supervisorRunner),
                },
            },
        };

        File.WriteAllText(_paths.ConfigFile, config.ToJsonString());
    }

    async Task Owner_Says_Async(string orchId, string text, long updateId, long messageId)
    {
        _telegram.Queue_Updates(
            "{\"ok\":true,\"result\":[{\"update_id\":" + updateId + ",\"message\":{\"message_id\":" + messageId
            + ",\"message_thread_id\":" + TOPIC_ID + ",\"from\":{\"id\":" + OWNER_USER_ID
            + "},\"chat\":{\"id\":" + SUPERGROUP_CHAT_ID + "},\"text\":\"" + text + "\"}}]}");

        // A crew with no role named is answered with the two role buttons; waiting for that is what
        // tells the tap below that the command path ran at all.
        Assert.True(
            await Wait_Until_Async(() => _telegram.Has_Sent_Containing("for which role"), 20_000),
            $"'{text}' was never answered in {orchId}.{Environment.NewLine}{_log.Dump()}{Environment.NewLine}{_telegram.Dump_Sent()}");
    }

    async Task Owner_Taps_Async(string data, long updateId, long messageId)
    {
        _telegram.Queue_Updates(
            "{\"ok\":true,\"result\":[{\"update_id\":" + updateId + ",\"callback_query\":{\"id\":\"cbq-" + updateId + "\","
            + $"\"data\":\"{data}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{messageId},\"message_thread_id\":{TOPIC_ID},\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}]}}");

        await Task.CompletedTask;
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
