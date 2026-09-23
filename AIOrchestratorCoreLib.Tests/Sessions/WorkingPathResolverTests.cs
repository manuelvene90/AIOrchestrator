using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// ONE DEFINITION OF "WHERE THIS SESSION RUNS" (spec §6, decision 12): the launcher, the validator and
/// the digest all read it here, so none of them can come to disagree about a sibling's tree.
/// </summary>
public class WorkingPathResolverTests
{
    static readonly DateTime CREATED = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

    static IOrchestrationSession Build_Session(string? workingPath)
    {
        return OrchestrationSession_Factory.Create(
            "ai-orchestrator-8", "AIOrchestrator", @"C:\repo", CREATED, null, null, null, null, null, null, null, [],
            TelegramDeliveryModes.Normal, null, workingPath: workingPath);
    }

    [Fact]
    public void Resolve_NoWorkingPath_IsTheRepoPath()
    {
        Assert.Equal(@"C:\repo", WorkingPath_Resolver.Resolve(Build_Session(null)));
    }

    [Fact]
    public void Resolve_AWorkingPath_IsThatPath()
    {
        Assert.Equal(@"C:\repo.worktrees\limits", WorkingPath_Resolver.Resolve(Build_Session(@"C:\repo.worktrees\limits")));
    }
}
