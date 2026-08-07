using AIOrchestratorCoreLib.Sessions.OrchestrationSession;

namespace AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;

/// <summary>
/// Creates and persists orchestration sessions under the supervision root.
/// All reads go to disk (the folder is shared with running agent sessions and the bridge).
/// </summary>
public interface IOrchestrationSessionStore
{
    IReadOnlyList<IOrchestrationSession> Load_All();
    IOrchestrationSession Get_Session(string orchId);
    IOrchestrationSession? Get_Session_OrNull(string orchId);
    IOrchestrationSession? Find_ByTelegramTopicId_OrNull(long topicId);

    IOrchestrationSession Create_Orchestration(string orchId, string repoName, string repoPath);
    IOrchestrationSession Add_Implementer(string orchId);

    void Set_TelegramTopicId(string orchId, long topicId);
    void Set_SupervisorPid(string orchId, int? pid);

    /// <summary>Stamps the communicator spawn (watchdog grace) — its pid lives only in its pid file.</summary>
    void Stamp_CommunicatorSpawned(string orchId);

    /// <summary>Per-topic silence: this orchestration's outbound Telegram traffic is DROPPED, not queued.</summary>
    void Set_TelegramSilenced(string orchId, bool silenced);
    void Set_MemberPid(string orchId, string memberId, int? pid);
    void Set_DisplayName(string orchId, string displayName);
    void Set_SupervisorModelOverride(string orchId, string? model);
    void Set_ImplementerModelOverride(string orchId, string? model);

    void Close_Member(string orchId, string memberId);
    void Close_Orchestration(string orchId);
}
