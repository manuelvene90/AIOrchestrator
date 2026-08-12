using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

public class MemberStateResolverTests
{
    static IChannelEntry Build_Entry(int index, ChannelAuthors author, string subject, string body)
    {
        return ChannelEntry_Factory.Create(index, author, "2026-08-06", subject, body, $"## [{index}] FROM x — d — {subject}\n{body}");
    }

    [Fact]
    public void Resolve_NoEntries_NewNoTraffic()
    {
        Assert.Equal(MemberStates.NewNoTraffic, MemberState_Resolver.Resolve([]));
    }

    [Fact]
    public void Resolve_LastEntryFromSupervisor_ImplementerWorking()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "orders", "do X"),
        };

        Assert.Equal(MemberStates.ImplementerWorking, MemberState_Resolver.Resolve(entries));
    }

    [Fact]
    public void Resolve_LastEntryFromImplementer_AwaitingSupervisorReview()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "orders", "do X"),
            Build_Entry(2, ChannelAuthors.Implementer, "report", "done X"),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }

    [Fact]
    public void Resolve_ImplementerReportsBlockedOnOwner_BlockedOnOwner()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "orders", "do X"),
            Build_Entry(2, ChannelAuthors.Implementer, "report", "🚩 BLOCKED ON OWNER — need a product decision"),
        };

        Assert.Equal(MemberStates.BlockedOnOwner, MemberState_Resolver.Resolve(entries));
    }

    [Fact]
    public void Resolve_WritingWindowOpenWithoutClose_WritingWindowOpen()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "orders", "do X"),
            Build_Entry(2, ChannelAuthors.Implementer, "WRITING WINDOW OPEN", "starting the batch"),
        };

        Assert.Equal(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    [Fact]
    public void Resolve_WritingWindowOpenedThenClosed_FallsBackToLastEntryRule()
    {
        // The brief is part of the fixture because it is part of every real channel: a member is
        // only ever "awaiting review" on work it was asked to do. Without it this reads as a member
        // that has never been briefed, which is waiting for WORK, not for a verdict.
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "BRIEF", "fix the five defects"),
            Build_Entry(2, ChannelAuthors.Implementer, "WRITING WINDOW OPEN", "starting"),
            Build_Entry(3, ChannelAuthors.Implementer, "WRITING WINDOW CLOSED", "five fixes landed"),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }

    [Fact]
    public void Resolve_MutationWindowOpen_ReportsWindowOpen()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Implementer, "MUTATION WINDOW OPEN", "running the seven mutations"),
        };

        Assert.Equal(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    [Fact]
    public void Resolve_WindowReopenedAfterClose_WindowWinsAgain()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Implementer, "WRITING WINDOW OPEN", "batch 1"),
            Build_Entry(2, ChannelAuthors.Implementer, "WRITING WINDOW CLOSED", "batch 1 done"),
            Build_Entry(3, ChannelAuthors.Implementer, "WRITING WINDOW OPEN", "batch 2"),
        };

        Assert.Equal(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }
}
