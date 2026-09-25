using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// MEMBERSHIP IS DERIVED FROM THE LIST, never stored (spec §3.2). These build the list in memory: the
/// resolver takes whatever <c>Load_All()</c> already returned this tick, so no disk is involved.
/// </summary>
public class EndeavourMembersResolverTests
{
    static readonly DateTime CREATED = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

    static IOrchestrationSession Build_Session(string orchId, string? endeavourId, DateTime? closedUtc = null)
    {
        return OrchestrationSession_Factory.Create(
            orchId, "AIOrchestrator", @"C:\repo", CREATED, null, null, null, null, null, null, null, [],
            TelegramDeliveryModes.Normal, closedUtc, endeavourId: endeavourId);
    }

    static string[] Ids(IReadOnlyList<IOrchestrationSession> sessions)
    {
        return sessions.Select(session => session.OrchId).ToArray();
    }

    [Fact]
    public void OpenSiblings_ExcludeSelf()
    {
        var parent = Build_Session("ai-orchestrator-7", "ai-orchestrator-7");
        var child = Build_Session("ai-orchestrator-8", "ai-orchestrator-7");

        var siblings = EndeavourMembers_Resolver.Resolve_OpenSiblings([parent, child], parent);

        Assert.Equal(["ai-orchestrator-8"], Ids(siblings));
    }

    [Fact]
    public void OpenSiblings_IncludeOnlyTheSameEndeavour()
    {
        var parent = Build_Session("ai-orchestrator-7", "ai-orchestrator-7");
        var child = Build_Session("ai-orchestrator-8", "ai-orchestrator-7");
        var otherParent = Build_Session("crm-2", "crm-2");
        var otherChild = Build_Session("crm-3", "crm-2");
        var unlinked = Build_Session("option-lab-4", null);

        var siblings = EndeavourMembers_Resolver.Resolve_OpenSiblings([otherParent, parent, unlinked, otherChild, child], child);

        Assert.Equal(["ai-orchestrator-7"], Ids(siblings));
    }

    [Fact]
    public void OpenSiblings_ExcludeClosedMembers()
    {
        var parent = Build_Session("ai-orchestrator-7", "ai-orchestrator-7");
        var closedChild = Build_Session("ai-orchestrator-8", "ai-orchestrator-7", CREATED.AddHours(2));
        var openChild = Build_Session("ai-orchestrator-9", "ai-orchestrator-7");

        var siblings = EndeavourMembers_Resolver.Resolve_OpenSiblings([parent, closedChild, openChild], parent);

        Assert.Equal(["ai-orchestrator-9"], Ids(siblings));
    }

    /// <summary>
    /// An unlinked session has no endeavour, so it has no siblings — even when other sessions share
    /// its repo and are linked among themselves. Null must never match null.
    /// </summary>
    [Fact]
    public void OpenSiblings_OfAnUnlinkedSession_IsEmpty()
    {
        var lonely = Build_Session("ai-orchestrator-6", null);
        var otherUnlinked = Build_Session("ai-orchestrator-5", null);
        var parent = Build_Session("ai-orchestrator-7", "ai-orchestrator-7");
        var child = Build_Session("ai-orchestrator-8", "ai-orchestrator-7");

        var siblings = EndeavourMembers_Resolver.Resolve_OpenSiblings([lonely, otherUnlinked, parent, child], lonely);

        Assert.Empty(siblings);
    }

    /// <summary>
    /// §3.5: a closed sibling stays in the endeavour's sum, or the bar would go backwards at the
    /// moment a finished job closes. So "all" means open AND closed, in the order the store gave.
    /// </summary>
    [Fact]
    public void All_IncludesClosedMembers_InStoreOrder()
    {
        var child = Build_Session("ai-orchestrator-9", "ai-orchestrator-7");
        var unrelated = Build_Session("crm-2", null);
        var closedChild = Build_Session("ai-orchestrator-8", "ai-orchestrator-7", CREATED.AddHours(2));
        var parent = Build_Session("ai-orchestrator-7", "ai-orchestrator-7");

        var members = EndeavourMembers_Resolver.Resolve_All([child, unrelated, closedChild, parent], "ai-orchestrator-7");

        Assert.Equal(["ai-orchestrator-9", "ai-orchestrator-8", "ai-orchestrator-7"], Ids(members));
    }

    /// <summary>
    /// O2 caps open MEMBERS, the requester included — so the count is of every open session in the
    /// endeavour, not of the requester's siblings.
    /// </summary>
    [Fact]
    public void CountOpen_CountsTheRequesterToo()
    {
        var parent = Build_Session("ai-orchestrator-7", "ai-orchestrator-7");
        var child = Build_Session("ai-orchestrator-8", "ai-orchestrator-7");
        var closedChild = Build_Session("ai-orchestrator-9", "ai-orchestrator-7", CREATED.AddHours(2));
        var unrelated = Build_Session("crm-2", "crm-2");

        var count = EndeavourMembers_Resolver.Count_Open([parent, child, closedChild, unrelated], "ai-orchestrator-7");

        Assert.Equal(2, count);
    }
}
