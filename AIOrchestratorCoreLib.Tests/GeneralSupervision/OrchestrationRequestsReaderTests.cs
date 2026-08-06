using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

public class OrchestrationRequestsReaderTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public OrchestrationRequestsReaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-requests-tests-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    void Write_Request(string fileName, string json)
    {
        File.WriteAllText(Path.Combine(_paths.RequestsFolder, fileName), json);
    }

    [Fact]
    public void Read_Pending_AllFourActions_ParsedIntoTheirLists()
    {
        Write_Request("a.json", """{"action":"start-orchestration","repo":"skeleton client"}""");
        Write_Request("b.json", """{"action":"add-implementer","orchId":"skel-work"}""");
        Write_Request("c.json", """{"action":"close-implementer","orchId":"skel-work","memberId":"imp-2"}""");
        Write_Request("d.json", """{"action":"close-orchestration","orchId":"old-orch"}""");

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        var start = Assert.Single(pending.StartRequests);
        Assert.Equal("skeleton client", start.RepoQuery);

        var add = Assert.Single(pending.AddImplementerRequests);
        Assert.Equal("skel-work", add.OrchId);

        var closeImp = Assert.Single(pending.CloseImplementerRequests);
        Assert.Equal("imp-2", closeImp.MemberId);

        var closeOrch = Assert.Single(pending.CloseOrchestrationRequests);
        Assert.Equal("old-orch", closeOrch.OrchId);

        Assert.Empty(pending.MalformedRequests);
    }

    [Fact]
    public void Read_Pending_MalformedFiles_ReportedWithReasonsNotThrown()
    {
        Write_Request("bad1.json", "not json at all");
        Write_Request("bad2.json", """{"action":"start-orchestration"}""");
        Write_Request("bad3.json", """{"action":"start-orchestration-retry","orchId":"x","repo":"crm"}""");
        Write_Request("good.json", """{"action":"add-implementer","orchId":"x"}""");

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Equal(3, pending.MalformedRequests.Count);
        Assert.Single(pending.AddImplementerRequests);

        // Agents hand-write these files — the reason must name what was wrong (e.g. an invented
        // retry action, the exact live failure that motivated this).
        var unknownAction = pending.MalformedRequests.Single(m => m.FilePath.EndsWith("bad3.json"));
        Assert.Contains("unknown action 'start-orchestration-retry'", unknownAction.Reason);
        Assert.Contains("retries must reuse the SAME action", unknownAction.Reason);

        var missingRepo = pending.MalformedRequests.Single(m => m.FilePath.EndsWith("bad2.json"));
        Assert.Contains("missing 'repo'", missingRepo.Reason);
    }

    [Fact]
    public void Read_Pending_NoRequestsFolder_ReturnsEmpty()
    {
        var emptyPaths = SupervisionPaths_Factory.Create(Path.Combine(_tempRoot, "does-not-exist"));

        var pending = OrchestrationRequests_Reader.Read_Pending(emptyPaths);

        Assert.Empty(pending.StartRequests);
        Assert.Empty(pending.MalformedRequests);
    }
}
