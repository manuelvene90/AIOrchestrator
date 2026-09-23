using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// The request a SOLO drops to start a SIBLING solo for a parallel job (spec 2026-09-23 §4.1, §4.2 —
/// the reader half). The reader checks JSON only: required fields, types, <c>handover</c> a positive
/// integer, <c>job</c> length, the <c>name</c> shape. Everything about the world — the requester's
/// shape, the cap, the handover entry, the worktree — is the executor's, refused with its own reason.
/// </summary>
public class SpawnSiblingRequestTests : IDisposable
{
    /// <summary>
    /// THE NAME HAS TWO WORDS AFTER THE CODE, not the spec example's one ("AI-Orch · limits"): pre-flight
    /// ruling F binds §4.1's own rule, <c>&lt;code&gt; · &lt;2-4 words&gt;</c>, over its example.
    /// </summary>
    const string WELL_FORMED = """
        {"action":"spawn-sibling","orchId":"ai-orchestrator-7","name":"AI-Orch · limits rework",
         "job":"Rework the usage-limit pause so a restored pause can be lifted per window",
         "handover":14,"worktree":"C:/Users/x/repo.worktrees/limits",
         "reason":"two jobs the owner wants to steer separately; disjoint files"}
        """;

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public SpawnSiblingRequestTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-spawn-sibling-tests-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    void Write(string fileName, string json)
    {
        File.WriteAllText(Path.Combine(_paths.RequestsFolder, fileName), json);
    }

    void Write_WellFormedWith(string field, JsonNode? value)
    {
        var root = JsonNode.Parse(WELL_FORMED)!.AsObject();
        root[field] = value;
        Write("a.json", root.ToJsonString());
    }

    string Single_Rejection()
    {
        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Empty(pending.SpawnSiblingRequests);

        return Assert.Single(pending.MalformedRequests).Reason;
    }

    [Fact]
    public void AWellFormedRequest_IsRead()
    {
        Write("a.json", WELL_FORMED);

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Empty(pending.MalformedRequests);
        var request = Assert.Single(pending.SpawnSiblingRequests);
        Assert.Equal("ai-orchestrator-7", request.OrchId);
        Assert.Equal("AI-Orch · limits rework", request.Name);
        Assert.Equal("Rework the usage-limit pause so a restored pause can be lifted per window", request.Job);
        Assert.Equal(14, request.HandoverIndex);
        Assert.Equal("C:/Users/x/repo.worktrees/limits", request.WorktreePath);
        Assert.Equal("two jobs the owner wants to steer separately; disjoint files", request.Reason);
        Assert.Equal(Path.Combine(_paths.RequestsFolder, "a.json"), request.SourceFilePath);
    }

    [Theory]
    [InlineData("orchId")]
    [InlineData("name")]
    [InlineData("job")]
    [InlineData("handover")]
    [InlineData("worktree")]
    [InlineData("reason")]
    public void EachMissingField_IsRejectedWithItsName(string field)
    {
        var root = JsonNode.Parse(WELL_FORMED)!.AsObject();
        root.Remove(field);
        Write("a.json", root.ToJsonString());

        var reason = Single_Rejection();

        if (field == "reason")
            Assert.Equal(OrchestrationRequests_Reader.MISSING_REASON_MESSAGE, reason);
        else
            Assert.Contains($"'{field}'", reason);
    }

    /// <summary>
    /// A STRING "14" IS REFUSED, NOT COERCED. The index is the one number an agent does not guess — the
    /// helper allocated it inside the lock (decision 12) — so a request that wrote it as anything but a
    /// positive whole number did not copy it from the helper's output.
    /// </summary>
    [Theory]
    [InlineData("\"14\"")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("14.5")]
    public void AHandoverThatIsNotAPositiveInteger_IsRejected(string handoverJson)
    {
        Write_WellFormedWith("handover", JsonNode.Parse(handoverJson));

        Assert.Contains("'handover'", Single_Rejection());
    }

    [Fact]
    public void ANameWithoutTheMiddleDot_IsRejected()
    {
        Write_WellFormedWith("name", "limits rework");

        Assert.Contains("'name'", Single_Rejection());
    }

    /// <summary>
    /// <c>.siblings</c> is TAB-SEPARATED AND READ BY BASH, one sibling per line, with the display name as
    /// its last field. A tab in a name shifts the fields; a newline splits one sibling into two lines.
    /// </summary>
    [Theory]
    [InlineData("AI-Orch · limits\trework")]
    [InlineData("AI-Orch · limits\nrework")]
    [InlineData("AI-Orch · limits\rrework")]
    public void ANameWithATabOrANewline_IsRejected(string name)
    {
        Write_WellFormedWith("name", name);

        Assert.Contains("'name'", Single_Rejection());
    }

