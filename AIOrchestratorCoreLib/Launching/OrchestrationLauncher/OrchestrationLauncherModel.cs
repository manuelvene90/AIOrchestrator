using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Kit;
using AIOrchestratorCoreLib.Kit.PluginGate;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.SessionRunner;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Launching.OrchestrationLauncher;

internal sealed class OrchestrationLauncherModel(
    ISupervisionPaths paths,
    IOrchestratorConfigProvider configProvider,
    IOrchestrationSessionStore store,
    IReadOnlyList<ISessionRunner> runners,
    IPluginGate pluginGate,
    IOrchestrationLog log) : IOrchestrationLauncher
{
    /// <summary>The shell writes its pid file within ~1 s of starting; 40 × 500 ms is generous.</summary>
    const int PID_FILE_WAIT_ATTEMPTS = 40;
    const int PID_FILE_POLL_MILLISECONDS = 500;

    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestratorConfigProvider _configProvider = configProvider;
    readonly IOrchestrationSessionStore _store = store;
    /// <summary>
    /// One entry per transport this app can start, keyed by its kind. A LIST rather than a
    /// parameter per runner because the third one arrived and the fourth is already named in the
    /// enum: a shape that has to be widened for each of them is a shape that gets one of them
    /// forgotten at one of its three call sites.
    /// </summary>
    readonly IReadOnlyDictionary<SessionRunners, ISessionRunner> _runners = runners.ToDictionary(runner => runner.Kind);

    /// <summary>
    /// Consulted in Start_Session, AFTER the runner is resolved and BEFORE it is started — so the
    /// kit is gated once for every transport in that dictionary, including the ones not written yet.
    /// </summary>
    readonly IPluginGate _pluginGate = pluginGate;
    readonly IOrchestrationLog _log = log;

    public IOrchestrationSession Start_Orchestration(string repoName, string repoPath)
    {
        if (!Directory.Exists(repoPath))
            throw new Exception($"Repo path '{repoPath}' for '{repoName}' does not exist — cannot start an orchestration");

        var orchId = OrchId_Allocator.Allocate_NextOrchId(_paths, repoName);

        _store.Create_Orchestration(orchId, repoName, repoPath);

        // The ledger exists from minute one: the card shows a bar and /progress answers, instead
        // of both looking broken until the supervisor gets around to writing the file.
        Planning.PlanSeed_Writer.Ensure_Exists(_paths, orchId, repoName);

        _log.Log_Info(orchId, $"Orchestration created for repo '{repoName}' ({repoPath})");

        Respawn_Supervisor(orchId);

        // NO COMMUNICATOR. It cost $74/day per orchestration and 196 turns to produce 37 identical
        // STATUS entries; the app now narrates a busy supervisor itself, from the same transcript,
        // for free (Narrate_BusySupervisor_Async). Respawn_Communicator is kept for the moment so
        // an already-running one can be restarted, but nothing spawns one any more.

        // Every orchestration starts with one implementer ready (owner directive): the supervisor
        // briefs imp-1 when the first task arrives instead of requesting a spawn first.
        Add_Implementer(orchId);

        // ...and one REVIEWER, for the same reason and a stronger one (owner directive): nobody
        // reviews their own work here, not even for small tasks. If the reviewer had to be
        // requested, the cheap path would be self-review, and a bad review is the one failure that
        // does not announce itself — bad code gets fixed, an approval lets it survive indefinitely.
        // So rev-1 exists before the first task does.
        return Add_Member(orchId, MemberKinds.Reviewer);
    }

    /// <summary>
    /// A BASIC orchestration: ONE session, talking straight to the owner. No supervisor, no
    /// reviewer, no communicator, no gates — for endeavours small enough that the coordination
    /// apparatus costs more than the work it coordinates.
    /// </summary>
    public IOrchestrationSession Start_BasicOrchestration(string repoName, string repoPath)
    {
        if (!Directory.Exists(repoPath))
            throw new Exception($"Repo path '{repoPath}' for '{repoName}' does not exist — cannot start a basic orchestration");

        var orchId = OrchId_Allocator.Allocate_NextOrchId(_paths, repoName);

        _store.Create_Orchestration(orchId, repoName, repoPath);
        Planning.PlanSeed_Writer.Ensure_Exists(_paths, orchId, repoName);

        _log.Log_Info(orchId, $"BASIC orchestration created for repo '{repoName}' ({repoPath}) — one session, no supervisor");

        return Add_Member(orchId, MemberKinds.Solo);
    }

    public IOrchestrationSession Add_Implementer(string orchId)
    {
        return Add_Member(orchId, MemberKinds.Implementer);
    }

    public IOrchestrationSession Add_Member(string orchId, MemberKinds kind)
    {
        return Add_Member(orchId, kind, null);
    }

    public IOrchestrationSession Add_Member(string orchId, MemberKinds kind, string? model)
    {
        // THE SHAPE GATE, and it is here rather than in the button because a click can only ask.
        // The desktop's "+ Implementer" reaches this method directly, so before this a click on a
        // basic card spawned an implementer beside the solo with NO supervisor and no stamp — the
        // orchestration still read as basic, the watchdog never made a supervisor, and the new
        // session waited on a brief that could not come. That bypassed the request, the handover
        // entry and the owner's tap in one click.
        //
        // It throws rather than returning quietly: every caller is either a UI handler that shows
        // the message or a request processor that reports it, and a silent no-op here would spend a
        // click and look like it worked.
        if (!OrchestrationShape.Can_AddMember(_store.Get_Session(orchId).SupervisorSpawnedUtc, kind))
            throw new Exception(OrchestrationShape.Describe_AddMemberRefusal(_store.Get_Session(orchId).SupervisorSpawnedUtc, kind));

        var session = _store.Add_Member(orchId, kind, model);
        var newMember = session.Members[session.Members.Count - 1];

        Respawn_Implementer(orchId, newMember.MemberId);

        // Respawn stamped the new member's spawn state — return the fresh session, not the
        // pre-spawn snapshot.
        return _store.Get_Session(orchId);
    }

    /// <summary>
    /// A basic orchestration becomes a full crew: the solo ends, a supervisor takes over its channel,
    /// and imp-1 spawns empty beside it.
    ///
    /// NOTHING MOVES. The solo has been writing `owner-channel.md` — the file a supervisor owns — so
    /// the supervisor opens it and finds the entire conversation, including the handover entry the
    /// solo had to file before it could ask. No channel migration, no history copy, and the Telegram
    /// topic stays bound to the same orchestration, so the owner keeps reading one thread.
    ///
    /// imp-1 SPAWNS EMPTY, deliberately. Making the solo into imp-1 would strand its reported work in
    /// a spoke it no longer reads, and the supervisor can brief a fresh implementer from the history
    /// it can see.
    ///
    /// THE ORDER IS THE DECISION HERE, and it is chosen for what survives a failure at each step:
    ///
    ///   1. SUPERVISOR FIRST. Its spawn stamps `SupervisorSpawnedUtc` BEFORE the attempt, so from
    ///      that instant the orchestration reads as promoted and the watchdog will respawn the
    ///      supervisor if the spawn itself failed. Nothing has been DESTROYED yet either: if this
    ///      throws, the solo is still running.
    ///
    ///      **This used to claim "the orchestration is exactly what it was", and that was false** —
    ///      the shape flag has already flipped by then and nothing rolls it back, because the stamp
    ///      is deliberately written before the attempt. What is true is narrower: nothing is lost, and
    ///      the state is RECOVERABLE. That half-promoted state now has a name — `Incomplete` — and a
    ///      retry finishes it rather than being refused as "already a crew", which is what the old
    ///      wording's optimism was hiding.
    ///   2. THEN CLOSE THE SOLO. Doing this first would mean a failed supervisor spawn leaves an
    ///      orchestration with NOTHING running and nothing to recover it — the watchdog skips closed
    ///      members, and a basic orchestration has no supervisor slot to protect.
    ///   3. imp-1 LAST, because it is the only step whose failure costs nothing: the supervisor can
    ///      ask for an implementer through the request protocol like any other.
    ///
    /// The window between 1 and 2 has two sessions on one channel. It is seconds, both are
    /// append-only, and the alternative is a window in which the orchestration has no session at all.
    /// </summary>
    public IOrchestrationSession Promote_ToFullCrew(string orchId)
    {
        var session = _store.Get_Session(orchId);

        // ASKED AT THE MOMENT OF EFFECT, because the park-time check can be twelve hours old. Two
        // requests can both pass it while neither has executed, and a `set-model` can change the shape
        // underneath it — so the decision that matters is this one.
        var readiness = OrchestrationShape.Decide_PromotionReadiness(
            session.SupervisorSpawnedUtc,
            OrchestrationShape.Has_LiveSolo(session.Members));

        if (!OrchestrationShape.Can_StillPromote(readiness))
        {
            _log.Log_Warning(orchId, $"Promotion skipped — {readiness}");
            return session;
        }

        // THE REPO IS CHECKED BEFORE THE STAMP, not after. `Respawn_Supervisor`'s first act is to
        // stamp `SupervisorSpawnedUtc`, deliberately, so no tick sees "no pid file and no grace" and
        // double-spawns — which means a spawn that throws leaves the orchestration reading as a crew
        // with its solo still running. Start_Orchestration and Start_BasicOrchestration both validate
        // this; promotion did not, so a moved or renamed repo folder flipped the shape and then failed.
        if (!Directory.Exists(session.RepoPath))
            throw new Exception($"Repo path '{session.RepoPath}' for '{orchId}' does not exist — nothing was promoted");

        // INCOMPLETE means a supervisor spawn was already attempted for this orchestration, so this
        // does NOT spawn a second one: `Respawn_Supervisor` does not terminate an incumbent, it nulls
        // the stored pid and clears the pid file — and both `Kill_AllSessions` and
        // `Kill_OrchestrationSessions` enumerate pid FILES, so the first supervisor would survive the
        // orchestration's close AND the app's exit, still appending to owner-channel.md.
        if (readiness == PromotionReadiness.Ready)
            Respawn_Supervisor(orchId);
        else
            _log.Log_Info(orchId, "Finishing a promotion that stopped halfway — a supervisor spawn was already attempted, so it is not repeated");

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null || MemberKind_Ids.Resolve_Kind(member.MemberId) != MemberKinds.Solo)
                continue;

            _store.Close_Member(orchId, member.MemberId);
            Termination.SessionTerminator.Kill_SessionTree_ByPidFile(_paths.Get_ImplementerPidFile(orchId, member.MemberId));

            _log.Log_Info(orchId, $"Solo session '{member.MemberId}' closed — promoted to a full crew");
        }

        // ONLY IF THERE IS NOT ONE ALREADY. Finishing a half-done promotion must not hand the
        // supervisor an imp-2 beside the imp-1 it already has — an idempotent completion that adds a
        // session every time it runs is not idempotent, it is just slower to notice.
        var hasLiveImplementer = _store.Get_Session(orchId).Members.Any(member =>
            member.ClosedUtc == null && MemberKind_Ids.Resolve_Kind(member.MemberId) == MemberKinds.Implementer);

        return hasLiveImplementer ? _store.Get_Session(orchId) : Add_Implementer(orchId);
    }

    /// <summary>
    /// A full crew becomes one session again: the supervisor and every member end, and a solo takes
    /// over the same owner-channel.
    ///
    /// THE WAY BACK, which did not exist until 2026-08-25. `SupervisorSpawnedUtc` is the shape and
    /// nothing anywhere could clear it, so the promotion prompt, `solo.md` and three comments all
    /// said "one-way" — accurately. The owner asked for one command that goes both ways, and this is
    /// the half that was missing.
    ///
    /// NOTHING MOVES, exactly as on the way up. The supervisor has been writing `owner-channel.md`,
    /// which is the file a solo owns, so the solo opens it and finds the whole conversation including
    /// the supervisor's handover entry. The Telegram topic stays bound to the orchestration, so the
    /// owner keeps reading one thread.
    ///
    /// THE ORDER IS THE MIRROR OF THE PROMOTION'S, chosen the same way — by what survives a failure:
    ///
    ///   1. CLEAR THE STAMP FIRST. From that instant the orchestration reads as basic, so the
    ///      watchdog stops respawning the supervisor. Doing this last would let a tick in the middle
    ///      of the work see a crew with a dead supervisor and spawn a replacement into an
    ///      orchestration being dismantled — the mirror of the double-spawn the promotion's ordering
    ///      protects against.
    ///   2. THEN KILL the supervisor and the members. The stamp is already gone, so nothing revives
    ///      them; and if this throws, the orchestration is basic with a stale crew still running,
    ///      which the retry finishes (Incomplete).
    ///   3. THE SOLO LAST, because it is the only step whose failure leaves a recoverable state: a
    ///      basic orchestration with no live session is exactly what the watchdog respawns.
    ///
    /// The window between 2 and 3 has no session on the channel. That is the opposite trade from the
    /// promotion's (two sessions briefly), and it is forced: a solo spawned before the supervisor
    /// died would meet the supervisor's own turn still writing, and two sessions that both believe
    /// they own the owner's phone line is worse than a few seconds of silence.
    /// </summary>
    public IOrchestrationSession Demote_ToBasic(string orchId)
    {
        var session = _store.Get_Session(orchId);

        // ASKED AT THE MOMENT OF EFFECT, for the reason Promote_ToFullCrew states: a parked request
        // can be twelve hours old and the shape may have moved under it.
        var readiness = OrchestrationShape.Decide_DemotionReadiness(
            session.SupervisorSpawnedUtc,
            OrchestrationShape.Has_LiveSolo(session.Members));

        if (!OrchestrationShape.Can_StillDemote(readiness))
        {
            _log.Log_Warning(orchId, $"Demotion skipped — {readiness}");
            return session;
        }

        // THE REPO IS CHECKED BEFORE ANYTHING IS DESTROYED. The promotion learned this the hard way:
        // it flipped the shape and then failed to spawn, against a repo folder that had been moved.
        // Here the cost would be worse — the crew would be killed and no solo could replace it.
        if (!Directory.Exists(session.RepoPath))
            throw new Exception($"Repo path '{session.RepoPath}' for '{orchId}' does not exist — nothing was demoted");

        _store.Clear_Supervisor(orchId);

        Termination.SessionTerminator.Kill_SessionTree_ByPidFile(_paths.Get_SupervisorPidFile(orchId));

        _log.Log_Info(orchId, "Supervisor ended — demoting to one session");

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc != null || MemberKind_Ids.Resolve_Kind(member.MemberId) == MemberKinds.Solo)
                continue;

            _store.Close_Member(orchId, member.MemberId);
            Termination.SessionTerminator.Kill_SessionTree_ByPidFile(_paths.Get_ImplementerPidFile(orchId, member.MemberId));

            _log.Log_Info(orchId, $"Member '{member.MemberId}' closed — demoted to one session");
        }

        // ONLY IF THERE IS NOT ONE ALREADY — the mirror of the promotion's implementer check, and
        // needed for the same reason: finishing a half-done demotion must not put a second solo
        // beside the live one. That is what Incomplete means here.
        if (OrchestrationShape.Has_LiveSolo(_store.Get_Session(orchId).Members))
        {
            _log.Log_Info(orchId, "Finishing a demotion that stopped halfway — a solo is already running, so it is not spawned again");
            return _store.Get_Session(orchId);
        }

        return Add_Member(orchId, MemberKinds.Solo);
    }

    public void Respawn_Supervisor(string orchId)
    {
        var session = _store.Get_Session(orchId);
        var pidFile = _paths.Get_SupervisorPidFile(orchId);

        var launch = SessionLaunch_Factory.Create(
            SessionRoles.Supervisor,
            orchId,
            SessionLaunch_Factory.SUPERVISOR_MEMBER_ID,
            session.RepoPath,
            session.SupervisorModelOverride ?? _configProvider.Get_Current().Get_ModelForRole(SessionRoles.Supervisor),
            pidFile,
            session.DisplayName);

        // Stamp the spawn (watchdog grace) BEFORE deleting the stale pid file, so no tick can see
        // "no pid file + no grace" and double-spawn. The stale file must go: the sync below must
        // never read the PREVIOUS session's pid as the new one.
        _store.Set_SupervisorPid(orchId, null);
        Delete_StalePidFile_BestEffort(pidFile);

        var runner = Start_Session(launch);

        if (runner == null)
            return;

        if (runner.Kind == SessionRunners.Terminal)
            Sync_TruePid_FromPidFile(pidFile, orchId, "supervisor", truePid => Store_SupervisorTruePid_IfStillOpen(orchId, truePid));

        _log.Log_Info(orchId, $"Supervisor session {Describe_Started(runner)}");
    }

    public void Respawn_Communicator(string orchId)
    {
        var session = _store.Get_Session(orchId);
        var pidFile = _paths.Get_CommunicatorPidFile(orchId);

        var launch = SessionLaunch_Factory.Create(
            SessionRoles.Communicator,
            orchId,
            SessionLaunch_Factory.COMMUNICATOR_MEMBER_ID,
            session.RepoPath,
            _configProvider.Get_Current().Get_ModelForRole(SessionRoles.Communicator),
            pidFile,
            session.DisplayName);

        // No pid lands in session.json for the communicator — the pid file is the liveness
        // source and nothing else needs it. Only the spawn-grace stamp is stored.
        _store.Stamp_CommunicatorSpawned(orchId);
        Delete_StalePidFile_BestEffort(pidFile);

        var runner = Start_Session(launch);

        if (runner != null)
            _log.Log_Info(orchId, $"Communicator session {Describe_Started(runner)}");
    }

    /// <summary>
    /// Respawns a member AS ITS KIND — the id carries it, so a respawned reviewer comes back
    /// read-only instead of being quietly resurrected as a writable implementer.
    /// </summary>
    public void Respawn_Implementer(string orchId, string memberId)
    {
        var session = _store.Get_Session(orchId);

        // A CLOSED MEMBER IS NOT RESPAWNED, re-read here rather than trusted from the caller's
        // snapshot. The watchdog decides what is dead from a `Load_All` taken at the top of its tick
        // and walks sessions doing file I/O and process lookups on the way down, while a promotion
        // closes the solo from a different loop with no shared lock. A tick whose snapshot predates
        // that close, reaching the solo after its process dies, asks for exactly this respawn.
        //
        // The store no longer re-opens the member on the pid write, so the roster stays honest either
        // way — but without this the app would still open a terminal for a session it had retired,
        // and the owner would find a solo alive beside the supervisor that replaced it.
        var member = session.Members.FirstOrDefault(member => member.MemberId == memberId);

        if (member?.ClosedUtc != null)
        {
            _log.Log_Info(orchId, $"Respawn of '{memberId}' skipped — it was closed while the tick was in flight");
            return;
        }

        var pidFile = _paths.Get_ImplementerPidFile(orchId, memberId);
        var kind = MemberKind_Ids.Resolve_Kind(memberId);

        var role = SessionRole_Names.From_MemberKind(kind);

        // THREE TIERS, IN THIS ORDER: the owner's set-model for this orchestration, then the model
        // the supervisor asked for when it requested this member (sized to the task — on the record,
        // so a respawn keeps it), then the config default. A fixed model per role made no sense to
        // the owner (2026-09-07): a one-line fix and a redesign are not the same job.
        //
        // THE THIRD TIER IS NOW PER ROLE, not per member-that-writes-code. A reviewer and a solo both
        // took `implementerModel` here until 2026-09-09, which made the owner's decision — implementer
        // sonnet, reviewer opus — unstateable rather than merely unset. The ladder inside
        // Get_ModelForRole keeps the old answer for a config.json that never heard of the new keys.
        //
        // THE FIRST TIER IS STILL THE IMPLEMENTER'S, deliberately: `set-model implementer` is the
        // owner reaching into ONE orchestration by hand, and it has covered every working member
        // since it existed. Narrowing it to implementers here would silently stop applying to the
        // reviewer the owner had just used it on — a separate decision, and theirs, not this stage's.
        var model = session.ImplementerModelOverride ?? member?.Model ?? _configProvider.Get_Current().Get_ModelForRole(role);

        var launch = SessionLaunch_Factory.Create(role, orchId, memberId, session.RepoPath, model, pidFile, session.DisplayName);

        _store.Set_MemberPid(orchId, memberId, null);
        Delete_StalePidFile_BestEffort(pidFile);

        var runner = Start_Session(launch);

        if (runner == null)
            return;

        if (runner.Kind == SessionRunners.Terminal)
            Sync_TruePid_FromPidFile(pidFile, orchId, memberId, truePid => Store_MemberTruePid_IfStillOpen(orchId, memberId, truePid));

        _log.Log_Info(orchId, $"{kind} '{memberId}' session {Describe_Started(runner)}");
    }

    public void Spawn_GeneralSupervisor()
    {
        GeneralChannel_Initializer.Ensure_Exists(_paths);

        // The general folder is the general supervisor's PERMANENT working directory: its
        // CLAUDE.md (persistent, machine-portable knowledge) auto-loads there, and --continue
        // resumes unambiguously because only general sessions ever run in it.
        var launch = SessionLaunch_Factory.Create(
            SessionRoles.General,
            ChannelDiscovery.GENERAL_ORCH_ID,
            SessionLaunch_Factory.GENERAL_MEMBER_ID,
            _paths.GeneralFolder,
            _configProvider.Get_Current().Get_ModelForRole(SessionRoles.General),
            _paths.GeneralPidFile,
            null);

        var runner = Start_Session(launch);

        if (runner != null)
            _log.Log_Info(ChannelDiscovery.GENERAL_ORCH_ID, $"General supervisor session {Describe_Started(runner)}");
    }

    /// <summary>
    /// THE RUNNER SEAM. The role's configured runner starts the session; a role configured print
    /// that this stage cannot print-run is started in a terminal, with a warning that says so.
    /// Terminal is the default for every role, so with an untouched config.json this is exactly the
    /// spawn that always happened.
    /// </summary>
    /// <summary>
    /// Returns null when the kit gate refused, and the callers treat that as "no session exists".
    /// It used to return the resolved runner either way, so a refusal was followed immediately by
    /// "Supervisor session spawned" and then, 20 s later, by "pid file never appeared" — three
    /// contradictory statements about one non-event, in a log whose entire job is telling a person
    /// what is actually running.
    /// </summary>
    ISessionRunner? Start_Session(ISessionLaunch launch)
    {
        var runner = Resolve_Runner(launch.Role, launch.OrchId);

        // A ROLE THAT LEFT A BRIDGE-DRIVEN MODE LEAVES ITS REGISTRATION BEHIND, and that file is what tells
        // the dispatcher to keep running turns and the watchdog that a missing pid file is by
        // design. Spawning a terminal for this session is the moment we know it is no longer
        // print-run, so it is the moment to clear it — otherwise the member would have a window AND
        // headless turns answering the same brief, while nothing would ever respawn the window.
        if (runner.Kind == SessionRunners.Terminal && PrintSessionState_Store.Delete_IfExists(_paths, launch.Role, launch.OrchId, launch.MemberId))
            _log.Log_Warning(launch.OrchId, $"'{launch.MemberId}' was registered as print-run but its role is now runner: terminal — the stale registration was cleared and it is spawned in a window");

        // THE KIT GATE. A session started against a kit this host was not built for would follow a
        // protocol nobody here has read, and would do it silently — so it does not start.
        //
        // It REFUSES rather than throwing. The watchdog reaches this on its tick, inside the bridge's
        // own loop with nothing catching, so an exception here would take the bridge down — and the
        // bridge staying up is exactly how the owner gets TOLD about the bad kit. Refusing costs a
        // session that would have been wrong anyway; throwing would cost the message about it.
        // Unchecked ALLOWS, but never silently: a verdict that was never taken means the host's kit
        // check did not run, which is a wiring bug and not a bad kit. Said ONCE per host run.
        if (_pluginGate.Verdict == PluginVerdicts.Unchecked && !_pluginGate.Unchecked_WasReported)
        {
            _pluginGate.Unchecked_WasReported = true;
            _log.Log_Warning(launch.OrchId, "The kit check never recorded a verdict, so sessions are starting UNVERIFIED — this host's startup check did not run. The app works; nothing is confirming which protocols these sessions read.");
        }

        if (!_pluginGate.Spawning_Allowed)
        {
            _log.Log_Error(launch.OrchId, $"REFUSED to start '{launch.MemberId}' ({launch.Role}) — {_pluginGate.Refusal}", null);
            return null;
        }

        runner.Start(launch);
        return runner;
    }

    ISessionRunner Resolve_Runner(SessionRoles role, string orchId)
    {
        var configured = _configProvider.Get_Current().Runners.Get_ForRole(role).Runner;

        if (configured == SessionRunners.Terminal)
            return Terminal_Runner();

        // Two questions, and both have to be yes: this stage must support the pairing AND the
        // transport must actually be wired. `bg` is the case that makes the second one real — it is
        // a word config.json accepts and nothing here can start.
        if (Runner_Support.Supports(configured, role) && _runners.TryGetValue(configured, out var runner))
            return runner;

        _log.Log_Warning(orchId, Runner_Support.Describe_Unsupported(configured, role));
        return Terminal_Runner();
    }

    ISessionRunner Terminal_Runner()
    {
        return _runners.TryGetValue(SessionRunners.Terminal, out var terminal)
            ? terminal
            : throw new Exception("No terminal runner was wired — every other runner falls back to it, so it is the one that cannot be optional");
    }

    static string Describe_Started(ISessionRunner runner)
    {
        return runner.Kind switch
        {
            SessionRunners.Print => "registered as print-run (no window; one claude -p turn per inbound entry)",
            SessionRunners.Stream => "registered as stream-run (no window; one living claude -p --input-format stream-json fed on stdin)",
            _ => "spawned",
        };
    }

    /// <summary>
    /// Process.Start's pid is wt.exe's short-lived DELEGATOR (it hands off to the terminal service
    /// and exits) — recording it in session.json made a supervisor conclude LIVE implementers were
    /// dead and retire them mid-work. The TRUE session-host pid is the one the spawned shell writes
    /// into its pid file; sync THAT into session.json once it appears. Until then the stored pid is
    /// null, meaning "spawning" — never "dead".
    /// </summary>
    void Sync_TruePid_FromPidFile(string pidFilePath, string orchId, string sessionLabel, Action<int> storeTruePid)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                for (var attempt = 0; attempt < PID_FILE_WAIT_ATTEMPTS; attempt++)
                {
                    await Task.Delay(PID_FILE_POLL_MILLISECONDS);

                    if (!File.Exists(pidFilePath))
                        continue;

                    if (int.TryParse(File.ReadAllText(pidFilePath).Trim(), out var truePid))
                    {
                        storeTruePid(truePid);
                        return;
                    }
                }

                _log.Log_Warning(orchId, $"{sessionLabel}: pid file '{pidFilePath}' never appeared — the session may have failed to start (the watchdog will respawn it)");
            }
            catch (Exception ex)
            {
                _log.Log_Warning(orchId, $"{sessionLabel}: true-pid sync from '{pidFilePath}' failed: {ex.Message}");
            }
        });
    }

    void Store_SupervisorTruePid_IfStillOpen(string orchId, int truePid)
    {
        var current = _store.Get_Session_OrNull(orchId);

        if (current == null || current.ClosedUtc != null)
            return;

        _store.Set_SupervisorPid(orchId, truePid);
    }

    void Store_MemberTruePid_IfStillOpen(string orchId, string memberId, int truePid)
    {
        var current = _store.Get_Session_OrNull(orchId);

        if (current == null || current.ClosedUtc != null)
            return;

        // A member closed during the sync window is not written to at all. The REASON here used to be
        // "writing its pid would reopen it", and that reason is now gone: `Set_MemberPid` carries
        // `ClosedUtc` through, so the store can no longer resurrect anybody. What is left is smaller
        // and still worth doing — a retired member's pid is a dead process, and recording it invites
        // a later reader to kill or foreground whatever now holds that number.
        //
        // Said explicitly because a guard whose stated reason has moved elsewhere is how a maintainer
        // comes to delete the wrong one, or to add a second copy of the one that actually holds.
        foreach (var member in current.Members)
        {
            if (member.MemberId == memberId && member.ClosedUtc == null)
                _store.Set_MemberPid(orchId, memberId, truePid);
        }
    }

    static void Delete_StalePidFile_BestEffort(string pidFilePath)
    {
        try
        {
            if (File.Exists(pidFilePath))
                File.Delete(pidFilePath);
        }
        catch
        {
            // A locked pid file is tolerable — the sync may then read a stale pid for one poll,
            // but the shell overwrites it within a second of starting.
        }
    }
}
