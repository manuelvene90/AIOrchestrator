using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Launching.OrchestrationLauncher;

internal sealed class OrchestrationLauncherModel(
    ISupervisionPaths paths,
    IOrchestratorConfigProvider configProvider,
    IOrchestrationSessionStore store,
    ISessionSpawner spawner,
    IOrchestrationLog log) : IOrchestrationLauncher
{
    /// <summary>The shell writes its pid file within ~1 s of starting; 40 × 500 ms is generous.</summary>
    const int PID_FILE_WAIT_ATTEMPTS = 40;
    const int PID_FILE_POLL_MILLISECONDS = 500;

    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestratorConfigProvider _configProvider = configProvider;
    readonly IOrchestrationSessionStore _store = store;
    readonly ISessionSpawner _spawner = spawner;
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

        var session = _store.Add_Member(orchId, kind);
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

    public void Respawn_Supervisor(string orchId)
    {
        var session = _store.Get_Session(orchId);
        var pidFile = _paths.Get_SupervisorPidFile(orchId);

        var command = SpawnCommand_Builder.Build_ForSupervisor(
            orchId,
            session.RepoPath,
            session.SupervisorModelOverride ?? _configProvider.Get_Current().SupervisorModel,
            pidFile);

        // Stamp the spawn (watchdog grace) BEFORE deleting the stale pid file, so no tick can see
        // "no pid file + no grace" and double-spawn. The stale file must go: the sync below must
        // never read the PREVIOUS session's pid as the new one.
        _store.Set_SupervisorPid(orchId, null);
        Delete_StalePidFile_BestEffort(pidFile);

        _spawner.Spawn(command);
        Sync_TruePid_FromPidFile(pidFile, orchId, "supervisor", truePid => Store_SupervisorTruePid_IfStillOpen(orchId, truePid));

        _log.Log_Info(orchId, "Supervisor session spawned");
    }

    public void Respawn_Communicator(string orchId)
    {
        var session = _store.Get_Session(orchId);
        var pidFile = _paths.Get_CommunicatorPidFile(orchId);

        var command = SpawnCommand_Builder.Build_ForCommunicator(
            orchId,
            session.RepoPath,
            _configProvider.Get_Current().CommunicatorModel,
            pidFile);

        // No pid lands in session.json for the communicator — the pid file is the liveness
        // source and nothing else needs it. Only the spawn-grace stamp is stored.
        _store.Stamp_CommunicatorSpawned(orchId);
        Delete_StalePidFile_BestEffort(pidFile);

        _spawner.Spawn(command);
        _log.Log_Info(orchId, "Communicator session spawned");
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
        if (session.Members.FirstOrDefault(member => member.MemberId == memberId)?.ClosedUtc != null)
        {
            _log.Log_Info(orchId, $"Respawn of '{memberId}' skipped — it was closed while the tick was in flight");
            return;
        }

        var pidFile = _paths.Get_ImplementerPidFile(orchId, memberId);
        var kind = MemberKind_Ids.Resolve_Kind(memberId);
        var model = session.ImplementerModelOverride ?? _configProvider.Get_Current().ImplementerModel;

        var command = kind switch
        {
            MemberKinds.Reviewer => SpawnCommand_Builder.Build_ForReviewer(orchId, memberId, session.RepoPath, model, pidFile),
            MemberKinds.Solo => SpawnCommand_Builder.Build_ForSolo(orchId, memberId, session.RepoPath, model, pidFile),
            MemberKinds.Implementer => SpawnCommand_Builder.Build_ForImplementer(orchId, memberId, session.RepoPath, model, pidFile),
            _ => throw new Exception($"Unhandled MemberKinds '{kind}' respawning '{memberId}' of '{orchId}'"),
        };

        _store.Set_MemberPid(orchId, memberId, null);
        Delete_StalePidFile_BestEffort(pidFile);

        _spawner.Spawn(command);
        Sync_TruePid_FromPidFile(pidFile, orchId, memberId, truePid => Store_MemberTruePid_IfStillOpen(orchId, memberId, truePid));

        _log.Log_Info(orchId, $"{kind} '{memberId}' session spawned");
    }

    public void Spawn_GeneralSupervisor()
    {
        GeneralChannel_Initializer.Ensure_Exists(_paths);

        // The general folder is the general supervisor's PERMANENT working directory: its
        // CLAUDE.md (persistent, machine-portable knowledge) auto-loads there, and --continue
        // resumes unambiguously because only general sessions ever run in it.
        var command = SpawnCommand_Builder.Build_ForGeneralSupervisor(
            _paths.GeneralFolder, _configProvider.Get_Current().GeneralSupervisorModel, _paths.GeneralPidFile);

        _spawner.Spawn(command);

        _log.Log_Info(ChannelDiscovery.GENERAL_ORCH_ID, "General supervisor session spawned (resume-if-possible)");
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
