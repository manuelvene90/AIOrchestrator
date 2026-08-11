using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// A confirmation BUTTON outlives everything around it. Telegram keeps it on the owner's phone for
/// good, while the request it points at can be archived, declined or expired underneath it — so the
/// button is a claim about the past, and acting on it requires re-checking the present.
///
/// Both of these were HIGH findings against the first close guard. A declined request could come
/// back as a second, unclearable prompt whose tap closed the orchestration the owner had just
/// refused to close, recorded as "Asked by: unrecorded". And a tap never re-checked expiry at all,
/// so a request asked at 21:00 and muted was still tappable — and still closing — thirteen hours
/// later, because Do-Not-Disturb froze the sweep that would have lapsed it.
/// </summary>
public class CloseConfirmationStaleTapTests : IDisposable
{
    const string CLOSE_REQUEST =
        """{"action":"close-orchestration","orchId":"crm-2","reason":"work is done","requester":"supervisor of crm-2"}""";

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public CloseConfirmationStaleTapTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-staletap-tests-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// The state a stale button points at: its file was archived when the decision was carried out.
    /// Nothing can be read back from it, and reading nothing must never authorise a close.
    /// </summary>
    [Fact]
    public void AnArchivedRequest_CanNoLongerBeRead_SoATapHasNothingToActOn()
    {
        var parkedPath = CloseConfirmation_Parking.Park(_paths, Write_Request(CLOSE_REQUEST));

        CloseConfirmation_Parking.Archive(_paths, parkedPath, "declined");

        Assert.False(File.Exists(parkedPath));
        Assert.Null(OrchestrationRequests_Reader.Read_CloseOrchestrationRequest_OrNull(parkedPath));
    }

    /// <summary>
    /// The decline is recorded under what was decided, so a later tap cannot be mistaken for a fresh
    /// decision — the archive is the evidence that the owner already answered.
    /// </summary>
    [Fact]
    public void ADeclinedRequest_IsFiledAsDeclined_AndIsGoneFromTheParkedSet()
    {
        var parkedPath = CloseConfirmation_Parking.Park(_paths, Write_Request(CLOSE_REQUEST));

        CloseConfirmation_Parking.Archive(_paths, parkedPath, "declined");

        var archived = Assert.Single(Directory.EnumerateFiles(CloseConfirmation_Parking.Get_ResolvedFolder(_paths)));

        Assert.StartsWith("declined", Path.GetFileName(archived));
        Assert.Empty(CloseConfirmation_Parking.Find_Parked(_paths));
    }

    /// <summary>
    /// F2's scenario, as the tap now sees it: asked, muted, and tapped the next morning. The request
    /// is still parked — the sweep never ran — so only an expiry check at TAP time refuses it.
    /// </summary>
    [Fact]
    public void ARequestAskedYesterday_ReadsAsExpiredAtTapTime_EvenThoughItIsStillParked()
    {
        var parkedPath = CloseConfirmation_Parking.Park(_paths, Write_Request(CLOSE_REQUEST));

        var askedAt = File.GetLastWriteTimeUtc(parkedPath);
        var tappedAt = askedAt.AddHours(13);

        Assert.True(File.Exists(parkedPath));
        Assert.True(CloseConfirmation_Parking.Is_Expired(parkedPath, tappedAt));
    }

    /// <summary>The same button inside the window is still good — the check must not refuse everything.</summary>
    [Fact]
    public void ATapWellInsideTheWindow_IsNotExpired()
    {
        var parkedPath = CloseConfirmation_Parking.Park(_paths, Write_Request(CLOSE_REQUEST));

        var askedAt = File.GetLastWriteTimeUtc(parkedPath);

        Assert.False(CloseConfirmation_Parking.Is_Expired(parkedPath, askedAt.AddMinutes(30)));
        Assert.NotNull(OrchestrationRequests_Reader.Read_CloseOrchestrationRequest_OrNull(parkedPath));
    }

    /// <summary>
    /// The lapse path files it as expired, so the same button cannot later be honoured as if the
    /// request were merely waiting.
    /// </summary>
    [Fact]
    public void AnExpiredRequest_IsFiledAsExpired_AndLeavesNothingParked()
    {
        var parkedPath = CloseConfirmation_Parking.Park(_paths, Write_Request(CLOSE_REQUEST));

        CloseConfirmation_Parking.Archive(_paths, parkedPath, "expired");

        Assert.StartsWith(
            "expired",
            Path.GetFileName(Assert.Single(Directory.EnumerateFiles(CloseConfirmation_Parking.Get_ResolvedFolder(_paths)))));

        Assert.Empty(CloseConfirmation_Parking.Find_Parked(_paths));
        Assert.Empty(OrchestrationRequests_Reader.Read_Pending(_paths).CloseOrchestrationRequests);
    }

    string Write_Request(string json)
    {
        var path = Path.Combine(_paths.RequestsFolder, $"close-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);

        return path;
    }
}
