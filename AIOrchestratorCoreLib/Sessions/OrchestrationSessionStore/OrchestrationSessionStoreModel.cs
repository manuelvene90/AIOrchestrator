using AIOrchestratorCoreLib.Sessions.OrchestrationMember;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;

internal sealed class OrchestrationSessionStoreModel(ISupervisionPaths paths) : IOrchestrationSessionStore
{

    readonly ISupervisionPaths _paths = paths;
    readonly Lock _writeLock = new();

    /// <summary>
    /// The last parse of each <c>session.json</c>, WITH THE STAMP IT WAS PARSED FROM — used only
    /// while the file on disk still has exactly that length and last-write time.
    ///
    /// <para>
    /// WHY: <c>Load_All</c> is called about twenty-six times inside one 2-second mirror tick — from
    /// the engine's sweeps, the watchdog and the print dispatcher — and every call re-read and
    /// re-deserialised every orchestration's session.json. The answers were identical; the reading
    /// was not free.
    /// </para>
    /// <para>
    /// IT IS NOT A WRITE-BEHIND CACHE AND HOLDS NO UNSAVED STATE. <see cref="Save"/> writes the file
    /// first and then FORGETS the entry, so the next read comes from disk. That order is what keeps
    /// this invisible: a reader can never be handed a session that differs from the bytes on disk.
    /// </para>
    /// <para>
    /// INVALIDATED BY THE FILE, NOT BY A CLOCK, so there is no window in which a stale roster, pid
    /// or <c>ClosedUtc</c> can be served — the failure mode that would matter here, since a stale
    /// <c>ClosedUtc</c> is how a closed member gets resurrected (see <see cref="Set_MemberPid"/>).
    /// The stamp pair is belt and braces: every save through <see cref="Storage.Atomic_FileWriter"/>
    /// moves a fresh file into place, and this store is the only writer of these files in the system.
    /// </para>
    /// </summary>
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, ((long Length, DateTime LastWriteUtc) Stamp, IOrchestrationSession Session)> _parsedByOrchId = new();

    public IReadOnlyList<IOrchestrationSession> Load_All()
    {
        List<IOrchestrationSession> sessions = [];

        if (!Directory.Exists(_paths.Root))
            return sessions;

        foreach (var orchFolder in Directory.EnumerateDirectories(_paths.Root))
        {
            var orchId = Path.GetFileName(orchFolder);
            var session = Get_Session_OrNull(orchId);

            if (session != null)
                sessions.Add(session);
        }

        return sessions;
    }

    public IOrchestrationSession Get_Session(string orchId)
    {
        return Get_Session_OrNull(orchId)
            ?? throw new Exception($"No session.json found for orchestration '{orchId}' under '{_paths.Root}'");
    }

    public IOrchestrationSession? Get_Session_OrNull(string orchId)
    {
        var sessionFile = new FileInfo(_paths.Get_SessionFile(orchId));

        // NOT REMEMBERED AS ABSENT: a file that does not exist yet has no stamp to invalidate
        // against, and the stat this needs is already the cheapest question that can be asked here.
        if (!sessionFile.Exists)
            return null;

        var stamp = (sessionFile.Length, sessionFile.LastWriteTimeUtc);

        if (_parsedByOrchId.TryGetValue(orchId, out var parsed) && parsed.Stamp == stamp)
            return parsed.Session;

        Diagnostics.TickIo_Counters.Count_SessionFileRead();

        // A file that cannot be deserialised THROWS, exactly as before, and nothing is remembered —
        // so a session.json repaired on disk is picked up by the next call rather than by a restart.
        var session = SessionJson_Serializer.Deserialize(File.ReadAllText(sessionFile.FullName), sessionFile.FullName);

        _parsedByOrchId[orchId] = (stamp, session);

        return session;
    }

    public IOrchestrationSession? Find_ByTelegramTopicId_OrNull(long topicId)
    {
        foreach (var session in Load_All())
        {
            if (session.TelegramTopicId == topicId)
                return session;
        }

        return null;
    }

