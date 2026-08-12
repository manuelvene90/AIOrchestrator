using AIOrchestratorCoreLib.Sessions.OrchestrationMember;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;

internal sealed class OrchestrationSessionStoreModel(ISupervisionPaths paths) : IOrchestrationSessionStore
{

    readonly ISupervisionPaths _paths = paths;
    readonly Lock _writeLock = new();

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
        var sessionFile = _paths.Get_SessionFile(orchId);

        if (!File.Exists(sessionFile))
            return null;

        return SessionJson_Serializer.Deserialize(File.ReadAllText(sessionFile), sessionFile);
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
            members.Add(OrchestrationMember_Factory.Create(memberId, null, null));

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
                    members.Add(OrchestrationMember_Factory.Create(memberId, pid, DateTime.UtcNow));
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
    }
}
