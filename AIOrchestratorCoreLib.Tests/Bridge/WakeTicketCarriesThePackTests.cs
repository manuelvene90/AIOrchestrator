using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.StatePack;
using AIOrchestratorCoreLib.Running.WakeTicket;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE PAYOFF OF THE SERIES: a TERMINAL session is handed the same state pack a headless one gets.
/// Until this step the pack was written only by <c>PrintTurnExecutorModel</c>, so the three things a
/// fresh session cannot reconstruct cheaply — its brief, its own last report, the code state — were
/// reachable only on the runner that is NOT the default. The ticket now names the pack, and the
/// shrunken monitor tells the session to read it first.
///
/// <para>
/// THE BRIEF IS NOT IN THE PENDING SET, ON PURPOSE. It is appended BEFORE the sweep first sees the
/// session, so it is absorbed as history and wakes nobody; the entry that does the waking carries no
/// task marker at all. Without that separation "the pack contains the brief" would have two routes to
/// green — the brief section, or the pending-entries section quoting the same text — and would pin
/// neither (CLAUDE.md decision 20). Here the brief's words can only have come from
/// <c>Brief_Finder</c> running over the channel's history.
/// </para>
/// <para>
/// DRIVEN THROUGH THE ENGINE'S REAL TICK, like <see cref="WakeTicketSweepTests"/> next door, and for
/// the same reason: what is asserted is a property of the sweep, not of any function a unit test
/// could ask. NO PRINT SESSION IS EVER REGISTERED — <c>BridgeEngine_Factory</c> wires the real
/// per-OS <c>claude</c> invocation into its print dispatcher with no fake-CLI seam, so everything
/// here is a terminal session behind a recording spawner.
/// </para>
/// </summary>
public class WakeTicketCarriesThePackTests : IDisposable
{
    /// <summary>
    /// Only the IMPLEMENTER is moved to the ticket; the supervisor of the same orchestration keeps the
    /// bash watcher, which makes it the live control for both silences below — same ticks, same sweep,
    /// different wake mode.
    /// </summary>
    const string CONFIG = """{"repos":[],"runners":{"implementer":{"runner":"terminal","wake":"ticket"}}}""";

    const string BRIEF_SUBJECT = "TASK 1 — build the thing";
    const string BRIEF_BODY = "Port the ledger parser, then report.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public WakeTicketCarriesThePackTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-wake-ticket-pack-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
        File.WriteAllText(_paths.ConfigFile, CONFIG);

