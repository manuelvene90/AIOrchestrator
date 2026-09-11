using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// THE REQUEST HAD NO ROOM FOR THE WORK. <c>start-orchestration</c> carried a repo and a shape and
/// nothing else, so the one thing the owner actually said — what they wanted done — was dropped
/// between their message and the orchestration that exists to do it.
///
/// <para>
/// It was invisible while the general supervisor could tell the new supervisor afterwards. It cannot:
/// its own protocol makes another orchestration's <c>owner-channel.md</c> READ-ONLY to it. So on
/// 2026-09-06, live from the phone, a full crew came up with a supervisor, an implementer and a
/// reviewer, and the round only moved because the task was appended to that channel BY HAND.
/// </para>
/// <para>
/// The field is what closes it, and it changes no permission: the APP writes the entry, under the
/// author it already uses for everything the owner types.
/// </para>
/// </summary>
public class StartOrchestrationTaskTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public StartOrchestrationTaskTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-starttask-tests-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void TheTaskTravelsWithTheRequest_Verbatim()
    {
        Write("a.json", """{"action":"start-orchestration","repo":"skeleton client","mode":"full","task":"Create and commit HELLO.md"}""");

        var request = Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).StartRequests);

        Assert.Equal("Create and commit HELLO.md", request.Task);
    }

    /// <summary>
    /// A request written before the key existed still starts an orchestration. The compatibility is
    /// worth one line of test because the alternative — rejecting it — would have every general
    /// supervisor mid-turn filing requests the app throws away.
    /// </summary>
    [Fact]
    public void AnAbsentTask_IsNoTask_AndTheRequestIsStillGood()
    {
        Write("a.json", """{"action":"start-orchestration","repo":"skeleton client"}""");

        var request = Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).StartRequests);

        Assert.Null(request.Task);
        Assert.Equal("skeleton client", request.RepoQuery);
    }

    /// <summary>
    /// A BLANK TASK IS NO TASK, decided once in the factory. Otherwise every reader of the field has
    /// its own idea of what <c>"task": "   "</c> means, and the one that guesses "yes there is a task"
    /// files an empty entry into the owner's channel and wakes a session with it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\\n")]
    public void ABlankTask_ReadsAsNoTask(string task)
    {
        Write("a.json", $$"""{"action":"start-orchestration","repo":"skeleton client","task":"{{task}}"}""");

        Assert.Null(Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).StartRequests).Task);
    }

    /// <summary>Surrounding whitespace goes; the words do not. The entry is the owner's, not a reformat of it.</summary>
    [Fact]
    public void TheTaskIsTrimmed_AndNothingElseIsDoneToIt()
    {
        Write("a.json", """{"action":"start-orchestration","repo":"skeleton client","task":"  Fix the parser, then tell me.  "}""");

        Assert.Equal("Fix the parser, then tell me.", Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).StartRequests).Task);
    }

    void Write(string fileName, string json)
    {
        File.WriteAllText(Path.Combine(_paths.RequestsFolder, fileName), json);
    }
}
