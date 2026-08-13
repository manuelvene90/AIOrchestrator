using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// THE WIRING, which two of us declared unpinnable and a reviewer then pinned.
///
/// The feature is not the reader or the prompt — it is WHAT A CLOSE-IMPLEMENTER REQUEST DOES when it
/// lands. That was "parks for the owner's tap" until 2026-08-13 and is "closes the member" since;
/// this file kept its name and its harness across that reversal, and each case says which side of it
/// the case belongs to. The decision lives in <c>BridgeEngineModel</c>, which is
/// <c>internal sealed</c> with no <c>InternalsVisibleTo</c>, so it was written off as reachable only
/// through a timing-dependent test of the kind that fails one run in seven and teaches everyone to
/// re-run it. That was an assumption, and nobody measured it.
///
/// NOT COVERED HERE, stated rather than left to be discovered: the tap-refusal branch in the confirmed
/// -close handler — the one that must NOT be deleted as unreachable, because its <c>else</c> ends the
/// whole orchestration. Reaching it needs a live confirmation registration, which needs the Telegram
/// client this harness deliberately does not have. It becomes testable through the injection seam
/// imp-1 built on the R1 branch, and until then it is verified by reading the diff, which is weaker
/// and is why it is written down here.
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
    /// A close-implementer request CLOSES THE MEMBER ON ARRIVAL — owner directive 2026-08-13,
    /// reversing their own decision of 2026-08-12: *"I wanted to be asked for confirmation to close
    /// the entire orchestration session. I trust the supervisor to manage its subordinate windows."*
    ///
    /// This assertion was the exact inverse one day ago, and that is the point of leaving it here
    /// rather than deleting the file: the guard it pinned is gone deliberately, not by accident, and
    /// the next person to find a close executing without a tap should find this docstring rather than
    /// conclude a protection was lost.
    ///
    /// THE THREE ASSERTIONS STAY SEPARATE, which was the reviewer's correction to the original and
    /// survives the reversal intact. The wait is on the request being CONSUMED, because "wait until
    /// the member is closed" also fails when the reader cannot see member closes at all — two routes
    /// to one symptom. `ClosedUtc` is specific to executing-versus-parking. And the absence of HELD
    /// closes the hole `ClosedUtc` leaves open on its own: a change that closed the member AND still
    /// parked a confirmation would satisfy the first two and put the owner's tap straight back.
    /// </summary>
    [Fact]
    public async Task ACloseRequestClosesTheMemberOnArrivalWithoutAskingTheOwner()
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
            _store.Get_Session(session.OrchId).Members[0].ClosedUtc != null,
            $"'{memberId}' is still open — the close did not execute on arrival");

        Assert.DoesNotContain(
            "HELD",
            File.ReadAllText(_paths.Get_OwnerChannelFile(session.OrchId)));
    }

    /// <summary>
    /// AND THE ORCHESTRATION CLOSE STILL ASKS. The owner kept that tap explicitly — it is the
    /// irreversible one, it ends every session including the supervisor's, and it cannot be undone.
    ///
    /// Asserted here, in the same file, because the two actions share `CloseConfirmation_Parking` and
    /// every sweep around it. Removing the member's confirmation is one `if` away from removing the
    /// orchestration's, and nothing else in the suite would notice.
    /// </summary>
    [Fact]
    public async Task ACloseOrchestrationRequestStillWaitsForTheOwner()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        var requestPath = Path.Combine(_paths.RequestsFolder, "close-orch.json");
        File.WriteAllText(
            requestPath,
            $$"""{"action":"close-orchestration","orchId":"{{session.OrchId}}","reason":"the work is delivered","requester":"supervisor of {{session.OrchId}}"}""");

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => !File.Exists(requestPath)),
            "the request file was never consumed — the engine never processed it");

        Assert.True(
            _store.Get_Session(session.OrchId).ClosedUtc == null,
            "the ORCHESTRATION was closed without the owner confirming — the irreversible tap was lost");

        Assert.Contains(
            "HELD",
            File.ReadAllText(_paths.Get_OwnerChannelFile(session.OrchId)));
    }

    /// <summary>
    /// A member close PARKED BEFORE the rule change is released, never executed. It may be hours old,
    /// and the member may since have been briefed with new work or finished — so it is not authority
    /// to kill a session, by the same standard the lapse path has always applied: a close must
    /// reflect the situation at the moment it takes effect, not a stale one.
    ///
    /// Executing it instead would apply a new policy retroactively to a decision the owner declined
    /// to make. Dropping costs one re-drop; executing costs a live session's context.
    /// </summary>
    [Fact]
    public async Task AMemberCloseParkedBeforeTheRuleChangeIsReleasedRatherThanExecuted()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var memberId = session.Members[0].MemberId;

        var parkedPath = Park_Directly(
            "close-member-from-yesterday.json",
            $$"""{"action":"close-implementer","orchId":"{{session.OrchId}}","memberId":"{{memberId}}","reason":"asked for before the rule changed"}""");

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => !File.Exists(parkedPath)),
            "the parked request was never resolved — nothing below means anything");

        Assert.True(
            _store.Get_Session(session.OrchId).Members[0].ClosedUtc == null,
            $"'{memberId}' was closed by a stale parked request the owner never answered");

        Assert.Contains(
            "RELEASED",
            File.ReadAllText(_paths.Get_OwnerChannelFile(session.OrchId)));
    }

    /// <summary>
    /// AND THE SWEEP LEAVES ORCHESTRATION CLOSES ALONE — with a POSITIVE CONTROL, because the first
    /// version of this test asserted two ABSENCES and passed with the sweep entirely removed. Nothing
    /// happened, and nothing happening satisfied "the orchestration close was not touched": the exact
    /// two-routes-to-one-state defect CLAUDE.md item 20 records, written into the guard test of the
    /// very sweep it was meant to pin. My own control table showed it staying green and I read past it.
    ///
    /// Both kinds are parked in the SAME tick now. The member close being released is the proof that
    /// the sweep ran at all; only then does the orchestration close surviving mean it was SKIPPED
    /// rather than simply never reached. With no Telegram client the ask fails closed and the parked
    /// file stays put, which is the stable end state this harness already relies on.
    /// </summary>
    [Fact]
    public async Task TheReleaseSweepReleasesTheMemberCloseAndLeavesTheOrchestrationCloseParked()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var memberId = session.Members[0].MemberId;

        var parkedMember = Park_Directly(
            "close-member-awaiting.json",
            $$"""{"action":"close-implementer","orchId":"{{session.OrchId}}","memberId":"{{memberId}}","reason":"asked for before the rule changed"}""");

        var parkedOrchestration = Park_Directly(
            "close-orch-awaiting.json",
            $$"""{"action":"close-orchestration","orchId":"{{session.OrchId}}","reason":"the work is delivered","requester":"supervisor of {{session.OrchId}}"}""");

        await Tick_Once_Async();

        // THE POSITIVE CONTROL, first: without this the rest passes when the sweep never ran.
        Assert.True(
            Wait_Until(() => !File.Exists(parkedMember)),
            "the parked MEMBER close was not released — the sweep did not run, so nothing below means anything");

        Assert.True(File.Exists(parkedOrchestration), "the orchestration close was released — the owner's tap was disarmed");
        Assert.True(_store.Get_Session(session.OrchId).ClosedUtc == null, "the orchestration was closed outright");
        Assert.True(_store.Get_Session(session.OrchId).Members[0].ClosedUtc == null, "the released member close was executed instead of released");
    }

    /// <summary>
    /// A `close-implementer` REQUEST NAMING A SOLO IS REFUSED — because a solo IS the orchestration.
    ///
    /// `Execute_CloseImplementer` has no kind check, and member closes no longer wait for a tap, so
    /// this route ended every session in a BASIC orchestration with nobody asked. That is exactly the
    /// confirmation the owner kept: *"I wanted to be asked for confirmation to close the entire
    /// orchestration session."* Removing the member tap must not smuggle in an untapped route to the
    /// orchestration one.
    ///
    /// REFUSED RATHER THAN REROUTED, deliberately. Routing it would turn one request kind into another
    /// silently, and this file has already paid for a request whose kind and effect disagreed. The
    /// requester is told which action to use instead, so nothing is lost but a round trip.
    ///
    /// THE GUARD IS ON THE REQUEST, NOT INSIDE `Execute_CloseImplementer`, and that is the promotion
    /// case: basic→full promotion has to close `solo-1` as part of becoming a crew. Guarding the
    /// execution would block the app's own legitimate close; guarding the request refuses only what an
    /// AGENT asks for, which is the untrusted input. The promotion path can still call the execution
    /// directly when it exists.
    /// </summary>
    [Fact]
    public async Task ACloseRequestNamingASoloIsRefusedBecauseASoloIsTheOrchestration()
    {
        var session = _launcher.Start_BasicOrchestration("Repo", _tempRepo);
        var soloId = session.Members[0].MemberId;

        Assert.StartsWith(MemberKind_Ids.SOLO_PREFIX, soloId);

        var requestPath = Path.Combine(_paths.RequestsFolder, "close-solo.json");
        File.WriteAllText(
            requestPath,
            $$"""{"action":"close-implementer","orchId":"{{session.OrchId}}","memberId":"{{soloId}}","reason":"tidying up"}""");

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => !File.Exists(requestPath)),
            "the request file was never consumed — the engine never processed it");

        Assert.True(
            _store.Get_Session(session.OrchId).Members[0].ClosedUtc == null,
            $"'{soloId}' was closed — a basic orchestration was ended with nobody asked");

        Assert.Contains(
            "close-orchestration",
            File.ReadAllText(_paths.Get_OwnerChannelFile(session.OrchId)));
    }

    /// <summary>
    /// A CLOSE THAT FAILED IS FILED AS FAILED. The `finally` archived every request as "executed"
    /// whichever way the `try` went, so a close that threw before anything was killed left an audit
    /// record of an execution that never happened — in the folder that exists precisely because "who
    /// asked, and what became of it" was once unanswerable minutes after the fact.
    ///
    /// An unknown member id is the reachable way in: `Close_Member` throws when no member matches,
    /// before the session tree is touched, so the member is still running and the request is resolved.
    /// </summary>
    [Fact]
    public async Task ACloseThatFailedIsArchivedAsFailedNotExecuted()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        var requestPath = Path.Combine(_paths.RequestsFolder, "close-ghost.json");
        File.WriteAllText(
            requestPath,
            $$"""{"action":"close-implementer","orchId":"{{session.OrchId}}","memberId":"imp-99","reason":"a member that never existed"}""");

        await Tick_Once_Async();

        Assert.True(
            Wait_Until(() => !File.Exists(requestPath)),
            "the request file was never consumed — the engine never processed it");

        var resolvedFolder = CloseConfirmation_Parking.Get_ResolvedFolder(_paths);
        var archived = Directory.Exists(resolvedFolder) ? Directory.GetFiles(resolvedFolder, "*close-ghost*") : [];

        Assert.Single(archived);
        Assert.StartsWith("failed-", Path.GetFileName(archived[0]));
    }

    /// <summary>Writes a request straight into the awaiting folder, as parking would have left it.</summary>
    string Park_Directly(string fileName, string json)
    {
        var awaitingFolder = CloseConfirmation_Parking.Get_AwaitingFolder(_paths);
        Directory.CreateDirectory(awaitingFolder);

        var parkedPath = Path.Combine(awaitingFolder, fileName);
        File.WriteAllText(parkedPath, json);

        return parkedPath;
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
