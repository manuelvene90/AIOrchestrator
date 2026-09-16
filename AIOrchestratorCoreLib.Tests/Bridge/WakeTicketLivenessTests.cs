using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.WakeTicket;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE FAILURE THIS SERIES INTRODUCES, MADE LOUD. Under the old monitor a session that stopped waking
/// was still fingerprinting its channels and caught up on the next change. Under a ticket it will not:
/// a monitor that died, or a ticket file nobody is polling, is indistinguishable from a quiet
/// orchestration — which is exactly the shape of a silent failure (2026-09-15 one-wake-model, plan 01
/// task 10).
///
/// <para>
/// DRIVEN THROUGH THE ENGINE'S REAL TICK, like <see cref="WakeTicketSweepTests"/> next door, because
/// the claim is about a per-tick sweep reaching these sessions and screening them correctly. The
/// clock is the test's (<see cref="FixedClock_Fake"/>) so the two windows pass in a jump instead of in
/// eleven minutes of wall clock.
/// </para>
/// <para>
/// EVERY SILENCE HERE IS ASSERTED BESIDE A LIVE CONTROL IN THE SAME RUN (CLAUDE.md decision 20): "no
/// alert was raised" has two routes to it — the screen held, or the sweep never reached this
/// orchestration at all — so a silent orchestration alone would pin neither. The control is a second
/// orchestration, ticketed in the same ticks, that DOES get its alert.
/// </para>
/// <para>
/// NO PRINT SESSION IS EVER REGISTERED HERE, for the harness reason the sweep tests give:
/// <c>BridgeEngine_Factory</c> wires the real per-OS <c>claude</c> invocation into its print
/// dispatcher with no fake-CLI seam. Everything here is a TERMINAL session behind a recording spawner.
/// </para>
/// </summary>
public class WakeTicketLivenessTests : IDisposable
{
    /// <summary>Only the supervisor is moved to the ticket, exactly as the sweep tests configure it.</summary>
    const string CONFIG = """{"repos":[],"runners":{"supervisor":{"runner":"terminal","wake":"ticket"}}}""";

    /// <summary>
    /// The stable half of the alert's subject. It must NOT appear in the alert's body as well: the
    /// "raised once" case counts occurrences of this string in the channel file, and a body that
    /// repeated it would make one alert look like two.
    /// </summary>
    const string ALERT_MARK = "has not taken a turn since wake ticket";

