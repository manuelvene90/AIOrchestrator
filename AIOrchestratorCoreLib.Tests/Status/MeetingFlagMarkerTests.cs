using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

public class MeetingFlagMarkerTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public MeetingFlagMarkerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-meeting-flag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.Get_OrchestrationFolder("arb-fix"));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Terminal_WritesTheFlagWhereABashLoopCanTestForIt()
    {
        MeetingFlag_Marker.Sync(_paths, "arb-fix", OwnerPresenceModes.Terminal);

        // The watcher is a shell loop testing `[ -f "$sup/.meeting" ]`, so the NAME and the LOCATION
        // are the contract — not an implementation detail this test may paraphrase.
        Assert.True(File.Exists(Path.Combine(_paths.Get_OrchestrationFolder("arb-fix"), ".meeting")));
        Assert.True(MeetingFlag_Marker.Is_InMeeting(_paths, "arb-fix"));
    }

    [Fact]
    public void Remote_RemovesIt_SoTheWatcherComesBack()
    {
        MeetingFlag_Marker.Sync(_paths, "arb-fix", OwnerPresenceModes.Terminal);
        MeetingFlag_Marker.Sync(_paths, "arb-fix", OwnerPresenceModes.Remote);

        Assert.False(MeetingFlag_Marker.Is_InMeeting(_paths, "arb-fix"));
    }

    [Fact]
    public void AFlagLeftBehindByADeadApp_IsClearedByTheNextSyncThatDisagreesWithIt()
    {
        // The app died mid-meeting: the file is on disk with nobody to remove it, and a file that can
        // silence a watcher is a file that can make an orchestration permanently deaf.
        File.WriteAllText(Path.Combine(_paths.Get_OrchestrationFolder("arb-fix"), ".meeting"), "stale");

        var changed = MeetingFlag_Marker.Sync(_paths, "arb-fix", OwnerPresenceModes.Remote);

        Assert.True(changed);
        Assert.False(MeetingFlag_Marker.Is_InMeeting(_paths, "arb-fix"));
    }

    [Fact]
    public void Sync_ReportsOnlyREALTransitions_SoATickDoesNotLogAThousandNoOps()
    {
        Assert.True(MeetingFlag_Marker.Sync(_paths, "arb-fix", OwnerPresenceModes.Terminal));
        Assert.False(MeetingFlag_Marker.Sync(_paths, "arb-fix", OwnerPresenceModes.Terminal));

        Assert.True(MeetingFlag_Marker.Sync(_paths, "arb-fix", OwnerPresenceModes.Remote));
        Assert.False(MeetingFlag_Marker.Sync(_paths, "arb-fix", OwnerPresenceModes.Remote));
    }

    [Fact]
    public void General_GetsItsFlagInItsOwnFolder_BecauseItHasNoSessionToCarryPresence()
    {
        Directory.CreateDirectory(_paths.GeneralFolder);

        MeetingFlag_Marker.Sync(_paths, "general", OwnerPresenceModes.Terminal);

        Assert.True(File.Exists(Path.Combine(_paths.GeneralFolder, ".meeting")));
        Assert.True(MeetingFlag_Marker.Is_InMeeting(_paths, "general"));
    }
}
