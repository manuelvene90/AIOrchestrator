using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationMember;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// "IS THE SESSION THE OWNER IS WAITING ON BUSY RIGHT NOW?" was answered by reading the SUPERVISOR's
/// usage file, in three places that each spelled the path themselves. In a basic orchestration
/// nothing ever writes that file — the solo writes its own, inside its member folder — so a solo
/// read as idle forever. The consequences all landed on the owner: they were told "still waiting for
/// your reply" while the solo was mid-turn, they never got the "busy — working on X" narration the
/// crew gets, and every one of those notices woke the session again for nothing.
///
/// One locator now, so the three readers cannot disagree about who talks to the owner.
/// </summary>
public class OwnerFacingSessionLocatorTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public OwnerFacingSessionLocatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-ownerfacing-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    static IOrchestrationMember Member(string memberId, DateTime? closedUtc = null)
    {
        return OrchestrationMember_Factory.Create(memberId, null, null, closedUtc);
    }

    static IOrchestrationSession Session(DateTime? supervisorSpawnedUtc, params IOrchestrationMember[] members)
    {
        return OrchestrationSession_Factory.Create(
            "arb-fix", "arb", @"C:\repos\arb", DateTime.UtcNow, null, null,
            supervisorSpawnedUtc, null, null, null, null, members, TelegramDeliveryModes.Normal, null);
    }

    [Fact]
    public void ACrew_ReadsTheSupervisorsUsageFile()
    {
        var session = Session(DateTime.UtcNow, Member("imp-1"));

        var usageFile = OwnerFacingSession_Locator.Get_UsageFile(_paths, "arb-fix", session);

        Assert.Equal(Path.Combine(_paths.Get_OrchestrationFolder("arb-fix"), ".usage.json"), usageFile);
    }

    [Fact]
    public void ABasicOrchestration_ReadsTheSolosOwnUsageFile_NotTheEmptySupervisorSlot()
    {
        var session = Session(supervisorSpawnedUtc: null, Member("solo-1"));

        var usageFile = OwnerFacingSession_Locator.Get_UsageFile(_paths, "arb-fix", session);

        Assert.Equal(Path.Combine(_paths.Get_ImplementerFolder("arb-fix", "solo-1"), ".usage.json"), usageFile);
    }

    /// <summary>
    /// A promoted orchestration keeps its closed solo in the roster for ever (member folders are
    /// audit trail), so "find a solo" must mean a LIVE one — otherwise the promoted supervisor's
    /// turns would be judged by a file its retired predecessor stopped writing.
    /// </summary>
    [Fact]
    public void APromotedOrchestration_ReadsTheSupervisor_NotTheRetiredSolo()
    {
        var session = Session(DateTime.UtcNow, Member("solo-1", closedUtc: DateTime.UtcNow), Member("imp-1"));

        var usageFile = OwnerFacingSession_Locator.Get_UsageFile(_paths, "arb-fix", session);

        Assert.Equal(Path.Combine(_paths.Get_OrchestrationFolder("arb-fix"), ".usage.json"), usageFile);
    }

    /// <summary>
    /// Basic, and its solo has been closed: there is nobody talking to the owner at all. The
    /// supervisor slot is the honest answer — nothing writes it, so "mid-turn" reads false, which is
    /// exactly true. The alternative, pointing at a closed member's stale file, would report a turn
    /// that ended hours ago as still running.
    /// </summary>
    [Fact]
    public void ABasicOrchestrationWithNoLiveSolo_FallsBackToTheSlotNobodyWrites()
    {
        var session = Session(supervisorSpawnedUtc: null, Member("solo-1", closedUtc: DateTime.UtcNow));

        var usageFile = OwnerFacingSession_Locator.Get_UsageFile(_paths, "arb-fix", session);

        Assert.Equal(Path.Combine(_paths.Get_OrchestrationFolder("arb-fix"), ".usage.json"), usageFile);
    }

    [Fact]
    public void TheGeneralSupervisor_ReadsItsOwnFolder_WhichHasNoSession()
    {
        var usageFile = OwnerFacingSession_Locator.Get_UsageFile(_paths, ChannelDiscovery.GENERAL_ORCH_ID, session: null);

        Assert.Equal(Path.Combine(_paths.GeneralFolder, ".usage.json"), usageFile);
    }

    /// <summary>
    /// A session the store could not produce is not a reason to invent a member folder: the orch slot
    /// is the same answer the three call sites gave before, so an unknown orchestration behaves
    /// exactly as it always did rather than newly throwing inside the mirror tick.
    /// </summary>
    [Fact]
    public void AnUnknownOrchestration_FallsBackToTheOrchestrationSlot()
    {
        var usageFile = OwnerFacingSession_Locator.Get_UsageFile(_paths, "arb-fix", session: null);

        Assert.Equal(Path.Combine(_paths.Get_OrchestrationFolder("arb-fix"), ".usage.json"), usageFile);
    }

    // WHOSE TURN THE DISPATCHER IS ASKED ABOUT — the same three shapes, for the readers of its record.

    [Fact]
    public void ACrew_IsAskedAboutAsTheSupervisor()
    {
        var session = Session(DateTime.UtcNow, Member("imp-1"));

        Assert.Equal((SessionRoles.Supervisor, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID), OwnerFacingSession_Locator.Resolve_Session("arb-fix", session));
    }

    [Fact]
    public void ABasicOrchestration_IsAskedAboutAsItsSolo()
    {
        var session = Session(supervisorSpawnedUtc: null, Member("solo-1"));

        Assert.Equal((SessionRoles.Solo, "solo-1"), OwnerFacingSession_Locator.Resolve_Session("arb-fix", session));
    }

    [Fact]
    public void APromotedOrchestration_IsAskedAboutAsTheSupervisor_NotTheRetiredSolo()
    {
        var session = Session(DateTime.UtcNow, Member("solo-1", closedUtc: DateTime.UtcNow), Member("imp-1"));

        Assert.Equal((SessionRoles.Supervisor, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID), OwnerFacingSession_Locator.Resolve_Session("arb-fix", session));
    }

    [Fact]
    public void TheGeneralSupervisor_IsAskedAboutAsItself()
    {
        Assert.Equal((SessionRoles.General, SessionLaunch_Factory.GENERAL_MEMBER_ID), OwnerFacingSession_Locator.Resolve_Session(ChannelDiscovery.GENERAL_ORCH_ID, session: null));
    }

    /// <summary>
    /// The incident, through the real dispatcher (ai-orch-2, 2026-09-11: "your turn ended without
    /// answering" 2 minutes into a running turn). A headless solo is mid-turn; the key the owner-reply
    /// loop now asks about sees it, and the key it used to ask about — a supervisor a basic
    /// orchestration never registers — does not. The second assertion is the control: without it the
    /// first would pass for a dispatcher that answered true to anything.
    /// </summary>
    [Fact]
    public async Task AHeadlessSoloMidTurn_IsInFlightUnderTheResolvedKey_AndNotUnderTheSupervisors()
    {
        using var harness = new PrintRunnerTestHarness("solo");
        var (orchId, _) = harness.Register_Member(MemberKinds.Solo);
        harness.Write_Scenario("""{"default":{"result":"online","delay_ms":20000}}""");

        var (_, memberId) = OwnerFacingSession_Locator.Resolve_Session(orchId, harness.Store.Get_Session_OrNull(orchId));

        // The solo's boot turn is its first turn and needs nothing said to it.
        var dispatcher = harness.Create_Dispatcher();
        var started = PrintRunnerTestHarness.Drive_Until(dispatcher, () => dispatcher.Is_TurnInFlight(orchId, memberId) && !dispatcher.Is_TurnQueued(orchId, memberId), PrintRunnerTestHarness.GENEROUS);
        var supervisorKeyInFlight = dispatcher.Is_TurnInFlight(orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);
        await dispatcher.Stop_Async();

        Assert.True(started, $"the solo's turn was never seen running under '{memberId}'");
        Assert.False(supervisorKeyInFlight, "the supervisor key reported a turn a basic orchestration never registers");
    }
}
