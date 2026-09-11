using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// A COLOUR PER REPOSITORY, STABLE FOR AS LONG AS THE REPOSITORY EXISTS — brief F1.
///
/// The property worth pinning is DISTINCTNESS, which is what a rotation buys over a hash: with six
/// repositories or fewer no two share a colour, so the dot in the topic list actually says which
/// endeavour a topic belongs to.
/// </summary>
public class TopicColorRotationTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public TopicColorRotationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-topiccolour-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
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
    }

    [Fact]
    public void TheFirstSixRepositories_AllGetDifferentColours()
    {
        List<int> assigned = [];

        for (var i = 0; i < TopicColor_Rotation.PALETTE.Count; i++)
            assigned.Add(TopicColor_Rotation.Pick_ForNewRepo(assigned));

        Assert.Equal(TopicColor_Rotation.PALETTE.Count, assigned.Distinct().Count());
        Assert.Equal(TopicColor_Rotation.PALETTE, assigned);
    }

    /// <summary>Six is Telegram's limit, not ours — the seventh reuses the first.</summary>
    [Fact]
    public void TheSeventhRepository_ReusesTheFirstColour()
    {
        List<int> assigned = [.. TopicColor_Rotation.PALETTE];

        Assert.Equal(TopicColor_Rotation.PALETTE[0], TopicColor_Rotation.Pick_ForNewRepo(assigned));
    }

    /// <summary>A gap left by a removed repository is filled rather than skipped past.</summary>
    [Fact]
    public void AColourFreedByARemovedRepository_IsHandedOutAgain()
    {
        List<int> assigned = [.. TopicColor_Rotation.PALETTE];
        assigned.Remove(TopicColor_Rotation.PALETTE[2]);

        Assert.Equal(TopicColor_Rotation.PALETTE[2], TopicColor_Rotation.Pick_ForNewRepo(assigned));
    }

    /// <summary>
    /// Telegram accepts SIX values and refuses the whole call for anything else — which would cost
    /// the orchestration its topic, not just its dot. config.json is hand-edited, so this is
    /// reachable rather than theoretical.
    /// </summary>
    [Fact]
    public void OnlyTelegramsOwnSixAreEverPermitted()
    {
        Assert.Equal(6, TopicColor_Rotation.PALETTE.Count);

        foreach (var colour in TopicColor_Rotation.PALETTE)
            Assert.True(TopicColor_Rotation.Is_Permitted(colour));

        Assert.False(TopicColor_Rotation.Is_Permitted(0x123456));
        Assert.False(TopicColor_Rotation.Is_Permitted(0));
        Assert.False(TopicColor_Rotation.Is_Permitted(-1));
    }

    [Fact]
    public void ARepoEntryWithAColourTelegramWouldRefuse_KeepsNoColourAtAll()
    {
        Assert.Null(RepoEntry_Factory.Create("repo", "/tmp/repo", 0x123456).TopicColor);
        Assert.Equal(TopicColor_Rotation.PALETTE[0], RepoEntry_Factory.Create("repo", "/tmp/repo", TopicColor_Rotation.PALETTE[0]).TopicColor);
    }

    // ---- persistence ----

    /// <summary>
    /// THE REASON IT IS PERSISTED AT ALL: the repo list is reordered at runtime, and a colour
    /// derived from a position would change under the owner every time they dragged a row.
    /// </summary>
    [Fact]
    public void AColourSurvivesAReorderOfTheRepoList()
    {
        File.WriteAllText(_paths.ConfigFile,
            "{\"repos\":[{\"name\":\"alpha\",\"path\":\"/tmp/a\"},{\"name\":\"beta\",\"path\":\"/tmp/b\"}],\"planBackend\":{\"kind\":\"kept\"}}");

        Assert.True(ConfigRepoColor_Writer.Persist_Colour(_paths, "beta", TopicColor_Rotation.PALETTE[3]));

        ConfigRepos_Reorderer.Persist_Order(_paths, ["beta", "alpha"]);

        var repos = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Repos;

        Assert.Equal("beta", repos[0].Name);
        Assert.Equal(TopicColor_Rotation.PALETTE[3], repos[0].TopicColor);
        Assert.Null(repos[1].TopicColor);
    }

    /// <summary>
    /// Agents edit config.json at runtime, so a write that rebuilt the file from this app's model
    /// would delete every key it does not know about — the defect OrchestratorConfig_Loader.Save
    /// already carries a paragraph about.
    /// </summary>
    [Fact]
    public void WritingAColour_LeavesEveryOtherKeyUntouched()
    {
        File.WriteAllText(_paths.ConfigFile,
            "{\"repos\":[{\"name\":\"alpha\",\"path\":\"/tmp/a\",\"somethingThisVersionDoesNotKnow\":42}],\"planBackend\":{\"kind\":\"kept\"}}");

        Assert.True(ConfigRepoColor_Writer.Persist_Colour(_paths, "alpha", TopicColor_Rotation.PALETTE[1]));

        var text = File.ReadAllText(_paths.ConfigFile);

        Assert.Contains("somethingThisVersionDoesNotKnow", text);
        Assert.Contains("planBackend", text);
        Assert.Equal(TopicColor_Rotation.PALETTE[1], OrchestratorConfig_Loader.Load_OrEmpty(_paths).Repos[0].TopicColor);
    }

    /// <summary>
    /// A colour is not worth failing a topic for: every way this can go wrong returns false and
    /// the caller carries on.
    /// </summary>
    [Theory]
    [InlineData("{\"repos\":[{\"name\":\"other\",\"path\":\"/tmp/o\"}]}")]
    [InlineData("{\"repos\":[]}")]
    [InlineData("{\"norepos\":1}")]
    [InlineData("{ not json at all")]
    public void AColourThatCannotBeWritten_IsReportedRatherThanThrown(string configContent)
    {
        File.WriteAllText(_paths.ConfigFile, configContent);

        Assert.False(ConfigRepoColor_Writer.Persist_Colour(_paths, "alpha", TopicColor_Rotation.PALETTE[0]));
    }

    [Fact]
    public void WithNoConfigFileAtAll_NothingThrows()
    {
        Assert.False(ConfigRepoColor_Writer.Persist_Colour(_paths, "alpha", TopicColor_Rotation.PALETTE[0]));
    }

    /// <summary>A repo that never had a colour writes no key, so an untouched config.json stays clean.</summary>
    [Fact]
    public void ARepoWithNoColour_AddsNoKeyWhenTheConfigIsSaved()
    {
        File.WriteAllText(_paths.ConfigFile, "{\"repos\":[{\"name\":\"alpha\",\"path\":\"/tmp/a\"}]}");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);
        OrchestratorConfig_Loader.Save(config, _paths);

        Assert.DoesNotContain("topicColor", File.ReadAllText(_paths.ConfigFile));
    }

    /// <summary>And a repo that HAS one keeps it across a save through the app's own model.</summary>
    [Fact]
    public void AColourSurvivesASaveThroughTheAppsOwnModel()
    {
        File.WriteAllText(_paths.ConfigFile,
            $"{{\"repos\":[{{\"name\":\"alpha\",\"path\":\"/tmp/a\",\"topicColor\":{TopicColor_Rotation.PALETTE[4]}}}]}}");

        OrchestratorConfig_Loader.Save(OrchestratorConfig_Loader.Load_OrEmpty(_paths), _paths);

        Assert.Equal(TopicColor_Rotation.PALETTE[4], OrchestratorConfig_Loader.Load_OrEmpty(_paths).Repos[0].TopicColor);
    }
}
