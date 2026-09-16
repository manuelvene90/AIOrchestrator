using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.WakeTicket;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE STEP THAT REPAIRS THE MACHINE ON THE DEFAULT RUNNER. A terminal supervisor in ticket mode gets
/// the SAME policy a bridge-driven one gets: the owner wakes it now, and the app's own bookkeeping
/// wakes nobody — measured on the VPS, 247 of ~400 supervisor wake-ups were member traffic at ~1 M
/// input tokens each, and zero were worth a wake on an app entry alone. Under the bash watcher every
/// one of those changes was a wake, because the monitor could only see that the file had changed.
///
/// <para>
/// DRIVEN THROUGH THE ENGINE'S REAL TICK, like the sweeps next door
/// (<see cref="ANewOrchestrationGetsItsTopicAtOnceTests"/>,
/// <see cref="APausedOrchestrationIsDormantTests"/>): what is being asserted is that a per-tick sweep
/// reaches these sessions at all and screens them correctly, which is a property of the loop and not
/// of any function a unit test could ask.
/// </para>
/// <para>
/// EVERY SILENCE HERE IS ASSERTED BESIDE A LIVE CONTROL IN THE SAME RUN. "No ticket was written" has
/// two routes to it — the screen held, or the sweep never ran — so a silent session alone would pin
/// neither (CLAUDE.md decision 20). The control is a second session that DOES get its ticket in the
/// same ticks.
/// </para>
/// <para>
/// NO PRINT SESSION IS EVER REGISTERED HERE. <c>BridgeEngine_Factory</c> wires the real per-OS
/// <c>claude</c> invocation into its print dispatcher with no fake-CLI seam, so a registered
/// bridge-driven session under a live engine can spawn a real <c>claude</c>. Everything here is a
/// TERMINAL session (<c>DrivesTurns = false</c>) behind a recording spawner, which is exactly the
/// subject anyway.
/// </para>
/// </summary>
public class WakeTicketSweepTests : IDisposable
{
    /// <summary>
    /// Only the SUPERVISOR is moved to the ticket. Every other role keeps the bash watcher, which is
    /// what makes <c>imp-1</c> a free control for the gate: same orchestration, same ticks, same
    /// sweep, different wake mode.
    /// </summary>
    const string CONFIG = """{"repos":[],"runners":{"supervisor":{"runner":"terminal","wake":"ticket"}}}""";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public WakeTicketSweepTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-wake-ticket-sweep-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
        File.WriteAllText(_paths.ConfigFile, CONFIG);