    /// <summary>
    /// Past <c>MemberDigestWindow × 2</c> — the shipped window is five minutes, so ten is the rule and
    /// eleven is past it.
    /// </summary>
    static readonly TimeSpan PAST_TWO_WINDOWS = TimeSpan.FromMinutes(11);

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);
    readonly IBridgeEngine _engine;

    public WakeTicketLivenessTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-wake-liveness-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
        File.WriteAllText(_paths.ConfigFile, CONFIG);

        _store = OrchestrationSessionStore_Factory.Create(_paths);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);

        // NO TELEGRAM CLIENT: file-only mode, as a machine with no bot token runs. The alert is a
        // channel entry; whether it is then mirrored is the mirror's business and not this file's.
        _engine = BridgeEngine_Factory.Create_WithDecisionState(
            _paths, configProvider, _store, _launcher, _log, telegramClient: null, _engineState, _clock, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// THE ALARM ITSELF: a ticket was written, two digest windows passed, and the session filed nothing
    /// of its own. The ticket is asserted first, so a case that never got as far as a ticket fails as
    /// a setup failure rather than as the defect.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATicketThatProducesNoEntryWithinTwoWindows_RaisesTheStall()
    {
        var orchId = await Ticket_ASupervisor_Async("Repo");

        _clock.Advance(PAST_TWO_WINDOWS);

        var raised = await Run_Until_Async(() => Count_Alerts(orchId) > 0, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(raised, $"a ticket nobody acted on raised nothing.{Environment.NewLine}{_log.Dump()}");
    }

    /// <summary>
    /// ONCE, NOT EVERY TICK. The token is what makes this true, and this is the half
    /// <see cref="Bridge.BudgetAlert_Planner"/>'s account exists to protect: a repeat that is a
    /// notification is a waterfall (CLAUDE.md decision 14).
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ItIsRaisedOnce_NotOnEveryTick()
    {
        var orchId = await Ticket_ASupervisor_Async("Repo");

        _clock.Advance(PAST_TWO_WINDOWS);

        var raised = await Run_Until_Async(() => Count_Alerts(orchId) > 0, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(raised, $"a ticket nobody acted on raised nothing, so counting repeats proves nothing.{Environment.NewLine}{_log.Dump()}");

        // MANY MORE TICKS, ALL OF THEM PAST THE WINDOW. Without the token every one of them is another
        // alert about the same ticket.
        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(12));

        Assert.Equal(1, Count_Alerts(orchId));
    }

    /// <summary>
    /// A SESSION THAT ANSWERED IS NOT STALLED, and an entry of its own is the only proof of that the
    /// app has: the ticket carries no acknowledgement back, and the session's own entry is not inbound
    /// traffic for it, so nothing else about the answer is visible from here.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASessionThatFiledItsOwnEntryIsNotFlagged_WhileTheSilentOneBesideItIs()
    {
        var answered = await Ticket_ASupervisor_Async("Answered");
        var silent = await Ticket_ASupervisor_Async("Silent");

        Assert.True(
            ChannelAppender.Append_SessionEntry(_paths.Get_OwnerChannelFile(answered), ChannelAuthors.Supervisor, "ON IT", "Reading the repo now.", DateTime.Now),
            "the supervisor's own entry could not be appended, so this case would prove nothing");

        _clock.Advance(PAST_TWO_WINDOWS);

        var control = await Run_Until_Async(() => Count_Alerts(silent) > 0, BridgeTestTiming.Window_ForTicks(12));

        Assert.True(control, $"the silent orchestration was never flagged, so the answered one's silence means nothing.{Environment.NewLine}{_log.Dump()}");
        Assert.Equal(0, Count_Alerts(answered));
    }

    /// <summary>
    /// A PAUSE IS NOT A STALL. The owner walked away from this orchestration on purpose, and CLAUDE.md's
    /// PAUSE decision says nothing may poke the session — an alarm about a session that is asleep
    /// because it was told to is exactly the noise the pause exists to stop.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APausedOrchestrationIsNeverFlagged_WhileTheOneBesideItIs()
    {
        var asleep = await Ticket_ASupervisor_Async("Asleep");
        var live = await Ticket_ASupervisor_Async("Live");

        _store.Set_Paused(asleep, true);
        _clock.Advance(PAST_TWO_WINDOWS);

        var control = await Run_Until_Async(() => Count_Alerts(live) > 0, BridgeTestTiming.Window_ForTicks(12));

        Assert.True(control, $"the live orchestration was never flagged, so the paused one's silence means nothing.{Environment.NewLine}{_log.Dump()}");
        Assert.Equal(0, Count_Alerts(asleep));
    }

    /// <summary>
    /// Starts an orchestration, lets the sweep see it once (a session registered with no cursors is
    /// BASELINED on first sight, so everything already in its channels is history), then wakes it with
    /// an owner message and waits for the ticket that follows.
    /// </summary>
    async Task<string> Ticket_ASupervisor_Async(string displayName)
    {
        var orchId = _launcher.Start_Orchestration(displayName, _tempRepo).OrchId;

        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(4));

        Assert.True(
            ChannelAppender.Append_OwnerEntry(_paths.Get_OwnerChannelFile(orchId), "do the merge", DateTime.Now),
            $"the owner entry could not be appended to '{orchId}', so this case would prove nothing");

        var ticketed = await Run_Until_Async(() => Read_Ticket(orchId) != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(ticketed, $"'{orchId}' never got a wake ticket, so nothing here is about liveness.{Environment.NewLine}{_log.Dump()}");

        // LET THE SWEEP SEE THE TICKET ONCE MORE BEFORE THE CLOCK MOVES, and that is the subject's own
        // rule rather than a harness convenience: the two windows are counted from the moment the APP
        // first sees a ticket number, not from the ticket's own stamp, so that a ticket answered before
        // a restart cannot be met afterwards by a fresh process with an old stamp and alerted on. A
        // case that advanced the clock at the instant the file appeared would be starting its window
        // after the jump and measuring nothing.
        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(6));

        return orchId;
    }

    IWakeTicket? Read_Ticket(string orchId)
    {
        return WakeTicket_Store.Read_OrNull(WakeTicket_Store.Get_File(_paths, SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID));
    }

    /// <summary>
    /// How many times the alert stands in this orchestration's owner channel — read from the FILE
    /// rather than from a recorded call, because "an entry is on disk" is what the choke point's
    /// return value means and what the owner would actually see.
    /// </summary>
    int Count_Alerts(string orchId)
    {
        var file = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(file))
            return 0;

        var text = File.ReadAllText(file);
        var count = 0;

        for (var at = text.IndexOf(ALERT_MARK, StringComparison.Ordinal); at >= 0; at = text.IndexOf(ALERT_MARK, at + 1, StringComparison.Ordinal))
            count++;

        return count;
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
