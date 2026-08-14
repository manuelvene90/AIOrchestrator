using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// THE NUDGE ACTUALLY FIRES — the assertion no unit test can make, because the decision is a pure
/// function and the CLOCK that feeds it is chosen at the call site.
///
/// `Measure_QuietFor` is pinned four ways in the unit tests and every one of them passes while the
/// engine hands it the wrong clock. On this machine `DateTime.UtcNow` against local stamps makes
/// `quietFor` NEGATIVE, so nothing ever reaches the nudge threshold and EVERY alarm in the system
/// goes quiet — with a green suite, because a defect that silences an alarm is invisible by
/// construction: nothing complains, since the complaining is what stopped.
///
/// This is the fourth appearance of one shape in a day — pin the CALL, not just the callee — and the
/// third time it hid in a seam built to make something testable.
///
/// WHAT IT CANNOT SEE, stated rather than left to be discovered: on a machine where local time IS
/// UTC, the mismatch has no effect and this test passes either way. It is not vacuous there — "a
/// dormant member gets nudged" is worth asserting on its own terms — but it stops being a guard
/// against the clock specifically. Verified on UTC+2.
/// </summary>
public class NudgeClockProbeTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public NudgeClockProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-nudgeclock-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        var store = OrchestrationSessionStore_Factory.Create(_paths);
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, store, new RecordingSpawner_Fake(), log);
        _engine = BridgeEngine_Factory.Create(_paths, configProvider, store, _launcher, log);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// A member that announced a writing window twenty minutes ago and went silent is stalled: its
    /// monitor fires only when someone else writes, so it cannot give itself the next turn. The app
    /// waking it is the load-bearing alarm of the whole system.
    ///
    /// The stamp and the file are both put twenty minutes in the past, so the quiet clock reads
    /// "quiet" by either route — this case is about the nudge REACHING the channel, not about which
    /// source the duration came from. That distinction is what the unit tests pin.
    /// </summary>
    [Fact]
    public async Task ADormantMemberIsActuallyNudged()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var memberId = session.Members[0].MemberId;
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, memberId);

        var stamp = DateTime.Now.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm");

        File.WriteAllText(
            channelFile,
            $"## [1] FROM supervisor — {stamp} — brief\nimplement the parser\n\n"
            + $"## [2] FROM implementer — {stamp} — WRITING WINDOW OPEN — Parser.cs\nstarting now\n");

        File.SetLastWriteTime(channelFile, DateTime.Now.AddMinutes(-20));

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => Has_AppEntry(channelFile)),
            "a member dormant for 20 minutes was never nudged — the alarm the whole system rests on did not fire");
    }

    bool Has_AppEntry(string channelFile)
    {
        return ChannelEntry_Parser
            .Parse_All(File.ReadAllText(channelFile))
            .Any(entry => entry.Author == ChannelAuthors.App);
    }

    async Task Tick_Once_Async()
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way this loop ends.
        }
    }

    static bool Wait_Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (condition())
                return true;

            Thread.Sleep(50);
        }

        return false;
    }
}
