using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Sessions;
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
        Write_Request("b.json", """{"action":"add-implementer","orchId":"skel-work","reason":"adversarial review of the drift guard"}""");
        Write_Request("c.json", """{"action":"close-implementer","orchId":"skel-work","memberId":"imp-2","reason":"task finished and verified"}""");
        Write_Request("d.json", """{"action":"close-orchestration","orchId":"old-orch","requester":"the owner, from the app"}""");

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        var start = Assert.Single(pending.StartRequests);
        Assert.Equal("skeleton client", start.RepoQuery);

        var add = Assert.Single(pending.AddImplementerRequests);
        Assert.Equal("skel-work", add.OrchId);
        Assert.Equal("adversarial review of the drift guard", add.Reason);

        var closeImp = Assert.Single(pending.CloseImplementerRequests);
        Assert.Equal("imp-2", closeImp.MemberId);
        Assert.Equal("task finished and verified", closeImp.Reason);

        // The owner's own UI close carries no reason — only AGENT actions must justify themselves.
        var closeOrch = Assert.Single(pending.CloseOrchestrationRequests);
        Assert.Equal("old-orch", closeOrch.OrchId);
        Assert.Equal("work concluded", closeOrch.Reason);
        Assert.Equal("the owner, from the app", closeOrch.Requester);

        Assert.Empty(pending.MalformedRequests);
    }

    /// <summary>
    /// The reason 'requester' does not default the way 'reason' does: on 2026-08-11
    /// 'ai-orchestrator-1' closed and nothing on disk could name who asked, because the request file
    /// is deleted on execution and carried no attribution. A default would rebuild that hole.
    /// </summary>
    [Fact]
    public void Read_Pending_CloseOrchestrationWithoutARequester_IsRejected()
    {
        Write_Request("a.json", """{"action":"close-orchestration","orchId":"old-orch","reason":"work is done"}""");

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Empty(pending.CloseOrchestrationRequests);
        Assert.Contains("missing 'requester'", Assert.Single(pending.MalformedRequests).Reason);
    }

    /// <summary>
    /// add-reviewer shares the add-implementer pipeline but MUST carry its kind through — a
    /// reviewer spawned as an implementer would come up able to edit and commit.
    /// </summary>
    [Fact]
    public void Read_Pending_AddReviewer_CarriesTheReviewerKind()
    {
        Write_Request("imp.json", """{"action":"add-implementer","orchId":"skel-work","reason":"second pair of hands"}""");
        Write_Request("rev.json", """{"action":"add-reviewer","orchId":"skel-work","reason":"deep adversarial review of the order path"}""");

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Equal(2, pending.AddImplementerRequests.Count);
        Assert.Equal(MemberKinds.Implementer, pending.AddImplementerRequests.Single(r => r.Reason.Contains("hands")).Kind);
        Assert.Equal(MemberKinds.Reviewer, pending.AddImplementerRequests.Single(r => r.Reason.Contains("review")).Kind);
        Assert.Empty(pending.MalformedRequests);
    }

    [Fact]
    public void Read_Pending_AddReviewerWithoutAReason_IsRejectedToo()
    {
        Write_Request("no-reason-rev.json", """{"action":"add-reviewer","orchId":"skel-work"}""");

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Empty(pending.AddImplementerRequests);
        Assert.Contains("missing 'reason'", Assert.Single(pending.MalformedRequests).Reason);
    }

    [Fact]
    public void Read_Pending_SpawningOrRetiringWithoutAReason_IsRejected_TheOwnerMustNeverBeInTheDark()
    {
        Write_Request("no-reason-add.json", """{"action":"add-implementer","orchId":"skel-work"}""");
        Write_Request("no-reason-close.json", """{"action":"close-implementer","orchId":"skel-work","memberId":"imp-2"}""");

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Empty(pending.AddImplementerRequests);
        Assert.Empty(pending.CloseImplementerRequests);
        Assert.Equal(2, pending.MalformedRequests.Count);

        foreach (var malformed in pending.MalformedRequests)
        {
            Assert.Contains("missing 'reason'", malformed.Reason);

            // The orchId is carried so the rejection can be reported in the agent's own channel.
            Assert.Equal("skel-work", malformed.OrchId);
        }
    }

    [Fact]
    public void Read_Pending_MalformedFiles_ReportedWithReasonsNotThrown()
    {
        Write_Request("bad1.json", "not json at all");
        Write_Request("bad2.json", """{"action":"start-orchestration"}""");
        Write_Request("bad3.json", """{"action":"start-orchestration-retry","orchId":"x","repo":"crm"}""");
        Write_Request("good.json", """{"action":"add-implementer","orchId":"x","reason":"second pair of eyes on the failing test"}""");

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