        _store = OrchestrationSessionStore_Factory.Create(_paths);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);

        // NO TELEGRAM CLIENT: file-only mode, as a machine with no bot token runs. The pack and the
        // ticket are local files and have nothing to do with the phone.
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, configProvider, _store, _launcher, log, telegramClient: null, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// THE WHOLE POINT, IN ONE CASE: the ticket names a pack, the pack is really on disk where the
    /// role command looks for it, and it carries the brief the session would otherwise have to go and
    /// find in a channel that may since have been compacted.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheTicketNamesAPackThatExistsAndHoldsTheBrief()
    {
        var orchId = Start_WithImplementer();

        Append_SupervisorEntry(orchId, BRIEF_SUBJECT, BRIEF_BODY);

        await Let_TheSweepRegisterEverything_Async();

        Append_SupervisorEntry(orchId, "anything new down there", "Tell me where you are.");

        var got = await Run_Until_Async(() => Read_ImplementerTicket(orchId) != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(got, "imp-1 never got a wake ticket, so nothing below could be about the pack");

        var ticket = Read_ImplementerTicket(orchId)!;

        Assert.NotNull(ticket.StatePackFile);
        Assert.True(File.Exists(ticket.StatePackFile), $"the ticket names a pack that is not on disk: {ticket.StatePackFile}");

        // WHERE THE ROLE COMMAND LOOKS. A pack written anywhere else would satisfy every assertion
        // above and be invisible to the session, whose boot sequence `ls`es exactly this path.
        Assert.Equal(StatePack_Locator.Get_File(_paths, SessionRoles.Implementer, orchId, "imp-1"), ticket.StatePackFile);

        var pack = File.ReadAllText(ticket.StatePackFile!);

        Assert.Contains("## Your brief", pack, StringComparison.Ordinal);
        Assert.Contains(BRIEF_SUBJECT, pack, StringComparison.Ordinal);
        Assert.Contains(BRIEF_BODY, pack, StringComparison.Ordinal);
    }

    /// <summary>
    /// A PACK IS THE MEMORY OF ONE TURN. A pack written once and left there would pass the case above
    /// for ever while handing the session the entries of a wake it already answered — so the second
    /// ticket has to bring a pack that names what woke it THIS time.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task EachTicketRewritesThePackWithTheEntriesThatWokeIt()
    {
        var orchId = Start_WithImplementer();

        await Let_TheSweepRegisterEverything_Async();

        Append_SupervisorEntry(orchId, "first nudge", "The first thing to look at.");

        var first = await Run_Until_Async(() => Read_ImplementerTicket(orchId)?.Number == 1, BridgeTestTiming.Window_ForTicks(10));
        Assert.True(first, "imp-1 never got its first ticket");

        var firstPack = File.ReadAllText(Read_ImplementerTicket(orchId)!.StatePackFile!);
        Assert.Contains("The first thing to look at.", firstPack, StringComparison.Ordinal);
        Assert.DoesNotContain("The second thing to look at.", firstPack, StringComparison.Ordinal);

        Append_SupervisorEntry(orchId, "second nudge", "The second thing to look at.");

        var second = await Run_Until_Async(() => Read_ImplementerTicket(orchId)?.Number == 2, BridgeTestTiming.Window_ForTicks(10));
        Assert.True(second, "imp-1 never got a second ticket, so the pack could not have been rewritten");

        var secondPack = File.ReadAllText(Read_ImplementerTicket(orchId)!.StatePackFile!);
        Assert.Contains("The second thing to look at.", secondPack, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE OPPOSITE ROAD, with a live control in the same ticks. A session still on the bash watcher
    /// must be handed NOTHING by this sweep — no ticket and no pack — because its monitor is still the
    /// thing that decides, and a pack written for a session nobody ticketed would be a file growing
    /// beside a session that never reads it. The supervisor of the same orchestration is woken by the
    /// owner in the very ticks that ticket imp-1.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AWatcherModeSessionGetsNeitherTicketNorPack_WhileTheTicketModeMemberBesideItGetsBoth()
    {
        var orchId = Start_WithImplementer();

        await Let_TheSweepRegisterEverything_Async();

        Assert.True(
            ChannelAppender.Append_OwnerEntry(_paths.Get_OwnerChannelFile(orchId), "status?", DateTime.Now),
            "the owner entry could not be appended, so the supervisor's silence would prove nothing");

        Append_SupervisorEntry(orchId, "get on with it", "Carry on.");

        var control = await Run_Until_Async(() => Read_ImplementerTicket(orchId) != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(control, "imp-1 never got a ticket, so the supervisor's silence beside it means nothing");
        Assert.True(File.Exists(Read_ImplementerTicket(orchId)!.StatePackFile), "imp-1's pack is missing, so this case is not comparing a pack against no pack");

        Assert.Null(WakeTicket_Store.Read_OrNull(WakeTicket_Store.Get_File(_paths, SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID)));
        Assert.False(File.Exists(StatePack_Locator.Get_File(_paths, SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID)));
    }

    string Start_WithImplementer()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;
        _launcher.Add_Implementer(orchId);

        return orchId;
    }

    /// <summary>
    /// LETS THE SWEEP SEE THE SESSION ONCE BEFORE THE CASE SEEDS THE TRAFFIC IT WAKES ON — the
    /// subject's own rule, not a harness convenience: a session registered with no cursors is
    /// BASELINED on the app's first sight of it, so everything already in its channels is history.
    /// That is exactly what makes the brief appended above it a brief rather than a pending entry.
    /// </summary>
    async Task Let_TheSweepRegisterEverything_Async()
    {
        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(4));
    }

    void Append_SupervisorEntry(string orchId, string subject, string body)
    {
        Assert.True(
            ChannelAppender.Append_SessionEntry(_paths.Get_ImplementerChannelFile(orchId, "imp-1"), ChannelAuthors.Supervisor, subject, body, DateTime.Now),
            $"'{subject}' could not be appended to imp-1, so this case would prove nothing");
    }

    IWakeTicket? Read_ImplementerTicket(string orchId)
    {
        return WakeTicket_Store.Read_OrNull(WakeTicket_Store.Get_File(_paths, SessionRoles.Implementer, orchId, "imp-1"));
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
