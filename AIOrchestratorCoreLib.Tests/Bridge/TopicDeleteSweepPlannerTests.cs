using AIOrchestratorCoreLib.Bridge.TopicDeletion;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// WHICH CLOSED ORCHESTRATIONS THE NEXT START STILL OWES A DELETE — and, just as much, which ones it
/// must leave alone.
///
/// The case that would have been missed: every orchestration closed before brief E1 shipped carries
/// no pending stamp, and its topic almost certainly went away at the time. A planner that selected on
/// "closed and not recorded deleted" would fire a delete at every one of them on the first start after
/// the upgrade — a burst of housekeeping calls against an app whose message allowance is a real
/// constraint, aimed at thread ids that no longer exist.
/// </summary>
public class TopicDeleteSweepPlannerTests
{
    [Fact]
    public void APendingDeleteThatNeverLanded_IsSelected()
    {
        var session = Build("orch-1", topicId: 100, pendingUtc: new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc), deletedUtc: null);

        Assert.True(TopicDeleteSweep_Planner.Needs_Delete(session));
        Assert.Equal(["orch-1"], TopicDeleteSweep_Planner.Select_PendingDeletes([session]).Select(s => s.OrchId));
    }

    [Fact]
    public void ADeleteTelegramConfirmed_IsNotSelectedAgain()
    {
        var session = Build("orch-1", topicId: 100,
            pendingUtc: new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            deletedUtc: new DateTime(2026, 9, 10, 8, 0, 5, DateTimeKind.Utc));

        Assert.False(TopicDeleteSweep_Planner.Needs_Delete(session));
        Assert.Empty(TopicDeleteSweep_Planner.Select_PendingDeletes([session]));
    }

    /// <summary>
    /// THE MIGRATION CASE, and the reason the pending stamp is the key rather than ClosedUtc. This is
    /// every orchestration in the owner's supervision root on the day this ships.
    /// </summary>
    [Fact]
    public void AnOrchestrationClosedBeforeThisFeature_IsNeverSweptUp()
    {
        var legacy = Build("orch-from-august", topicId: 100, pendingUtc: null, deletedUtc: null,
            closedUtc: new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc));

        Assert.False(TopicDeleteSweep_Planner.Needs_Delete(legacy));
        Assert.Empty(TopicDeleteSweep_Planner.Select_PendingDeletes([legacy]));
    }

    [Fact]
    public void AnOrchestrationWithNoTopicId_IsNotSelected()
    {
        var session = Build("orch-1", topicId: null, pendingUtc: new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc), deletedUtc: null);

        Assert.False(TopicDeleteSweep_Planner.Needs_Delete(session));
    }

    /// <summary>Oldest first, so a backlog is worked through in the order it accumulated.</summary>
    [Fact]
    public void PendingDeletes_ComeBackOldestFirst()
    {
        var newer = Build("orch-newer", topicId: 2, pendingUtc: new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc), deletedUtc: null);
        var older = Build("orch-older", topicId: 1, pendingUtc: new DateTime(2026, 9, 9, 9, 0, 0, DateTimeKind.Utc), deletedUtc: null);
        var settled = Build("orch-done", topicId: 3, pendingUtc: new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc), deletedUtc: new DateTime(2026, 9, 8, 9, 0, 1, DateTimeKind.Utc));

        var selected = TopicDeleteSweep_Planner.Select_PendingDeletes([newer, settled, older]);

        Assert.Equal(["orch-older", "orch-newer"], selected.Select(s => s.OrchId));
    }

    static IOrchestrationSession Build(string orchId, long? topicId, DateTime? pendingUtc, DateTime? deletedUtc, DateTime? closedUtc = null)
    {
        return OrchestrationSession_Factory.Create(
            orchId, "repo", "/tmp/repo", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            topicId, null, null, null, null, null, null, [],
            AIOrchestratorCoreLib.Telegram.TelegramDeliveryModes.Normal,
            closedUtc ?? new DateTime(2026, 9, 10, 7, 59, 0, DateTimeKind.Utc),
            telegramTopicDeletePendingUtc: pendingUtc,
            telegramTopicDeletedUtc: deletedUtc);
    }
}
