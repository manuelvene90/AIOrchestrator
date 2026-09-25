using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE POINT OF EFFECT OF <c>topic.repoColours</c>, moved out of <c>BridgeEngineModel.cs</c> by plan 03
/// task 14 (code-conventions: a piece the stage touches moves out). The engine proof is
/// <c>TopicColourFollowsTheSettingTests</c>; this pins each branch on its own, with a real
/// config.json because "nothing was written" is half of what off means.
/// </summary>
public class RepoTopicColourResolverTests : IDisposable
{
    const string CONFIG = """{"repos":[{"name":"alpha","path":"/tmp/a"},{"name":"beta","path":"/tmp/b"}]}""";

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public RepoTopicColourResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-repo-topic-colour-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        File.WriteAllText(_paths.ConfigFile, CONFIG);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Off_AnswersNoColour_AndLeavesConfigJsonByteForByte()
    {
        var (colour, warning) = RepoTopicColour_Resolver.Resolve(repoColoursOn: false, Repos(), "alpha", _paths);

        Assert.Null(colour);
        Assert.Null(warning);
        Assert.Equal(CONFIG, File.ReadAllText(_paths.ConfigFile));
    }

    /// <summary>Off wins over a colour already on file — and does not erase it, so on again restores the same one.</summary>
    [Fact]
    public void Off_WithAColourAlreadyOnFile_AnswersNoColour_AndKeepsTheFileAsItIs()
    {
        IReadOnlyList<IRepoEntry> repos = [RepoEntry_Factory.Create("alpha", "/tmp/a", TopicColor_Rotation.PALETTE[2])];

        var (colour, _) = RepoTopicColour_Resolver.Resolve(repoColoursOn: false, repos, "alpha", _paths);

        Assert.Null(colour);
        Assert.Equal(CONFIG, File.ReadAllText(_paths.ConfigFile));
    }

    [Fact]
    public void On_ANewRepo_TakesTheNextColourOfTheRotation_AndItIsWrittenDown()
    {
        IReadOnlyList<IRepoEntry> repos =
        [
            RepoEntry_Factory.Create("alpha", "/tmp/a", TopicColor_Rotation.PALETTE[0]),
            RepoEntry_Factory.Create("beta", "/tmp/b", null),
        ];

        var (colour, warning) = RepoTopicColour_Resolver.Resolve(repoColoursOn: true, repos, "beta", _paths);

        Assert.Equal(TopicColor_Rotation.PALETTE[1], colour);
        Assert.Null(warning);
        Assert.Equal(TopicColor_Rotation.PALETTE[1], OrchestratorConfig_Loader.Load_OrEmpty(_paths).Repos.Single(repo => repo.Name == "beta").TopicColor);
    }

    [Fact]
    public void On_ARepoWithAColour_KeepsIt_AndWritesNothing()
    {
        IReadOnlyList<IRepoEntry> repos = [RepoEntry_Factory.Create("alpha", "/tmp/a", TopicColor_Rotation.PALETTE[4])];

        var (colour, _) = RepoTopicColour_Resolver.Resolve(repoColoursOn: true, repos, "ALPHA", _paths);

        Assert.Equal(TopicColor_Rotation.PALETTE[4], colour);
        Assert.Equal(CONFIG, File.ReadAllText(_paths.ConfigFile));
    }

    /// <summary>A repository no longer in config.json gets no colour rather than a wrong one.</summary>
    [Fact]
    public void On_ARepoNotInTheConfig_AnswersNoColour()
    {
        var (colour, warning) = RepoTopicColour_Resolver.Resolve(repoColoursOn: true, Repos(), "gone", _paths);

        Assert.Null(colour);
        Assert.Null(warning);
    }

    /// <summary>
    /// A colour that cannot be written is still USED, and the caller is handed a warning to log — the
    /// topic is never the price of a dot.
    /// </summary>
    [Fact]
    public void On_AColourThatCannotBeWritten_IsStillUsed_AndComesBackWithAWarning()
    {
        // The repos were read before the file went bad — the provider's copy is older than the write.
        var repos = Repos();
        File.WriteAllText(_paths.ConfigFile, "{ not json at all");

        var (colour, warning) = RepoTopicColour_Resolver.Resolve(repoColoursOn: true, repos, "alpha", _paths);

        Assert.Equal(TopicColor_Rotation.PALETTE[0], colour);
        Assert.NotNull(warning);
        Assert.Contains("alpha", warning, StringComparison.Ordinal);
    }

    IReadOnlyList<IRepoEntry> Repos()
    {
        return OrchestratorConfig_Loader.Load_OrEmpty(_paths).Repos;
    }
}
