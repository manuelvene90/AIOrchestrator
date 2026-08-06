using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.GeneralSupervision;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

public class RepoQueryResolverTests
{
    static readonly IReadOnlyList<IRepoEntry> REPOS =
    [
        RepoEntry_Factory.Create("Skeleton", @"C:\repos\skeleton"),
        RepoEntry_Factory.Create("Skeleton Master", @"C:\repos\skeleton-master"),
        RepoEntry_Factory.Create("Arb Studio", @"C:\repos\arb"),
        RepoEntry_Factory.Create("AI Orchestrator", @"C:\repos\aiorch"),
    ];

    [Fact]
    public void Resolve_ExactNameCaseInsensitive_Matches()
    {
        var repo = RepoQuery_Resolver.Resolve_OrNull("arb studio", REPOS);

        Assert.NotNull(repo);
        Assert.Equal("Arb Studio", repo.Name);
    }

    [Fact]
    public void Resolve_ExactMatchWins_EvenWhenAlsoASubstringOfAnother()
    {
        var repo = RepoQuery_Resolver.Resolve_OrNull("skeleton", REPOS);

        Assert.NotNull(repo);
        Assert.Equal("Skeleton", repo.Name);
    }

    [Fact]
    public void Resolve_UniqueSubstring_Matches()
    {
        var repo = RepoQuery_Resolver.Resolve_OrNull("orchestrator", REPOS);

        Assert.NotNull(repo);
        Assert.Equal("AI Orchestrator", repo.Name);
    }

    [Fact]
    public void Resolve_AmbiguousSubstring_ReturnsNull()
    {
        Assert.Null(RepoQuery_Resolver.Resolve_OrNull("skel", REPOS));
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsNull()
    {
        Assert.Null(RepoQuery_Resolver.Resolve_OrNull("seasonal", REPOS));
    }

    [Fact]
    public void Resolve_IgnoresSpacingAndDashes()
    {
        var repo = RepoQuery_Resolver.Resolve_OrNull("skeleton-master", REPOS);

        Assert.NotNull(repo);
        Assert.Equal("Skeleton Master", repo.Name);
    }
}
