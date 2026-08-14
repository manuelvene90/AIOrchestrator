using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// The request a SOLO session drops to ask for its basic orchestration to become a full crew.
///
/// It is the one request a session makes about its own existence, and it is the largest spend
/// increase in the system — a supervisor and an implementer, indefinitely, replacing one session.
/// Every other spend increase here either carries a reason to the owner or waits for their tap; this
/// one does both, and is effectively one-way.
/// </summary>
public class PromoteOrchestrationRequestTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public PromoteOrchestrationRequestTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-promote-tests-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void AWellFormedRequestIsRead()
    {
        Write("a.json", """{"action":"promote-orchestration","orchId":"crm-4","reason":"three subsystems and a review gate — one session is not enough"}""");

        var request = Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).PromoteOrchestrationRequests);

        Assert.Equal("crm-4", request.OrchId);
        Assert.Equal("three subsystems and a review gate — one session is not enough", request.Reason);
    }

    /// <summary>
    /// THE REASON IS MANDATORY, exactly as it is for add-implementer and the closes. The owner is
    /// about to be asked to approve spending; they are told what on, in the same message.
    /// </summary>
    [Fact]
    public void ARequestWithNoReasonIsRejectedWithThatReason()
    {
        Write("a.json", """{"action":"promote-orchestration","orchId":"crm-4"}""");

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Empty(pending.PromoteOrchestrationRequests);
        Assert.Contains("missing 'reason'", Assert.Single(pending.MalformedRequests).Reason);
    }

    [Fact]
    public void ARequestWithNoOrchIdIsRejectedWithThatReason()
    {
        Write("a.json", """{"action":"promote-orchestration","reason":"it outgrew one session"}""");

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Empty(pending.PromoteOrchestrationRequests);
        Assert.Equal("missing 'orchId'", Assert.Single(pending.MalformedRequests).Reason);
    }

    /// <summary>
    /// THE PARSER JUDGES THE JSON AND NOTHING ELSE, and that separation is deliberate.
    ///
    /// Whether the orchestration is basic, and whether its solo has filed a HANDOVER entry, are facts
    /// about the world that the executor reads and refuses on with its own reason. A parser that
    /// answered "unanalysable" to a perfectly analysable line — because something it cannot see is not
    /// true yet — would wear the safe posture of decision 21 without having it: the defect is not the
    /// posture, it is mislabelling an ordinary line as unevaluable.
    ///
    /// So a syntactically good request for an orchestration that does not exist parses CLEANLY here.
    /// </summary>
    [Fact]
    public void AnOrchestrationThatDoesNotExistIsStillAWellFormedRequest()
    {
        Write("a.json", """{"action":"promote-orchestration","orchId":"nothing-like-this-1","reason":"it outgrew one session"}""");

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Single(pending.PromoteOrchestrationRequests);
        Assert.Empty(pending.MalformedRequests);
    }

    /// <summary>
    /// The action string is exact. Retries must REUSE it rather than inventing a variant — the rule
    /// every action in this reader follows, and the reason unrecognised actions are rejected loudly.
    /// </summary>
    [Theory]
    [InlineData("promote")]
    [InlineData("promote-orchestration-retry")]
    [InlineData("upgrade-orchestration")]
    public void AVariantActionIsNotHonoured(string action)
    {
        Write("a.json", $$"""{"action":"{{action}}","orchId":"crm-4","reason":"it outgrew one session"}""");

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Empty(pending.PromoteOrchestrationRequests);
        Assert.Single(pending.MalformedRequests);
    }

    void Write(string fileName, string json)
    {
        File.WriteAllText(Path.Combine(_paths.RequestsFolder, fileName), json);
    }
}
