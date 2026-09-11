using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// Which channels wake which role. This is the table that used to be
/// <c>PrintSessionState_Store.Resolve_ChannelFile</c>'s single answer, and the supervisor's row is the
/// whole of the change: the owner channel plus every OPEN member's spoke, resolved from the roster each
/// time it is asked so a member added mid-life needs nothing re-registered.
/// </summary>
public class TurnSourcesResolverTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;

    public TurnSourcesResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-turn-sources-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _store = OrchestrationSessionStore_Factory.Create(_paths);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void ASupervisor_IsWokenByTheOwnerChannelAndEveryOpenSpoke()
    {
        _store.Create_Orchestration("o", "Repo", _tempRoot);
        _store.Add_Member("o", MemberKinds.Implementer);
        _store.Add_Member("o", MemberKinds.Reviewer);
        _store.Add_Member("o", MemberKinds.Implementer);
        _store.Close_Member("o", "imp-2");

        var sources = TurnSources_Resolver.Resolve(_paths, _store, SessionRoles.Supervisor, "o", "sup");

        Assert.Equal(["owner", "imp-1", "rev-1"], sources.Select(source => source.Key));
        Assert.Equal(_paths.Get_OwnerChannelFile("o"), sources[0].ChannelFilePath);
        Assert.Equal(_paths.Get_ImplementerChannelFile("o", "imp-1"), sources[1].ChannelFilePath);
        Assert.True(sources[0].IsOwnerChannel);
        Assert.False(sources[1].IsOwnerChannel);
    }

    /// <summary>
    /// A BASIC orchestration's solo writes into the owner channel rather than a spoke of its own
    /// (<see cref="MemberChannel_Locator"/>), so a naive roster walk lists the same file twice — once as
    /// <c>owner</c> and once as <c>solo-1</c> — and every entry in it would then be delivered twice under
    /// two cursors. Deduped by PATH, which is what actually collides.
    /// </summary>
    [Fact]
    public void ASoloDoesNotDoubleTheOwnerChannel()
    {
        _store.Create_Orchestration("b", "Repo", _tempRoot);
        _store.Add_Member("b", MemberKinds.Solo);

        var sources = TurnSources_Resolver.Resolve(_paths, _store, SessionRoles.Supervisor, "b", "sup");

        Assert.Equal("owner", Assert.Single(sources).Key);
    }

    [Theory]
    [InlineData(SessionRoles.Implementer, "imp-1")]
    [InlineData(SessionRoles.Reviewer, "rev-1")]
    [InlineData(SessionRoles.Solo, "solo-1")]
    [InlineData(SessionRoles.General, "general")]
    public void EveryOtherRole_HasExactlyOneChannel(SessionRoles role, string memberId)
    {
        _store.Create_Orchestration("o", "Repo", _tempRoot);

        Assert.Single(TurnSources_Resolver.Resolve(_paths, _store, role, "o", memberId));
    }

    [Fact]
    public void AnUnknownOrchestration_LeavesTheSupervisorWithItsOwnChannel()
    {
        var sources = TurnSources_Resolver.Resolve(_paths, _store, SessionRoles.Supervisor, "never-created", "sup");

        Assert.Equal("owner", Assert.Single(sources).Key);
    }
}
