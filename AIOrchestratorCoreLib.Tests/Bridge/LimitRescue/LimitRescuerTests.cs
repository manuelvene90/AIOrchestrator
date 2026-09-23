using AIOrchestratorCoreLib.Bridge.LimitRescue;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Usage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.LimitRescue;

/// <summary>
/// WHICH SESSIONS ARE STOPPED SO THE WATCHDOG BRINGS THEM BACK after a usage limit (owner,
/// 2026-09-23: <i>"nothing happened automatically … I sent a /resume command to the gen sup, and not
/// all got awakened"</i>). Driven against real session folders, real transcripts and the real store,
/// with the process table faked: no session is ever spawned or killed.
/// </summary>
public class LimitRescuerTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly RecordingLog_Fake _log = new();
    readonly SessionProcesses_Fake _processes = new();
    readonly ILimitRescuer _rescuer;

    /// <summary>11:13 in Rome on 2026-09-23 — when the five-hour window was refused that morning.</summary>
    static readonly DateTime RefusedAt = new(2026, 9, 23, 9, 13, 0, DateTimeKind.Utc);

    const string ROME_NOON = "You've hit your session limit · resets 12pm (Europe/Rome)";

    static bool NothingBridgeDriven(SessionRoles role, string orchId, string memberId) => false;

    static bool NoProbe(DateTime floorUtc) => false;

    public LimitRescuerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-limitrescuer-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        File.WriteAllText(_paths.ConfigFile, "{\"repos\":[]}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);
        _rescuer = LimitRescuer_Factory.Create(_paths, _store, _log, _processes);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }
    }

    /// <summary>
    /// /resume's sweep: every blocked, running session is stopped — the general supervisor included —
    /// and nothing else is. An awake member keeps working; a dead one is the watchdog's already.
    /// </summary>
    [Fact]
    public void TheForcedSweep_StopsEveryBlockedRunningSession_AndLeavesTheRestAlone()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var implementer = session.Members[0].MemberId;
        var reviewer = session.Members[1].MemberId;

        var supervisorPid = Seed_Supervisor(session.OrchId, Limit_Refusal(RefusedAt, ROME_NOON), alive: true);
        var implementerPid = Seed_Member(session.OrchId, implementer, Ordinary_Reply(RefusedAt), alive: true);
        var reviewerPid = Seed_Member(session.OrchId, reviewer, Limit_Refusal(RefusedAt, ROME_NOON), alive: false);
        var generalPid = Seed_General(Limit_Refusal(RefusedAt, ROME_NOON), alive: true);

        // One minute after the refusal: no grace, no reset — the owner said go.
        var sweep = _rescuer.Rescue_AllBlocked_Now(RefusedAt.AddMinutes(1), NothingBridgeDriven);

        Assert.Equal(2, _processes.Killed.Count);
        Assert.Contains(supervisorPid, _processes.Killed);
        Assert.Contains(generalPid, _processes.Killed);
        Assert.DoesNotContain(implementerPid, _processes.Killed);
        Assert.DoesNotContain(reviewerPid, _processes.Killed);

        Assert.Contains($"{session.OrchId}/supervisor", sweep.Restarted);
        Assert.Contains("general", sweep.Restarted);
        Assert.Equal(2, sweep.Restarted.Count);
        Assert.Contains(($"{session.OrchId}/{implementer}", LimitRescueSkips.NotBlocked), sweep.LeftAlone);
        Assert.Contains(($"{session.OrchId}/{reviewer}", LimitRescueSkips.NotRunning), sweep.LeftAlone);

        var logLine = sweep.Describe_ForLog();
        Assert.Contains($"{session.OrchId}/supervisor", logLine);
        Assert.Contains($"{session.OrchId}/{implementer} (awake", logLine);
        Assert.Contains($"{session.OrchId}/{reviewer} (not running", logLine);

        var ownerLine = sweep.Describe_ForOwner();
        Assert.Contains("restarted 2", ownerLine);
        Assert.Contains("1 already awake", ownerLine);
        Assert.Contains("1 not running", ownerLine);
    }

    /// <summary>
    /// /RESUME TWICE IN A ROW. The first stops the blocked supervisor; by the second the watchdog has
    /// respawned it, and its resumed transcript still ends in the old refusal while it starts up. It is
    /// not stopped again — the running process is not the one that was refused.
    /// </summary>
    [Fact]
    public void ASecondResume_DoesNotStopASessionRespawnedSinceItsRefusal()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var supervisorPid = Seed_Supervisor(session.OrchId, Limit_Refusal(RefusedAt, ROME_NOON), alive: true);
        var firstResume = RefusedAt.AddMinutes(50);

        _rescuer.Rescue_AllBlocked_Now(firstResume, NothingBridgeDriven);
        _processes.Mark_Alive(supervisorPid, startedUtc: firstResume.AddSeconds(5));

        var second = _rescuer.Rescue_AllBlocked_Now(firstResume.AddSeconds(20), NothingBridgeDriven);

        Assert.Single(_processes.Killed);
        Assert.Contains(($"{session.OrchId}/supervisor", LimitRescueSkips.StartedSinceRefusal), second.LeftAlone);
        Assert.Contains("already restarting", second.Describe_ForOwner());
    }

    /// <summary>A bridge-driven session is the dispatcher's, whose appointment /resume clears — it is never killed here.</summary>
    [Fact]
    public void TheForcedSweep_LeavesABridgeDrivenSessionToItsDispatcher()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var implementer = session.Members[0].MemberId;
        var implementerPid = Seed_Member(session.OrchId, implementer, Limit_Refusal(RefusedAt, ROME_NOON), alive: true);

        var sweep = _rescuer.Rescue_AllBlocked_Now(
            RefusedAt.AddMinutes(1),
            (role, orchId, memberId) => memberId == implementer);

        Assert.DoesNotContain(implementerPid, _processes.Killed);
        Assert.Contains(($"{session.OrchId}/{implementer}", LimitRescueSkips.BridgeDriven), sweep.LeftAlone);
    }

    /// <summary>A paused orchestration is asleep by the owner's decision; /resume does not wake it, so nothing of it is stopped.</summary>
    [Fact]
    public void TheForcedSweep_SkipsAPausedOrchestration()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var supervisorPid = Seed_Supervisor(session.OrchId, Limit_Refusal(RefusedAt, ROME_NOON), alive: true);
        _store.Set_Paused(session.OrchId, true);

        var sweep = _rescuer.Rescue_AllBlocked_Now(RefusedAt.AddMinutes(1), NothingBridgeDriven);

        Assert.DoesNotContain(supervisorPid, _processes.Killed);
        Assert.DoesNotContain(sweep.Restarted, slot => slot.StartsWith(session.OrchId, StringComparison.Ordinal));
        Assert.DoesNotContain(sweep.LeftAlone, pair => pair.Slot.StartsWith(session.OrchId, StringComparison.Ordinal));
    }

    /// <summary>
    /// THE PERIODIC PATH, on the morning's clock: the dispatcher open for hours (a stale lift), the
    /// refusal naming noon in Rome. Nothing before 12:05, one stop at 12:05, none again at 12:20.
    /// </summary>
    [Fact]
    public void ThePeriodicRescue_WaitsForTheResetTheRefusalNames()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var supervisorPid = Seed_Supervisor(session.OrchId, Limit_Refusal(RefusedAt, ROME_NOON), alive: true);
        var dispatchOpenSince = RefusedAt.AddHours(-3);

        foreach (var minute in new[] { 18, 30, 50 })
            _rescuer.Rescue_Due(new DateTime(2026, 9, 23, 9, minute, 0, DateTimeKind.Utc), dispatchOpenSince, null, NothingBridgeDriven, NoProbe);

        Assert.Empty(_processes.Killed);

        _rescuer.Rescue_Due(new DateTime(2026, 9, 23, 10, 5, 0, DateTimeKind.Utc), dispatchOpenSince, null, NothingBridgeDriven, NoProbe);

        Assert.Equal([supervisorPid], _processes.Killed);
        Assert.True(_log.Has_Line_Containing("supervisor was still stuck on a usage limit"), _log.Dump());
        Assert.True(_log.Has_Line_Containing("resets 12pm (Europe/Rome)"), _log.Dump());

        _rescuer.Rescue_Due(new DateTime(2026, 9, 23, 10, 20, 0, DateTimeKind.Utc), dispatchOpenSince, null, NothingBridgeDriven, NoProbe);

        Assert.Single(_processes.Killed);
    }

    /// <summary>A /resume restart counts toward the half-hour cap — the periodic path does not stop the same session again a tick later.</summary>
    [Fact]
    public void AForcedRestart_CountsTowardThePeriodicCap()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        Seed_Supervisor(session.OrchId, Limit_Refusal(RefusedAt, ROME_NOON), alive: true);
        var afterReset = new DateTime(2026, 9, 23, 10, 10, 0, DateTimeKind.Utc);

        _rescuer.Rescue_AllBlocked_Now(afterReset, NothingBridgeDriven);
        _rescuer.Rescue_Due(afterReset.AddMinutes(2), RefusedAt.AddHours(-3), null, NothingBridgeDriven, NoProbe);

        Assert.Single(_processes.Killed);
    }

    /// <summary>The probe path reaches the decider: a fresh reading showing the allowance back restarts before the named reset.</summary>
    [Fact]
    public void ThePeriodicRescue_TrustsAFreshProbeShowingTheAllowanceBack()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var supervisorPid = Seed_Supervisor(session.OrchId, Limit_Refusal(RefusedAt, ROME_NOON), alive: true);
        DateTime? askedFloor = null;

        _rescuer.Rescue_Due(
            new DateTime(2026, 9, 23, 9, 58, 0, DateTimeKind.Utc), RefusedAt.AddHours(-3), null, NothingBridgeDriven,
            floorUtc =>
            {
                askedFloor = floorUtc;
                return true;
            });

        Assert.Equal([supervisorPid], _processes.Killed);
        Assert.Equal(new DateTime(2026, 9, 23, 9, 53, 0, DateTimeKind.Utc), askedFloor);
    }

    string Seed_Supervisor(string orchId, string transcriptLine, bool alive)
    {
        return Seed(
            Path.Combine(_paths.Get_OrchestrationFolder(orchId), UsageTotals_Reader.SESSION_USAGE_FILE),
            _paths.Get_SupervisorPidFile(orchId), $"{orchId}-sup", transcriptLine, alive);
    }

    string Seed_Member(string orchId, string memberId, string transcriptLine, bool alive)
    {
        return Seed(
            Path.Combine(_paths.Get_ImplementerFolder(orchId, memberId), UsageTotals_Reader.SESSION_USAGE_FILE),
            _paths.Get_ImplementerPidFile(orchId, memberId), $"{orchId}-{memberId}", transcriptLine, alive);
    }

    string Seed_General(string transcriptLine, bool alive)
    {
        return Seed(
            Path.Combine(_paths.GeneralFolder, UsageTotals_Reader.SESSION_USAGE_FILE),
            _paths.GeneralPidFile, "general", transcriptLine, alive);
    }

    /// <summary>Writes the slot's probe file pointing at a one-line transcript; returns its pid file.</summary>
    string Seed(string usageFile, string pidFile, string name, string transcriptLine, bool alive)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(usageFile)!);

        var transcriptPath = Path.Combine(_tempRoot, "transcripts", $"{name}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(transcriptPath)!);
        File.WriteAllText(transcriptPath, transcriptLine + "\n");

        File.WriteAllText(usageFile, "{\"transcript_path\":\"" + transcriptPath.Replace("\\", "\\\\") + "\"}");

        if (alive)
            _processes.Mark_Alive(pidFile);

        return pidFile;
    }

    static string Stamp(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    static string Ordinary_Reply(DateTime utc)
        => "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(utc)
         + "\",\"uuid\":\"u\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"done\"}]}}";

    static string Limit_Refusal(DateTime utc, string text)
        => "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(utc)
         + "\",\"uuid\":\"u\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}]},"
         + "\"error\":\"rate_limit\",\"isApiErrorMessage\":true,\"apiErrorStatus\":429}";
}