    [Fact]
    public void ANameWithMoreThanFourWordsAfterTheCode_IsRejected()
    {
        Write_WellFormedWith("name", "AI-Orch · rework the usage limit pause");

        Assert.Contains("'name'", Single_Rejection());
    }

    /// <summary>
    /// RULING F (pre-flight, 2026-09-23): 2-4 words after the code, as spec §4.1 says. The plan allowed one
    /// on the claim that solo/SKILL.md's examples include one-word names; they do not (every example has
    /// two or three words).
    /// </summary>
    [Fact]
    public void ANameWithOneWordAfterTheCode_IsRejected()
    {
        Write_WellFormedWith("name", "AI-Orch · limits");

        Assert.Contains("'name'", Single_Rejection());
    }

    [Theory]
    [InlineData("AI-Orch · limits rework")]
    [InlineData("IS · portfolio picker")]
    [InlineData("SK-C · one two three four")]
    public void ALegalName_HasNoRefusal(string name)
    {
        Assert.Null(SiblingName_Rules.Describe_Refusal_OrNull(name));
    }

    [Theory]
    [InlineData("AI-Orch · limits · rework")]
    [InlineData("A-very-long-code · limits rework")]
    [InlineData(" · limits rework")]
    [InlineData("AI-Orch · abcdefghijklmnopqrstuvwxyz abcdefghijklmnopqrstuvwxyz abcdefgh")]
    public void AnIllegalName_IsRefusedWithASentence(string name)
    {
        var refusal = SiblingName_Rules.Describe_Refusal_OrNull(name);

        Assert.False(string.IsNullOrWhiteSpace(refusal));
    }

    [Fact]
    public void AJobOver200Characters_IsRejected()
    {
        Write_WellFormedWith("job", new string('x', OrchestrationRequests_Reader.SIBLING_JOB_MAX_CHARS + 1));

        Assert.Contains("'job'", Single_Rejection());
    }

    [Fact]
    public void AJobOfExactly200Characters_IsRead()
    {
        Write_WellFormedWith("job", new string('x', OrchestrationRequests_Reader.SIBLING_JOB_MAX_CHARS));

        Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).SpawnSiblingRequests);
    }

    /// <summary>
    /// "ONE LINE" (spec §4.1): the job becomes the child's first FROM owner entry and the prompt's line on
    /// the owner's phone, so a newline would be a second paragraph nobody asked for.
    /// </summary>
    [Fact]
    public void AJobWithANewline_IsRejected()
    {
        Write_WellFormedWith("job", "Rework the pause\nand the receipts");

        Assert.Contains("'job'", Single_Rejection());
    }

    /// <summary>
    /// A request parked outside the scanned folder while it waits for the owner's tap is re-read through
    /// the SAME parse, so it can never be honoured on terms the scanner would have refused.
    /// </summary>
    [Fact]
    public void AParkedFile_IsReReadByPath()
    {
        var parkedFolder = Path.Combine(_tempRoot, "parked");
        Directory.CreateDirectory(parkedFolder);
        var parked = Path.Combine(parkedFolder, "sibling-ai-orchestrator-7-20260923.json");
        File.WriteAllText(parked, WELL_FORMED);

        var request = OrchestrationRequests_Reader.Read_SpawnSiblingRequest_OrNull(parked);

        Assert.NotNull(request);
        Assert.Equal("ai-orchestrator-7", request.OrchId);
        Assert.Equal(14, request.HandoverIndex);
        Assert.Equal(parked, request.SourceFilePath);
    }

    [Fact]
    public void AParkedFileThatNoLongerParses_ReReadsAsNull()
    {
        var parked = Path.Combine(_tempRoot, "parked.json");
        File.WriteAllText(parked, """{"action":"spawn-sibling","orchId":"ai-orchestrator-7"}""");

        Assert.Null(OrchestrationRequests_Reader.Read_SpawnSiblingRequest_OrNull(parked));
    }

    /// <summary>
    /// §4.1: NO MODEL AND NO EFFORT, on purpose. The child copies the parent's overrides — the owner's
    /// dial on this endeavour — so a field naming one is ignored rather than honoured, and it is not
    /// refused either: a harmless extra key must not cost the owner a re-drop.
    /// </summary>
    [Fact]
    public void AModelOrEffortField_IsIgnored_NotHonoured()
    {
        var root = JsonNode.Parse(WELL_FORMED)!.AsObject();
        root["model"] = "fable";
        root["effort"] = "max";
        Write("a.json", root.ToJsonString());

        var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

        Assert.Empty(pending.MalformedRequests);
        var request = Assert.Single(pending.SpawnSiblingRequests);
        Assert.DoesNotContain(typeof(ISpawnSiblingRequest).GetProperties(), property => property.Name.Contains("Model") || property.Name.Contains("Effort"));
        Assert.Equal("ai-orchestrator-7", request.OrchId);
    }
}