    public IOrchestrationSession Create_Orchestration(string orchId, string repoName, string repoPath)
    {
        lock (_writeLock)
        {
            if (Get_Session_OrNull(orchId) != null)
                throw new Exception($"Orchestration '{orchId}' already exists under '{_paths.Root}'");

            Directory.CreateDirectory(_paths.Get_OrchestrationFolder(orchId));

            var ownerChannelFile = _paths.Get_OwnerChannelFile(orchId);
            if (!File.Exists(ownerChannelFile))
                File.WriteAllText(ownerChannelFile, ChannelSeed_Builder.Build_OwnerChannelSeed(orchId));

            var session = OrchestrationSession_Factory.Create(
                orchId, repoName, repoPath, DateTime.UtcNow, null, null, []);

            Save(session);
            return session;
        }
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
        lock (_writeLock)
        {
            var session = Get_Session(orchId);

            var prefix = MemberKind_Ids.Build_Prefix(kind);
            var memberId = $"{prefix}{Get_NextMemberNumber(session, prefix)}";

            Directory.CreateDirectory(_paths.Get_ImplementerFolder(orchId, memberId));

            var channelFile = _paths.Get_ImplementerChannelFile(orchId, memberId);
            if (!File.Exists(channelFile))
                File.WriteAllText(channelFile, ChannelSeed_Builder.Build_ImplementerChannelSeed(orchId, memberId));

            List<IOrchestrationMember> members = [.. session.Members];
            members.Add(OrchestrationMember_Factory.Create(memberId, null, null, null, model));

            var updated = OrchestrationSession_Factory.CreateFrom_Existing_WithMembers(session, members);
            Save(updated);
            return updated;
        }
    }

