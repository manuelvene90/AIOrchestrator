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

    /// <summary>Adds a member of the given KIND (imp-N or rev-N); the id carries the kind everywhere.</summary>
    IOrchestrationSession Add_Member(string orchId, MemberKinds kind);

    void Set_TelegramTopicId(string orchId, long topicId);

    /// <summary>Remembers the topic's one status message, so a restart edits rather than re-posts.</summary>
    void Set_StatusLineMessageId(string orchId, long messageId);

    /// <summary>Forgets the status message, so the next tick posts a fresh one.</summary>
    void Clear_StatusLineMessageId(string orchId);
    void Set_SupervisorPid(string orchId, int? pid);

    /// <summary>Stamps the communicator spawn (watchdog grace) — its pid lives only in its pid file.</summary>
    void Stamp_CommunicatorSpawned(string orchId);

    /// <summary>Per-topic delivery mode (Normal / Deferred = queued / Silenced = dropped).</summary>
    void Set_TelegramMode(string orchId, Telegram.TelegramDeliveryModes mode);

    /// <summary>Finished but untested — the owner's reminder not to close it yet.</summary>
    void Set_AwaitingTest(string orchId, bool awaitingTest);

    /// <summary>Where the owner is for this orchestration — see IOrchestrationSession.OwnerPresence.</summary>
    void Set_OwnerPresence(string orchId, Telegram.OwnerPresenceModes presence);
    void Set_MemberPid(string orchId, string memberId, int? pid);
    void Set_DisplayName(string orchId, string displayName);
    void Set_SupervisorModelOverride(string orchId, string? model);
    void Set_ImplementerModelOverride(string orchId, string? model);

    void Close_Member(string orchId, string memberId);
    void Close_Orchestration(string orchId);
}
