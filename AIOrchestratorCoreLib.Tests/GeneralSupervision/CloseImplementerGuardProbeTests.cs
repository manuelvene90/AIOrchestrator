using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// THE WIRING, which two of us declared unpinnable and a reviewer then pinned.
///
/// The feature is not the reader or the prompt — it is that a close-implementer request PARKS instead
/// of killing a session tree. That decision lives in <c>BridgeEngineModel</c>, which is
/// <c>internal sealed</c> with no <c>InternalsVisibleTo</c>, so it was written off as reachable only
/// through a timing-dependent test of the kind that fails one run in seven and teaches everyone to
/// re-run it. That was an assumption, and nobody measured it.
///
/// EVERY FLAKE REASON ASSUMED HERE TURNS OUT TO BE CHECKABLE, and the reviewer checked them:
/// <c>BridgeEngine_Factory.Create</c> takes interfaces only and the spawner fake already exists, so
/// nothing is spawned; a temp root carries no config, so the Telegram client is null and no network
/// is touched; the tick calls <c>Process_PendingRequests</c> as its FIRST statement before any await,
/// so the parking happens synchronously on this thread; with no Telegram client the ask returns on
/// its fail-closed path, so the parked file STAYS parked — a stable end state with nothing to race;
/// and killing a pid file that does not exist is a no-op.
///
/// It costs nothing in production. The harness is the same shape as the launcher tests.
/// </summary>
public class CloseImplementerGuardProbeTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public CloseImplementerGuardProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-closeprobe-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        _store = OrchestrationSessionStore_Factory.Create(_paths);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);
        _engine = BridgeEngine_Factory.Create(_paths, configProvider, _store, _launcher, log);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// A dropped close-implementer request must leave the member ALIVE and the request waiting for
    /// the owner.
    ///
    /// THE TWO ASSERTIONS ARE SEPARATE AND NAMED, which is the reviewer's own correction to their
    /// first draft. Written as "wait until one file is parked", it also failed when the reader could
    /// not see member closes at all — the file is then archived unreadable and the wait never
    /// succeeds. Two routes to one failure is the exact defect this probe was added alongside.
    ///
    /// So the wait is on the request being CONSUMED, and ClosedUtc is asserted on its own: that one is
    /// specific to executing-versus-parking, and no reader change can reach it.
    /// </summary>
    [Fact]
    public async Task ACloseRequestParksTheAskAndLeavesTheMemberRunning()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var memberId = session.Members[0].MemberId;

        var requestPath = Path.Combine(_paths.RequestsFolder, "close-member.json");
        File.WriteAllText(
            requestPath,
            $$"""{"action":"close-implementer","orchId":"{{session.OrchId}}","memberId":"{{memberId}}","reason":"its task is delivered"}""");

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => !File.Exists(requestPath)),
            "the request file was never consumed — the engine never processed it, so nothing below means anything");

        Assert.True(
            _store.Get_Session(session.OrchId).Members[0].ClosedUtc == null,
            $"'{memberId}' was CLOSED without the owner confirming anything — the guard is not in force");

        // NOT Find_Parked, and this is the same correction one step further on. "One file is parked"
        // ALSO fails when the reader cannot see member closes: the file parks, the ask then reads
        // null and archives it as unreadable, and the count is zero for a reason that has nothing to
        // do with this guard. Two defects, one symptom — which is the thing being fixed everywhere
        // else in this branch.
        //
        // The HELD entry is written by the parking path itself, before anything can undo it, and no
        // other path writes it. It also closes the hole ClosedUtc leaves open on its own: a mutation
        // that simply DROPPED the request would keep the member alive and satisfy the assertion above
        // while losing the close entirely.
        Assert.Contains(
            "HELD",
            File.ReadAllText(_paths.Get_OwnerChannelFile(session.OrchId)));
    }

    /// <summary>
    /// Runs the loop long enough for one tick. The parking happens synchronously before the first
    /// await, so cancelling immediately is not a race — it is what keeps this test at a couple of
    /// hundred milliseconds instead of a sleep.
    /// </summary>
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