    public void Set_TelegramTopicId(string orchId, long topicId)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithTopicId(session, topicId));
        }
    }

    public void Set_StatusLineMessageId(string orchId, long messageId)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithStatusLineMessageId(session, messageId));
        }
    }

    public void Clear_StatusLineMessageId(string orchId)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithoutStatusLineMessageId(session));
        }
    }

    public void Set_SupervisorPid(string orchId, int? pid)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithSupervisorPid(session, pid));
        }
    }

    /// <summary>
    /// Clears the supervisor stamp and pid, taking the orchestration back to BASIC. The one write
    /// that can undo a promotion — see OrchestrationSession_Factory.CreateFrom_Existing_WithoutSupervisor.
    /// </summary>
    public void Clear_Supervisor(string orchId)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithoutSupervisor(session));
        }
    }

    public void Stamp_CommunicatorSpawned(string orchId)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithCommunicatorSpawnedNow(session));
        }
    }

    public void Set_TelegramMode(string orchId, Telegram.TelegramDeliveryModes mode)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithTelegramMode(session, mode));
        }
    }

    public void Set_AwaitingTest(string orchId, bool awaitingTest)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithAwaitingTest(session, awaitingTest));
        }
    }

    public void Set_Done(string orchId, bool done)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithDone(session, done));
        }
    }

    public void Set_OwnerPresence(string orchId, Telegram.OwnerPresenceModes presence)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithOwnerPresence(session, presence));
        }
    }

    public void Set_MemberPid(string orchId, string memberId, int? pid)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);

            List<IOrchestrationMember> members = [];
            var found = false;

            foreach (var member in session.Members)
            {
                if (member.MemberId == memberId)
                {
                    // CLOSED STAYS CLOSED. This called the three-argument overload, which forwards
                    // `closedUtc: null` — so writing a pid silently RE-OPENED a retired member, and
                    // `Respawn_Implementer`'s first act is exactly this write. A watchdog tick holding
                    // a snapshot from before a close could therefore resurrect the member it was
                    // racing, permanently, with nothing to heal it.
                    //
                    // The codebase already knew this hazard and guarded the LATER write —
                    // `Store_MemberTruePid_IfStillOpen`, "a member closed during the sync window must
                    // stay closed". The guard was simply never applied to the earlier one. Fixed here
                    // rather than at that call site so it holds for every caller of this store method,
                    // present and future: setting a pid is not a statement about whether a member is
                    // open.
                    members.Add(OrchestrationMember_Factory.Create(memberId, pid, DateTime.UtcNow, member.ClosedUtc, member.Model));
                    found = true;
                }
                else
                {
                    members.Add(member);
                }
            }

            if (!found)
                throw new Exception($"Member '{memberId}' not found in orchestration '{orchId}' (members: {string.Join(", ", session.Members.Select(m => m.MemberId))})");

            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithMembers(session, members));
        }
    }

    public void Set_DisplayName(string orchId, string displayName)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithDisplayName(session, displayName));
        }
    }

    public void Set_SupervisorModelOverride(string orchId, string? model)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithSupervisorModelOverride(session, model));
        }
    }

    public void Set_ImplementerModelOverride(string orchId, string? model)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithImplementerModelOverride(session, model));
        }
    }

    public void Close_Member(string orchId, string memberId)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);

            List<IOrchestrationMember> members = [];
            var found = false;

            foreach (var member in session.Members)
            {
                if (member.MemberId == memberId)
                {
                    members.Add(OrchestrationMember_Factory.CreateFrom_Existing_Closed(member, DateTime.UtcNow));
                    found = true;
                }
                else
                {
                    members.Add(member);
                }
            }

            if (!found)
                throw new Exception($"Member '{memberId}' not found in orchestration '{orchId}' (members: {string.Join(", ", session.Members.Select(m => m.MemberId))})");

            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithMembers(session, members));
        }
    }

    public void Mark_TopicDeletePending(string orchId)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithTopicDeletePending(session, DateTime.UtcNow));
        }
    }

    public void Mark_TopicDeleted(string orchId)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithTopicDeleted(session, DateTime.UtcNow));
        }
    }

    public void Mark_TopicDeleteFailureReported(string orchId)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithTopicDeleteFailureReported(session));
        }
    }

    public void Close_Orchestration(string orchId)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_Closed(session, DateTime.UtcNow));
        }
    }

    /// <summary>Each kind numbers independently, so an orchestration reads as "imp-1, imp-2, rev-1".</summary>
    static int Get_NextMemberNumber(IOrchestrationSession session, string prefix)
    {
        var maxNumber = 0;

        foreach (var member in session.Members)
        {
            if (!member.MemberId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var numberPart = member.MemberId[prefix.Length..];

            if (int.TryParse(numberPart, out var number) && number > maxNumber)
                maxNumber = number;
        }

        return maxNumber + 1;
    }

    /// <summary>
    /// ATOMIC, because nothing regenerates this file. session.json holds the repo path, the member
    /// roster WITH their pids, the Telegram topic id, the model overrides and ClosedUtc — a
    /// truncated write makes the orchestration disappear from the card list, stops the watchdog
    /// respawning it, strands its processes beyond Kill_OrchestrationSessions' reach and loses the
    /// topic id needed to clean up its Telegram side.
    ///
    /// It is written at the worst possible moment too: closing an orchestration saves this file
    /// immediately before tree-killing the very processes whose pids it records. This is the shape
    /// commit 3a0f8a2 introduced Atomic_FileWriter for — a write that fails must not destroy the
    /// file it was updating.
    /// </summary>
    void Save(IOrchestrationSession session)
    {
        Atomic_FileWriter.Write_AllText(_paths.Get_SessionFile(session.OrchId), SessionJson_Serializer.Serialize(session));

        // THE BELT, not the mechanism. The stamp check is what makes a stale parse unusable, and it
        // already covers this write from either side — a reader racing this call sees the old file
        // with its old stamp or the new file with its new one, and both are the truth on disk at the
        // moment they looked. This forget covers the one case a stamp cannot: a replacement that
        // lands on the SAME length and the same last-write time, which is what the filesystem's
        // timestamp granularity makes conceivable for a small file rewritten twice in a row. It
        // costs a dictionary removal on a write that happens a few times an hour.
        _parsedByOrchId.TryRemove(session.OrchId, out _);
    }
}
