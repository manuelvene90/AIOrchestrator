using AIOrchestratorCoreLib.Sessions.OrchestrationMember;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;

internal sealed class OrchestrationSessionStoreModel(ISupervisionPaths paths) : IOrchestrationSessionStore
{
    const string IMPLEMENTER_ID_PREFIX = "imp-";

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
        lock (_writeLock)
        {
            var session = Get_Session(orchId);

            var memberId = $"{IMPLEMENTER_ID_PREFIX}{Get_NextImplementerNumber(session)}";

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

    public void Set_SupervisorPid(string orchId, int? pid)
    {
        lock (_writeLock)
        {
            var session = Get_Session(orchId);
            Save(OrchestrationSession_Factory.CreateFrom_Existing_WithSupervisorPid(session, pid));
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

    static int Get_NextImplementerNumber(IOrchestrationSession session)
    {
        var maxNumber = 0;

        foreach (var member in session.Members)
        {
            var numberPart = member.MemberId.Replace(IMPLEMENTER_ID_PREFIX, string.Empty);

            if (int.TryParse(numberPart, out var number) && number > maxNumber)
                maxNumber = number;
        }

        return maxNumber + 1;
    }

    void Save(IOrchestrationSession session)
    {
        File.WriteAllText(_paths.Get_SessionFile(session.OrchId), SessionJson_Serializer.Serialize(session));
    }
}