        _store = OrchestrationSessionStore_Factory.Create(_paths);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);

        // NO TELEGRAM CLIENT: file-only mode, exactly as a machine with no bot token runs. The ticket
        // is a local file and has nothing to do with the phone — and running without the poller keeps
        // these cases about the sweep.
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, configProvider, _store, _launcher, log, telegramClient: null, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// THE OWNER WAKES IT NOW — the rule nothing is allowed to hold — and the ticket says so in the
    /// words the decision carried, so the terminal session is told WHY it is taking a turn rather than
    /// merely that a file moved.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnOwnerMessageWritesATicketForATerminalSupervisorInTicketMode()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        await Let_TheSweepRegisterEverything_Async();
        Append_OwnerEntry(orchId, "do the merge");

        var got = await Run_Until_Async(() => Read_SupervisorTicket(orchId) != null, BridgeTestTiming.Window_ForTicks(8));

        Assert.True(got, "the terminal supervisor never got a wake ticket");
        Assert.Contains("the owner wrote", Read_SupervisorTicket(orchId)!.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE APP'S OWN BOOKKEEPING WAKES NOBODY. Under the watcher this entry was indistinguishable from
    /// a member's report — it changed the file, so the session woke and spent a turn reading a note
    /// addressed to it by the app. The live control is the orchestration beside it, which gets its
    /// ticket from an owner message in the same ticks.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnAppNoteWritesNoTicket_WhileTheOwnerMessageBesideItDoes()
    {
        var quiet = _launcher.Start_Orchestration("Quiet", _tempRepo).OrchId;
        var live = _launcher.Start_Orchestration("Live", _tempRepo).OrchId;

        await Let_TheSweepRegisterEverything_Async();

        Assert.True(
            ChannelAppender.Append_AppEntry(_paths.Get_OwnerChannelFile(quiet), AppEntryAudiences.Agent, "PLAN.md is behind your verdicts", "Bring it up to date.", DateTime.Now),
            "the app note could not be appended, so this case would prove nothing");

        Append_OwnerEntry(live, "status?");

        var control = await Run_Until_Async(() => Read_SupervisorTicket(live) != null, BridgeTestTiming.Window_ForTicks(8));

        Assert.True(control, "the control orchestration never got a ticket, so the silence beside it means nothing");
        Assert.Null(Read_SupervisorTicket(quiet));
    }

    /// <summary>
    /// THE GATE IS PER ROLE, AND IT IS OFF UNTIL SOMEBODY SAYS OTHERWISE. <c>imp-1</c> is on the
    /// watcher in this config and must be handed nothing at all, while the supervisor of the SAME
    /// orchestration, woken in the same ticks, gets its ticket.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASessionStillOnTheWatcherGetsNoTicket_WhileTheTicketModeSupervisorBesideItDoes()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        await Let_TheSweepRegisterEverything_Async();

        Assert.True(
            ChannelAppender.Append_SessionEntry(_paths.Get_ImplementerChannelFile(orchId, "imp-1"), ChannelAuthors.Supervisor, "TASK 1", "Do the thing.", DateTime.Now),
            "the brief could not be appended to imp-1, so this case would prove nothing");

        Append_OwnerEntry(orchId, "do the merge");

        var control = await Run_Until_Async(() => Read_SupervisorTicket(orchId) != null, BridgeTestTiming.Window_ForTicks(8));

        Assert.True(control, "the supervisor never got a ticket, so imp-1's silence means nothing");
        Assert.Null(WakeTicket_Store.Read_OrNull(WakeTicket_Store.Get_File(_paths, SessionRoles.Implementer, orchId, "imp-1")));
    }

    /// <summary>
    /// ONE ENTRY IS ONE WAKE. The cursor advance beside the ticket is what makes this true, and it is
    /// the half that cannot be seen from the ticket file alone — so the session's own record is read
    /// back too, for the flag the advance must carry over: a transition that let <c>DrivesTurns</c>
    /// default back to <c>true</c> would put this session under the print dispatcher on its FIRST
    /// ticket, with a terminal window and headless turns answering the same brief.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheSameEntryDoesNotWriteASecondTicket_AndTheSessionStaysOutOfTheDispatcher()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        await Let_TheSweepRegisterEverything_Async();
        Append_OwnerEntry(orchId, "do the merge");

        var got = await Run_Until_Async(() => Read_SupervisorTicket(orchId) != null, BridgeTestTiming.Window_ForTicks(8));
        Assert.True(got, "the supervisor never got a ticket");

        // MANY MORE TICKS OVER AN UNCHANGED CHANNEL. A sweep that did not advance the cursor would
        // find the same entry pending every 20 ms and count up here.
        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(10));

        Assert.Equal(1, Read_SupervisorTicket(orchId)!.Number);
        Assert.False(Read_SupervisorState(orchId)!.DrivesTurns, "the ticket put the session back under the print dispatcher");
    }

    /// <summary>
    /// AN IDLE SESSION IS NEVER TICKETED — the empty-set guard, and the one case that would have been
    /// a ticket per session per tick for ever. <c>WakeUp_Policy</c> answers an EMPTY pending set with
    /// "boot turn" UNCONDITIONALLY: it was written when only the dispatcher's boot turn could reach it
    /// with one. Two things stand between that and this sweep, and both are asserted by this silence —
    /// <c>Decide_OrNull</c>'s explicit empty-set return, and <c>Needs_BootTurn</c> refusing a session
    /// the app does not drive (nothing ever records an executed turn for a terminal session, so
    /// "has not taken a turn yet" would be true of it for ever).
    ///
    /// <para>
    /// THE CONTROL IS THE SAME SESSION, AFTERWARDS. Ten quiet ticks prove nothing on their own — the
    /// sweep might not be reaching this orchestration at all — so the one owner message at the end is
    /// what proves the silence was a decision.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnIdleSessionIsNeverTicketed_OverManyTicks_AndTheSameSessionStillWakesOnAnOwnerMessage()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        await Run_Until_Async(() => Read_SupervisorTicket(orchId) != null, BridgeTestTiming.Window_ForTicks(12));

        Assert.Null(Read_SupervisorTicket(orchId));

        Append_OwnerEntry(orchId, "now there is something to do");

        var woke = await Run_Until_Async(() => Read_SupervisorTicket(orchId) != null, BridgeTestTiming.Window_ForTicks(8));

        Assert.True(woke, "the sweep never reached this session at all, so the silence above proves nothing");
        Assert.Equal(1, Read_SupervisorTicket(orchId)!.Number);
    }

    /// <summary>
    /// A PAUSED ORCHESTRATION IS DORMANT, and in ticket mode the ticket is the WHOLE of what starts
    /// that session's turn — so a paused supervisor that still got tickets would be asleep in name
    /// only. CLAUDE.md's PAUSE decision: every waker is gated for itself, and "miss one and dormancy
    /// is a word".
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APausedOrchestrationGetsNoTicket_WhileTheOneBesideItDoes()
    {
        var asleep = _launcher.Start_Orchestration("Asleep", _tempRepo).OrchId;
        var live = _launcher.Start_Orchestration("Live", _tempRepo).OrchId;

        await Let_TheSweepRegisterEverything_Async();

        _store.Set_Paused(asleep, true);

        Append_OwnerEntry(asleep, "do the merge");
        Append_OwnerEntry(live, "do the merge");

        var control = await Run_Until_Async(() => Read_SupervisorTicket(live) != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(control, "the live orchestration never got a ticket, so the paused one's silence means nothing");
        Assert.Null(Read_SupervisorTicket(asleep));
    }

    /// <summary>
    /// LETS THE SWEEP SEE THE SESSION ONCE BEFORE THE CASE SEEDS ITS TRAFFIC, and that is the subject's
    /// own rule rather than a harness convenience: a session registered with no cursors is BASELINED on
    /// the app's first sight of it — everything already in its channels is HISTORY and wakes nobody
    /// (<c>WakeDecision_Resolver.Read_Pending</c>, and the same absorption a print registration does).
    /// In life the session is spawned and then written to; a case that seeded first would be asserting
    /// against the baseline instead of against the wake policy.
    /// </summary>
    async Task Let_TheSweepRegisterEverything_Async()
    {
        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(4));
    }

    void Append_OwnerEntry(string orchId, string text)
    {
        Assert.True(
            ChannelAppender.Append_OwnerEntry(_paths.Get_OwnerChannelFile(orchId), text, DateTime.Now),
            $"the owner entry could not be appended to '{orchId}', so this case would prove nothing");
    }

    IWakeTicket? Read_SupervisorTicket(string orchId)
    {
        return WakeTicket_Store.Read_OrNull(WakeTicket_Store.Get_File(_paths, SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID));
    }

    IPrintSessionState? Read_SupervisorState(string orchId)
    {
        return PrintSessionState_Store.Read_OrNull(PrintSessionState_Store.Get_StateFile(_paths, SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID));
    }

    async Task<bool> Run_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);
        var satisfied = false;

        for (var waited = 0; waited < maxMilliseconds; waited += 50)
        {
            if (condition())
            {
                satisfied = true;
                break;
            }

            await Task.Delay(50);
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
