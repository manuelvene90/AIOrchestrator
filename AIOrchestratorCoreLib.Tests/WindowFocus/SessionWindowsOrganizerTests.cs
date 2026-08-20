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
