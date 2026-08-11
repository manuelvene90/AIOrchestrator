using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// The owner asks the concierge for a session from their phone; that is the route they actually use.
/// Start_BasicOrchestration existed and was wired to a UI button, but the request protocol had no way
/// to reach it — so the concierge could only ever start a FULL crew, and a basic session was
/// unreachable from the one place it was needed. The owner called that critical.
/// </summary>
public class StartOrchestrationModeTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public StartOrchestrationModeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-startmode-tests-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// Every request written before this field existed must keep working, and there are live
    /// orchestrations whose installed role commands will not mention it until the app restarts.
    /// </summary>
    [Fact]
    public void NoMode_IsAFullCrew()
    {
        Write("a.json", """{"action":"start-orchestration","repo":"skeleton client"}""");

        var request = Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).StartRequests);

        Assert.Equal("skeleton client", request.RepoQuery);
        Assert.False(request.IsBasic);
    }

    [Fact]
    public void ModeBasic_AsksForTheSoloShape()
    {
        Write("a.json", """{"action":"start-orchestration","repo":"skeleton client","mode":"basic"}""");

        Assert.True(Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).StartRequests).IsBasic);
    }

    [Fact]
    public void ModeFull_IsTheSameAsOmittingIt()
    {
        Write("a.json", """{"action":"start-orchestration","repo":"skeleton client","mode":"full"}""");

        Assert.False(Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).StartRequests).IsBasic);
    }

    /// <summary>Hand-written JSON, so spacing and case are the agent's to get wrong.</summary>
    [Theory]
    [InlineData("BASIC")]
    [InlineData(" basic ")]
    [InlineData("Basic")]
    public void ModeIsReadCaseAndSpaceInsensitively(string mode)
    {
        Write("a.json", $$"""{"action":"start-orchestration","repo":"skeleton client","mode":"{{mode}}"}""");

        Assert.True(Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).StartRequests).IsBasic);
    }

    /// <summary>
    /// THE one that matters most. A typo must not quietly hand the owner the expensive shape when
    /// they asked for the cheap one — silently defaulting would spend their tokens on a crew they
    /// did not ask for and never tell them why.
    /// </summary>
    [Theory]
    [InlineData("bsaic")]
    [InlineData("solo")]
    [InlineData("simple")]
    public void AnUnrecognisedMode_IsRejected_NotDefaulted(string mode)
    {
        Write("a.json", $$"""{"action":"start-orchestration","repo":"skeleton client","mode":"{{mode}}"}""");

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Empty(pending.StartRequests);
        Assert.Contains("mode must be", Assert.Single(pending.MalformedRequests).Reason);
    }

    void Write(string fileName, string json)
    {
        File.WriteAllText(Path.Combine(_paths.RequestsFolder, fileName), json);
    }
}
