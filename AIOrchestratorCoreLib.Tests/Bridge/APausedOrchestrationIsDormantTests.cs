using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// /pause — ASLEEP, NOT CLOSED AND NOT FINISHED, driven through the real engine.
///
/// <para>
/// The owner's state (2026-09-09): they walked away from one orchestration without ending it.
/// CLAUDE.md's PAUSE decision says dormancy is TWO halves and that BOTH are needed for it to be
/// true — OUTBOUND rides the delivery funnel (<c>EffectiveMode_Resolver</c> answers Deferred, so
/// every send gate and the offset freeze inherit it and the backlog replays on unpause), while
/// PUSHING does not, because no delivery mode has ever governed what the app WRITES INTO A CHANNEL.
/// Each waker is gated separately, and "miss one and dormancy is a word" is the sentence this file
/// exists to keep honest.
/// </para>
/// <para>
/// BOTH HALVES ARE ASSERTED IN ONE CASE ON PURPOSE. Either one alone passes while the other is
/// broken, and the owner's complaint would be identical in both worlds: the topic they put to sleep
/// spoke to them, or the session they told to stop kept working. A guard with two routes to green
/// pins neither.
/// </para>
/// <para>
/// NO PRINT SESSION IS EVER REGISTERED HERE, and that is a harness rule rather than a subject:
/// <c>BridgeEngine_Factory</c> wires the real per-OS <c>claude</c> invocation into its print
/// dispatcher with no fake-CLI seam, so a registered print session under the live engine can spawn
/// a real <c>claude</c>. Terminal runners and a recording spawner only.
/// </para>
/// </summary>
public class APausedOrchestrationIsDormantTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 7373;

    /// <summary>The supervisor entry written while the topic sleeps — held, never dropped.</summary>
    const string SLEPT_THROUGH = "the refactor is still running";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly SoundRecordingTelegram_Fake _telegram = new();
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);
    readonly IBridgeEngine _engine;

    CancellationTokenSource? _runCancellation;
    Task? _runLoop;

    public APausedOrchestrationIsDormantTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-paused-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);

        _engine = BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, _telegram, _engineState, _clock, BridgeTestTiming.Fast());
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
    /// THE WHOLE OF PAUSE, END TO END: the command puts the orchestration to sleep on disk AND in
    /// session.json, nothing the app writes reaches the supervisor's channel while it sleeps,
    /// nothing the supervisor writes reaches the phone — and writing in the topic wakes it, at which
    /// point everything held arrives.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WhilePaused_NothingIsPushedToTheSupervisor_AndOutboundIsDeferred_UntilTheOwnerWrites()
    {
        var orchId = await Start_Async();

        await Owner_Says_Async("/pause", 7001, 401, () => Session_Now(orchId).Paused);

        // BOTH RECORDS, because the hook reads one and the app reads the other. A bash Stop hook
        // cannot open session.json, so a pause that exists only in the store would leave the session
        // refused its turn end — still working, which is the opposite of dormant.
        Assert.True(Session_Now(orchId).Paused);
        Assert.True(File.Exists(Paused_FlagFile(orchId)), $"the .paused marker was never raised.{Environment.NewLine}{_log.Dump()}");

        Append_Supervisor(orchId, 9, SLEPT_THROUGH);

        var channelLengthWhenPaused = Channel_Text(orchId).Length;

        // Long enough for every sweep on the tick to have run — idle nudges, the ledger check, the
        // periodic pass. Each is gated separately, so this window is what makes the claim about all
        // of them rather than about whichever one happened to fire first.
        await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(20));

        // THE PUSHING HALF: nothing the app writes woke the supervisor while it slept.
        Assert.DoesNotContain(
            "FROM app",
            Channel_Text(orchId)[channelLengthWhenPaused..]);

        // THE OUTBOUND HALF: what the supervisor wrote is HELD, not dropped — Deferred, never
        // Silenced, which is the difference between a backlog and a loss.
        Assert.False(
            _telegram.Has_Sent_Containing(SLEPT_THROUGH),
            $"a paused topic texted the owner.{Environment.NewLine}{_telegram.Dump_Sent()}");

        await Owner_Says_Async("go on", 7002, 402, () => !Session_Now(orchId).Paused);

        Assert.False(Session_Now(orchId).Paused);

        Assert.True(
            await Wait_Until_Async(() => !File.Exists(Paused_FlagFile(orchId)), 20_000),
            $"the .paused marker survived the wake.{Environment.NewLine}{_log.Dump()}");

        // AND THE BACKLOG REPLAYS. This is the promise pause makes and the reason it resolves to
        // Deferred rather than Silenced.
        Assert.True(
            await Wait_Until_Async(() => _telegram.Has_Sent_Containing(SLEPT_THROUGH), 25_000),
            $"the entry written while the topic slept never arrived.{Environment.NewLine}{_log.Dump()}{Environment.NewLine}{_telegram.Dump_Sent()}");
    }

    /// <summary>
    /// A SECOND /pause INSIDE THE RENAME LAG RE-ASSERTS RATHER THAN TOGGLING — the /done evidence,
    /// where every toggle in this machine's history was undone by a repeat press 17-23 seconds
    /// later, because the rename had not surfaced yet and the owner sent the command again.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASecondPauseStraightAway_LeavesItPaused_RatherThanWakingIt()
    {
        var orchId = await Start_Async();

        await Owner_Says_Async("/pause", 7101, 411, () => Session_Now(orchId).Paused);

        await Owner_Says_Async(
            "/pause", 7102, 412,
            () => _log.Has_Line_Containing("already paused, re-asserted"));

        Assert.True(Session_Now(orchId).Paused, "a second /pause woke the topic instead of re-asserting the pause");
        Assert.True(File.Exists(Paused_FlagFile(orchId)));
    }

    /// <summary>
    /// DERIVED, NEVER AUTHORED — the rule the meeting flag states. The marker is reconciled for
    /// every session on the tick, so it cannot outlive the state it stands for: an app that died
    /// with one on disk clears it on the way back in, and one that died with a pause unmarked raises
    /// it. Asserted from the STORE rather than through the command, because the command writes the
    /// flag itself and would pass with the reconcile deleted.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ThePausedMarker_IsReconciledOnTheTick_InBothDirections()
    {
        var orchId = await Start_Async();

        File.WriteAllText(Paused_FlagFile(orchId), "left behind by a crash\n");

        Assert.True(
            await Wait_Until_Async(() => !File.Exists(Paused_FlagFile(orchId)), 20_000),
            $"a stale .paused marker survived the tick, so it can exempt a session for ever.{Environment.NewLine}{_log.Dump()}");

        _store.Set_Paused(orchId, true);

        Assert.True(
            await Wait_Until_Async(() => File.Exists(Paused_FlagFile(orchId)), 20_000),
            $"a paused session never got its marker, so the turn-end hook keeps it working.{Environment.NewLine}{_log.Dump()}");
    }

    string Paused_FlagFile(string orchId)
    {
        return Path.Combine(_paths.Get_OrchestrationFolder(orchId), AIOrchestratorCoreLib.Status.PausedFlag_Marker.FILE_NAME);
    }

    /// <summary>
    /// session.json, read while the engine is writing it. The store opens the file without sharing,
    /// so a read that collides is a share violation and not an answer — asked again rather than
    /// reported as the pause failing.
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
    /// File.ReadAllText loses that race on Windows — a flake that says nothing about the pause.
    /// </summary>
    string Channel_Text(string orchId)
    {
        using var stream = new FileStream(
            _paths.Get_OwnerChannelFile(orchId), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    async Task<string> Start_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        _runCancellation = new CancellationTokenSource();
        _runLoop = _engine.Run_Async(_runCancellation.Token);

        _telegram.Queue_Updates(Message_Json("what is happening", 7000, 400));

        Assert.True(
            await Wait_Until_Async(() => _log.Has_Info_Containing("Owner message buffered"), 15_000),
            $"the engine never made its first pass.{Environment.NewLine}{_log.Dump()}");

        return session.OrchId;
    }

    void Append_Supervisor(string orchId, int entryNumber, string body)
    {
        File.AppendAllText(
            _paths.Get_OwnerChannelFile(orchId),
            $"\n## [{entryNumber}] FROM supervisor — {DateTime.Now:yyyy-MM-dd HH:mm} — a report\n{body}\n");
    }

    async Task Owner_Says_Async(string text, long updateId, long messageId, Func<bool> answered)
    {
        _telegram.Queue_Updates(Message_Json(text, updateId, messageId));

        Assert.True(
            await Wait_Until_Async(answered, 25_000),
            $"'{text}' was never acted on.{Environment.NewLine}{_log.Dump()}{Environment.NewLine}{_telegram.Dump_Sent()}");
    }

    static string Message_Json(string text, long updateId, long messageId)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":" + updateId + ",\"message\":{\"message_id\":" + messageId
            + ",\"message_thread_id\":" + TOPIC_ID + ",\"from\":{\"id\":" + OWNER_USER_ID
            + "},\"chat\":{\"id\":" + SUPERGROUP_CHAT_ID + "},\"text\":\"" + text + "\"}}]}";
    }

    /// <summary>
    /// A POLL THAT LOSES A FILE RACE HAS NOT ANSWERED. These conditions read session.json and the
    /// owner channel while the running engine is writing them, and on Windows that is a share
    /// violation rather than a stale read — so it is treated as "not yet" and asked again.
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
