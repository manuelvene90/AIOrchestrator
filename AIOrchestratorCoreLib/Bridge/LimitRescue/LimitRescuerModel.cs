using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Termination.SessionProcesses;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Bridge.LimitRescue;

internal sealed class LimitRescuerModel(
    ISupervisionPaths paths,
    IOrchestrationSessionStore store,
    IOrchestrationLog log,
    ISessionProcesses processes) : ILimitRescuer
{
    /// <summary>The general supervisor's orchestration id in the log and the engine's lookups (the engine's GLOBAL_ORCH_ID).</summary>
    const string GENERAL_ORCH_ID = "";

    const string GENERAL_SLOT = "general";

    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestrationSessionStore _store = store;
    readonly IOrchestrationLog _log = log;
    readonly ISessionProcesses _processes = processes;

    /// <summary>
    /// ONE LOCK FOR BOTH PATHS, held across a whole sweep: the periodic path runs on the mirror loop and
    /// /resume on the inbound loop, and without it both could stop the same session a moment apart.
    /// </summary>
    readonly object _lock = new();

    /// <summary>When each slot was last stopped for being stuck on a limit, by either path.</summary>
    readonly Dictionary<string, DateTime> _lastRestartUtc = [];

    DateTime _lastCheckUtc = DateTime.MinValue;

    public void Rescue_Due(
        DateTime nowUtc,
        DateTime dispatchOpenSinceUtc,
        DateTime? accountSwitchedAtUtc,
        Func<SessionRoles, string, string, bool> isBridgeDriven,
        Func<DateTime, bool> freshProbeShowsAllowanceSince)
    {
        lock (_lock)
        {
            if ((nowUtc - _lastCheckUtc).TotalSeconds < LimitRescue_Decider.CHECK_INTERVAL_SECONDS)
                return;

            _lastCheckUtc = nowUtc;

            // Nothing can qualify inside the grace — skip the glob and the transcript reads entirely.
            if (nowUtc - dispatchOpenSinceUtc < TimeSpan.FromMinutes(LimitRescue_Decider.GRACE_MINUTES))
                return;

            foreach (var slot in Enumerate_Slots())
            {
                if (isBridgeDriven(slot.Role, slot.OrchId, slot.MemberId))
                    continue;

                var refusal = SessionActivity_Probe.Read_UsageLimitRefusal_OrNull(slot.UsageFile);

                if (refusal == null || !_processes.Is_Alive(slot.PidFile) || Started_SinceRefusal(slot.PidFile, refusal.Value.RefusedAtUtc))
                    continue;

                _lastRestartUtc.TryGetValue(slot.Key, out var lastRestartUtc);
                var refusedAtUtc = refusal.Value.RefusedAtUtc;

                // THE PROBE IS READ LAST AND ONLY FOR A BLOCKED SESSION: it globs every usage file, so it
                // is handed over as a Func the decider asks only when no earlier gate has decided.
                var grounds = LimitRescue_Decider.Decide_Grounds_OrNull(
                    refusedAtUtc,
                    LimitRescue_Decider.Read_RefusalResetsAtUtc_OrNull(refusal.Value.Text, refusal.Value.ApiStatus, refusedAtUtc),
                    accountSwitchedAtUtc,
                    () => freshProbeShowsAllowanceSince(LimitRescue_Decider.Build_FreshProbeFloor(refusedAtUtc, accountSwitchedAtUtc, nowUtc)),
                    dispatchOpenSinceUtc,
                    lastRestartUtc == default ? null : lastRestartUtc,
                    nowUtc);

                if (grounds == null)
                    continue;

                _lastRestartUtc[slot.Key] = nowUtc;
                _log.Log_Info(slot.OrchId, LimitRescue_Decider.Describe_Restart(slot.Label, grounds.Value, refusal.Value.Text));
                _processes.Kill_Tree(slot.PidFile);
            }
        }
    }

    public LimitRescueSweep Rescue_AllBlocked_Now(DateTime nowUtc, Func<SessionRoles, string, string, bool> isBridgeDriven)
    {
        List<string> restarted = [];
        List<(string Slot, LimitRescueSkips Reason)> leftAlone = [];

        lock (_lock)
        {
            foreach (var slot in Enumerate_Slots())
            {
                var name = slot.OrchId == GENERAL_ORCH_ID ? GENERAL_SLOT : $"{slot.OrchId}/{slot.Label}";

                // Its appointment was cleared by /resume already (Clear_LimitDeferrals); a terminal
                // stop would only kill a dispatcher's turn in flight.
                if (isBridgeDriven(slot.Role, slot.OrchId, slot.MemberId))
                {
                    leftAlone.Add((name, LimitRescueSkips.BridgeDriven));
                    continue;
                }

                if (!_processes.Is_Alive(slot.PidFile))
                {
                    leftAlone.Add((name, LimitRescueSkips.NotRunning));
                    continue;
                }

                // NOT BLOCKED IS NEVER STOPPED, and "cannot tell" is not blocked: a /resume must never
                // interrupt live work.
                var refusal = SessionActivity_Probe.Read_UsageLimitRefusal_OrNull(slot.UsageFile);

                if (refusal == null)
                {
                    leftAlone.Add((name, LimitRescueSkips.NotBlocked));
                    continue;
                }

                if (Started_SinceRefusal(slot.PidFile, refusal.Value.RefusedAtUtc))
                {
                    leftAlone.Add((name, LimitRescueSkips.StartedSinceRefusal));
                    continue;
                }

                _lastRestartUtc[slot.Key] = nowUtc;
                _processes.Kill_Tree(slot.PidFile);
                restarted.Add(name);
            }
        }

        return new LimitRescueSweep(restarted, leftAlone);
    }

    /// <summary>
    /// THE RUNNING PROCESS IS NOT THE ONE THAT WAS REFUSED. A respawned supervisor or solo resumes the
    /// SAME transcript, whose last reply stays the old refusal until the new process answers its role
    /// command; a fresh implementer's probe file keeps pointing at the old transcript until its first
    /// status line. Either reads as "blocked" for the first seconds of the new process — and the owner
    /// sending /resume twice in a row (the repeat press every toggle on this machine has seen) would
    /// kill it mid-startup. A process refused AFTER it started writes a newer refusal, so this never
    /// hides a session that is really stuck.
    /// </summary>
    bool Started_SinceRefusal(string pidFile, DateTime refusedAtUtc)
    {
        var startedUtc = _processes.Read_StartedUtc_OrNull(pidFile);

        return startedUtc != null && startedUtc.Value > refusedAtUtc;
    }

    /// <summary>
    /// Every terminal slot the watchdog would bring back: the general supervisor, each open
    /// orchestration's supervisor (a BASIC one has none — its solo is a member) and its open members.
    /// The watchdog's own two exemptions apply: a closed session is never revived, and a paused one is
    /// asleep by the owner's decision, not stuck.
    /// </summary>
    IEnumerable<(string Key, string OrchId, string Label, SessionRoles Role, string MemberId, string UsageFile, string PidFile)> Enumerate_Slots()
    {
        yield return (
            $"{GENERAL_ORCH_ID}/{SessionLaunch_Factory.GENERAL_MEMBER_ID}", GENERAL_ORCH_ID, "general supervisor", SessionRoles.General, SessionLaunch_Factory.GENERAL_MEMBER_ID,
            Path.Combine(_paths.GeneralFolder, UsageTotals_Reader.SESSION_USAGE_FILE), _paths.GeneralPidFile);

        foreach (var session in _store.Load_All())
        {
            if (session.ClosedUtc != null || session.Paused)
                continue;

            if (!OrchestrationShape.Is_BasicOrchestration(session.SupervisorSpawnedUtc))
            {
                yield return (
                    $"{session.OrchId}/{SessionLaunch_Factory.SUPERVISOR_MEMBER_ID}", session.OrchId, "supervisor", SessionRoles.Supervisor, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID,
                    Path.Combine(_paths.Get_OrchestrationFolder(session.OrchId), UsageTotals_Reader.SESSION_USAGE_FILE),
                    _paths.Get_SupervisorPidFile(session.OrchId));
            }

            foreach (var member in session.Members)
            {
                if (member.ClosedUtc != null)
                    continue;

                yield return (
                    $"{session.OrchId}/{member.MemberId}", session.OrchId, member.MemberId,
                    SessionRole_Names.From_MemberKind(MemberKind_Ids.Resolve_Kind(member.MemberId)), member.MemberId,
                    Path.Combine(_paths.Get_ImplementerFolder(session.OrchId, member.MemberId), UsageTotals_Reader.SESSION_USAGE_FILE),
                    _paths.Get_ImplementerPidFile(session.OrchId, member.MemberId));
            }
        }
    }
}
