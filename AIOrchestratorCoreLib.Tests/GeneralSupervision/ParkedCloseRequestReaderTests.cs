using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// The parked folder now holds two kinds of close, and everything downstream of it used to assume
/// one.
///
/// WHY THIS READER EXISTS, asserted rather than described: every parked path went through
/// <c>Read_CloseOrchestrationRequest_OrNull</c>, which returns NULL for a member close. Parking one
/// without this would have filed a perfectly valid request as "unreadable", told its requester to
/// drop a fresh one, and never asked the owner anything — a guard that loses the request it was
/// added to protect.
/// </summary>
public class ParkedCloseRequestReaderTests : IDisposable
{
    const string MEMBER_CLOSE =
        """{"action":"close-implementer","orchId":"crm-2","memberId":"imp-2","reason":"its task is delivered"}""";

    const string ORCHESTRATION_CLOSE =
        """{"action":"close-orchestration","orchId":"crm-2","reason":"work is done","requester":"supervisor of crm-2"}""";

    readonly string _tempRoot;

    public ParkedCloseRequestReaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-parkedclose-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// THE case. Both halves in one assertion pair, because the second is only interesting given the
    /// first: the old reader cannot see this file at all, and the new one reads it as a MEMBER close.
    /// </summary>
    [Fact]
    public void AMemberCloseIsInvisibleToTheOldReaderAndReadsAsAMemberCloseToThisOne()
    {
        var path = Write_Request(MEMBER_CLOSE);

        Assert.Null(OrchestrationRequests_Reader.Read_CloseOrchestrationRequest_OrNull(path));

        var parked = ParkedCloseRequest_Reader.Read_OrNull(path);

        Assert.NotNull(parked);
        Assert.Equal(ParkedCloseKinds.Implementer, parked.Kind);
        Assert.Equal("imp-2", parked.MemberId);
        Assert.Equal("crm-2", parked.OrchId);
        Assert.Equal("its task is delivered", parked.Reason);
    }

    /// <summary>
    /// And the kind it always handled is unchanged — asserted separately so neither case can pass for
    /// the other's reason. MemberId is null here and that is load-bearing: it is what the executor
    /// branches on.
    /// </summary>
    [Fact]
    public void AnOrchestrationCloseStillReadsAsAnOrchestrationClose()
    {
        var parked = ParkedCloseRequest_Reader.Read_OrNull(Write_Request(ORCHESTRATION_CLOSE));

        Assert.NotNull(parked);
        Assert.Equal(ParkedCloseKinds.Orchestration, parked.Kind);
        Assert.Null(parked.MemberId);
        Assert.Equal("supervisor of crm-2", parked.Requester);
    }

    /// <summary>
    /// A file nobody can read is not authority to end anything, so it reads as null and the caller
    /// files it unexecuted. This is the same answer the orchestration guard already gives.
    /// </summary>
    [Fact]
    public void AnUnreadableFileReadsAsNothingToAct()
    {
        Assert.Null(ParkedCloseRequest_Reader.Read_OrNull(Write_Request("{ not json at all")));
        Assert.Null(ParkedCloseRequest_Reader.Read_OrNull(Path.Combine(_tempRoot, "does-not-exist.json")));
    }

    /// <summary>
    /// THE STRICT PARSE SURVIVES THE DETOUR, which is the property the parked re-read exists to keep:
    /// a request missing the 'reason' every autonomous action owes the owner is rejected by the
    /// scanner, and must not become acceptable merely by having been parked.
    ///
    /// Without this, the guard would be a way to LAUNDER a request the front door refused.
    /// </summary>
    [Fact]
    public void AMemberCloseWithNoReasonIsRejectedEvenAfterParking()
    {
        var path = Write_Request("""{"action":"close-implementer","orchId":"crm-2","memberId":"imp-2"}""");

        Assert.Null(ParkedCloseRequest_Reader.Read_OrNull(path));
    }

    /// <summary>A kind that has no business being parked is not a close, and reads as nothing.</summary>
    [Fact]
    public void ARequestThatIsNotACloseAtAllReadsAsNothing()
    {
        var path = Write_Request("""{"action":"add-implementer","orchId":"crm-2","reason":"more hands"}""");

        Assert.Null(ParkedCloseRequest_Reader.Read_OrNull(path));
    }

    string Write_Request(string json)
    {
        var path = Path.Combine(_tempRoot, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
