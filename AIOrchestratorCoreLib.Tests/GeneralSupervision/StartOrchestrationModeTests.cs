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
    /// NO MODE MEANS "THE REQUEST DID NOT SAY" — and that is all it means here now.
    ///
    /// <para>
    /// It used to mean BASIC, collapsed at this line, per the owner's directive of 2026-08-13: "as a
    /// cost-saving measure, a new session should be Basic by default, not an orchestration session".
    /// Basic is still what an untouched machine gets, but the rule moved to <c>config.json</c>
    /// (<c>defaults.orchestrationMode</c>) so the owner who set it can change it — and a default can
    /// only mean anything if the silence it settles is still legible when it reaches the thing that
    /// holds it. Collapsing here made "absent" and "explicitly basic" one value, and the setting
    /// unreachable.
    /// </para>
    /// <para>
    /// Where the null becomes a shape is <c>BridgeEngineModel.Process_StartRequests</c>, and what it
    /// becomes there is pinned by <c>StartOrchestrationDefaultShapeTests</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void NoMode_LeavesTheShapeToTheConfiguredDefault()
    {
        Write("a.json", """{"action":"start-orchestration","repo":"skeleton client"}""");

        var request = Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).StartRequests);

        Assert.Equal("skeleton client", request.RepoQuery);
        Assert.Null(request.IsBasic);
    }

    [Fact]
    public void ModeBasic_AsksForTheSoloShape()
    {
        Write("a.json", """{"action":"start-orchestration","repo":"skeleton client","mode":"basic"}""");

        Assert.True(Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).StartRequests).IsBasic);
    }

    /// <summary>
    /// A CREW IS NOW THE EXPLICIT ONE, and this is the case that pins the flip from the expensive
    /// side: `mode:"full"` is the ONLY way to get a supervisor and an implementer. Asserted apart
    /// from the omitted-mode case above so neither can pass for the other's reason — before the flip
    /// the two were the same assertion, which is why one test could cover both.
    /// </summary>
    [Fact]
    public void ModeFull_IsTheOnlyWayToAskForACrew()
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
