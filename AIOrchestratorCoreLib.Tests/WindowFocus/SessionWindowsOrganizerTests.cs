using AIOrchestratorCoreLib.Sessions.OrchestrationMember;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.WindowFocus;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.WindowFocus;

/// <summary>
/// WHICH WINDOWS AN ORCHESTRATION OWNS — the list Organize sizes its layout from.
///
/// The owner reported Organize "doesn't really work because in calculating windows count, and thus
/// positions and size, takes into account also closed terminals" (2026-08-20). Two things put
/// phantoms in that count: a CLOSED member, and — for a basic orchestration — a SUPERVISOR window
/// that was never spawned. Either one makes the layout reserve a tile for a window that is not
/// there, which is a hole on screen and everything else the wrong size.
///
/// The placement itself needs a real desktop and real terminals, so these pin the LIST, which is
/// where both phantoms came from.
/// </summary>
public class SessionWindowsOrganizerTests
{
    [Fact]
    public void ABasicOrchestrationOffersNoSupervisorWindow()
    {
        var fragments = SessionWindows_Organizer.Build_TitleFragments(Basic_WithSolo());

        Assert.DoesNotContain(fragments, fragment => fragment.StartsWith("SUP", StringComparison.Ordinal));
        Assert.Contains("SOLO · crm-2", fragments);
    }

    [Fact]
    public void ACrewOffersItsSupervisor()
    {
        var fragments = SessionWindows_Organizer.Build_TitleFragments(Crew_WithMembers());

        Assert.Contains("SUP · crm-2", fragments);
    }

    /// <summary>
    /// A closed member's terminal is gone, so reserving a tile for it leaves a hole and shrinks
    /// every other window for nothing.
    /// </summary>
    [Fact]
    public void ClosedMembersAreNotCounted()
    {
        var fragments = SessionWindows_Organizer.Build_TitleFragments(Crew_WithMembers());

        Assert.Contains("IMP-1 · crm-2", fragments);
        Assert.DoesNotContain("IMP-2 · crm-2", fragments);
    }

    /// <summary>A communicator only exists once one has been spawned.</summary>
    [Fact]
    public void ACommunicatorIsOfferedOnlyWhenItWasSpawned()
    {
        Assert.DoesNotContain(
            SessionWindows_Organizer.Build_TitleFragments(Crew_WithMembers()),
            fragment => fragment.StartsWith("COM", StringComparison.Ordinal));
    }

    /// <summary>
    /// WHICH WINDOW IS AN ORCHESTRATION'S MAIN ONE — the choice behind both /show and
    /// /organize_mains, and the reason the two cannot disagree about it.
    ///
    /// The owner asked for /organize_mains on 2026-08-21: *"does the terminal organization but for
    /// all sups and solos (no general sup)"*. The app already had an Organize-supervisors button
    /// that built `SUP · <orch>` for EVERY open orchestration — so a basic one, which has no
    /// supervisor at all, contributed a fragment that matched nothing and was silently dropped. Its
    /// solo, the very window the owner talks to, never appeared. These pin the rule that replaces it.
    /// </summary>
    [Fact]
    public void ACrewsMainWindowIsItsSupervisor()
    {
        Assert.Equal(["SUP · crm-2"], SessionWindows_Organizer.Build_MainWindowCandidates(Crew_WithMembers()));
    }

    /// <summary>The window is titled "SOLO · <orch>", never "SOLO-1" — there is only ever one.</summary>
    [Fact]
    public void ABasicOrchestrationsMainWindowIsItsSolo()
    {
        Assert.Equal(["SOLO · crm-2"], SessionWindows_Organizer.Build_MainWindowCandidates(Basic_WithSolo()));
    }

    /// <summary>
    /// NOT THE MEMBERS, NOT THE COMMUNICATOR. A crew's implementers and reviewers have windows and
    /// /organize tiles them; this is the OTHER question — one window per orchestration, the one the
    /// owner is talking to — so an implementer appearing here would tile a session they never
    /// address. The crew fixture has a live imp-1, and it must not be offered.
    /// </summary>
    [Fact]
    public void MembersAndCommunicatorsAreNeverMainWindows()
    {
        var candidates = SessionWindows_Organizer.Build_MainWindowCandidates(Crew_WithMembers());

        Assert.DoesNotContain(candidates, candidate => candidate.StartsWith("IMP", StringComparison.Ordinal));
        Assert.DoesNotContain(candidates, candidate => candidate.StartsWith("REV", StringComparison.Ordinal));
        Assert.DoesNotContain(candidates, candidate => candidate.StartsWith("COM", StringComparison.Ordinal));
    }

    /// <summary>
    /// A closed solo leaves nothing to organize. Returning its fragment would be the phantom-tile
    /// defect again, one orchestration wide: a tile reserved for a terminal that is gone.
    /// </summary>
    [Fact]
    public void AClosedSoloLeavesNoMainWindow()
    {
        var closed = OrchestrationSession_Factory.Create(
            "crm-2", "CRM", @"C:\repos\crm", DateTime.UtcNow, 799, null,
            null, null, null, null, null,
            [OrchestrationMember_Factory.Create("solo-1", null, DateTime.UtcNow, DateTime.UtcNow)],
            AIOrchestratorCoreLib.Telegram.TelegramDeliveryModes.Normal, null);

        Assert.Empty(SessionWindows_Organizer.Build_MainWindowCandidates(closed));
    }

    static IOrchestrationSession Basic_WithSolo()
    {
        return OrchestrationSession_Factory.Create(
            "crm-2", "CRM", @"C:\repos\crm", DateTime.UtcNow, 799, null,
            null, null, null, null, null,
            [OrchestrationMember_Factory.Create("solo-1", null, DateTime.UtcNow, null)],
            AIOrchestratorCoreLib.Telegram.TelegramDeliveryModes.Normal, null);
    }

    static IOrchestrationSession Crew_WithMembers()
    {
        return OrchestrationSession_Factory.Create(
            "crm-2", "CRM", @"C:\repos\crm", DateTime.UtcNow, 799, null,
            DateTime.UtcNow, null, null, null, null,
            [
                OrchestrationMember_Factory.Create("imp-1", null, DateTime.UtcNow, null),
                OrchestrationMember_Factory.Create("imp-2", null, DateTime.UtcNow, DateTime.UtcNow),
            ],
            AIOrchestratorCoreLib.Telegram.TelegramDeliveryModes.Normal, null);
    }
}
