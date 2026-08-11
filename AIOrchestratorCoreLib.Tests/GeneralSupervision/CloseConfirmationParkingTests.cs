using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// The close guard, from the side that has to hold: an orchestration must not close because nobody
/// answered. On 2026-08-11 'ai-orchestrator-1' was closed within ~2 s of a supervisor inferring it
/// from an ambiguous sentence, so the owner now confirms every close with a tap — and a request that
/// never gets one must sit there doing nothing, indefinitely, without being re-executed by the tick
/// that re-reads the folder every two seconds.
/// </summary>
public class CloseConfirmationParkingTests : IDisposable
{
    const string CLOSE_REQUEST =
        """{"action":"close-orchestration","orchId":"crm-2","reason":"work is done","requester":"supervisor of crm-2"}""";

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public CloseConfirmationParkingTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-closeguard-tests-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// THE guarantee. A parked request is invisible to the scanner, so no tick can execute it — and
    /// this is the mechanism, not a flag: the reader enumerates one level of *.json, so moving the
    /// file into a subfolder is what makes "no tap, no close" true even across a restart.
    /// </summary>
    [Fact]
    public void AParkedRequest_IsNeverReturnedToTheExecutor_HoweverOftenItIsScanned()
    {
        var requestPath = Write_Request("close.json", CLOSE_REQUEST);

        Assert.Single(OrchestrationRequests_Reader.Read_Pending(_paths).CloseOrchestrationRequests);

        CloseConfirmation_Parking.Park(_paths, requestPath);

        // Re-scanned the way the 2-second tick does. It must stay empty every time.
        for (var scan = 0; scan < 3; scan++)
        {
            var pending = OrchestrationRequests_Reader.Read_Pending(_paths);

            Assert.Empty(pending.CloseOrchestrationRequests);
            Assert.Empty(pending.MalformedRequests);
        }

        Assert.False(File.Exists(requestPath));
        Assert.Single(CloseConfirmation_Parking.Find_Parked(_paths));
    }

    /// <summary>The parked file stays the record of what was asked — that is what the tap confirms.</summary>
    [Fact]
    public void AParkedRequest_CanStillBeReadBack_WithWhoAskedAndWhy()
    {
        var parkedPath = CloseConfirmation_Parking.Park(_paths, Write_Request("close.json", CLOSE_REQUEST));

        var request = OrchestrationRequests_Reader.Read_CloseOrchestrationRequest_OrNull(parkedPath);

        Assert.NotNull(request);
        Assert.Equal("crm-2", request.OrchId);
        Assert.Equal("work is done", request.Reason);
        Assert.Equal("supervisor of crm-2", request.Requester);
    }

    /// <summary>
    /// Parity with the scanner: a parked file is re-read through the SAME parse, so it can never be
    /// honoured on terms the scanner would have rejected.
    /// </summary>
    [Fact]
    public void AParkedRequestMissingItsRequester_IsNotHonoured()
    {
        var parkedPath = CloseConfirmation_Parking.Park(
            _paths,
            Write_Request("close.json", """{"action":"close-orchestration","orchId":"crm-2"}"""));

        Assert.Null(OrchestrationRequests_Reader.Read_CloseOrchestrationRequest_OrNull(parkedPath));
    }

    /// <summary>
    /// A destructive confirmation must not outlive the situation that produced it — a close asked
    /// for last night must never execute on a stray tap tomorrow.
    /// </summary>
    [Fact]
    public void AnUnansweredRequest_ExpiresOnlyAfterTheFullWindow()
    {
        var parkedPath = CloseConfirmation_Parking.Park(_paths, Write_Request("close.json", CLOSE_REQUEST));

        var asked = File.GetLastWriteTimeUtc(parkedPath);

        Assert.False(CloseConfirmation_Parking.Is_Expired(parkedPath, asked));
        Assert.False(CloseConfirmation_Parking.Is_Expired(parkedPath, asked.AddHours(CloseConfirmation_Parking.EXPIRY_HOURS - 1)));
        Assert.True(CloseConfirmation_Parking.Is_Expired(parkedPath, asked.AddHours(CloseConfirmation_Parking.EXPIRY_HOURS)));
    }

    /// <summary>A file we cannot stat is not a licence to close an orchestration.</summary>
    [Fact]
    public void AMissingParkedFile_NeverCountsAsExpired()
    {
        Assert.False(CloseConfirmation_Parking.Is_Expired(Path.Combine(_tempRoot, "gone.json"), DateTime.UtcNow));
    }

    /// <summary>
    /// The audit record the 2026-08-11 close never left: until now every processed request was
    /// deleted outright, so "who asked, and what was decided" was unanswerable minutes later.
    /// </summary>
    [Theory]
    [InlineData("closed")]
    [InlineData("declined")]
    [InlineData("expired")]
    public void AResolvedRequest_IsKeptUnderWhatWasDecided_AndStaysOutOfTheScanner(string outcome)
    {
        var parkedPath = CloseConfirmation_Parking.Park(_paths, Write_Request("close.json", CLOSE_REQUEST));

        CloseConfirmation_Parking.Archive(_paths, parkedPath, outcome);

        var archived = Assert.Single(Directory.EnumerateFiles(CloseConfirmation_Parking.Get_ResolvedFolder(_paths)));

        Assert.StartsWith(outcome, Path.GetFileName(archived));
        Assert.Equal(CLOSE_REQUEST, File.ReadAllText(archived));

        Assert.Empty(CloseConfirmation_Parking.Find_Parked(_paths));
        Assert.Empty(OrchestrationRequests_Reader.Read_Pending(_paths).CloseOrchestrationRequests);
    }

    /// <summary>
    /// NOTHING in a request can claim the owner already agreed. An earlier version of this guard
    /// had an `ownerConfirmed` flag so the app's own UI closes would not be prompted twice, and it
    /// was removed: nobody would have had to forge it in bad faith — a role command written from
    /// this JSON, or an agent copying a shape out of an archived request, and "no tap, no close"
    /// stops being true while everyone behaves reasonably.
    ///
    /// The owner's own closes now bypass this protocol entirely (IBridgeEngine.Close_Orchestration_ByOwner),
    /// so a request that still carries the old field is just an ordinary agent request that will be
    /// held like any other.
    /// </summary>
    [Fact]
    public void ARequestClaimingTheOwnerAlreadyAgreed_IsTreatedNoDifferentlyFromAnyOther()
    {
        Write_Request("claims.json", """{"action":"close-orchestration","orchId":"crm-2","reason":"work is done","requester":"supervisor of crm-2","ownerConfirmed":true}""");
        Write_Request("plain.json", CLOSE_REQUEST.Replace("crm-2", "crm-3"));

        var requests = OrchestrationRequests_Reader.Read_Pending(_paths).CloseOrchestrationRequests;

        Assert.Equal(2, requests.Count);

        // Same shape, same fields, no trace of the claim — there is nowhere for it to be honoured.
        foreach (var request in requests)
        {
            Assert.Equal("work is done", request.Reason);
            Assert.StartsWith("supervisor of ", request.Requester);
        }
    }

    string Write_Request(string fileName, string json)
    {
        var path = Path.Combine(_paths.RequestsFolder, fileName);
        File.WriteAllText(path, json);

        return path;
    }
}
