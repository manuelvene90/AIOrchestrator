using AIOrchestratorCoreLib.Sessions.OrchestrationMember;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Sessions.OrchestrationSession;

public static class OrchestrationSession_Factory
{
    public static IOrchestrationSession Create(
        string orchId,
        string repoName,
        string repoPath,
        DateTime createdUtc,
        long? telegramTopicId,
        int? supervisorPid,
        IReadOnlyList<IOrchestrationMember> members)
    {
        return Create(orchId, repoName, repoPath, createdUtc, telegramTopicId, supervisorPid, null, null, null, null, null, members, TelegramDeliveryModes.Normal, null);
    }

    public static IOrchestrationSession Create(
        string orchId,
        string repoName,
        string repoPath,
        DateTime createdUtc,
        long? telegramTopicId,
        int? supervisorPid,
        DateTime? supervisorSpawnedUtc,
        DateTime? communicatorSpawnedUtc,
        string? displayName,
        string? supervisorModelOverride,
        string? implementerModelOverride,
        IReadOnlyList<IOrchestrationMember> members,
        TelegramDeliveryModes telegramMode,
        DateTime? closedUtc,
        long? statusLineMessageId = null,
        OwnerPresenceModes ownerPresence = OwnerPresenceModes.Remote,
        bool awaitingTest = false,
        bool done = false)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"OrchId must be non-empty (repo '{repoName}' at '{repoPath}')");

        return new OrchestrationSessionModel(
            orchId, repoName, repoPath, createdUtc, telegramTopicId, supervisorPid, supervisorSpawnedUtc,
            communicatorSpawnedUtc, displayName, supervisorModelOverride, implementerModelOverride, members,
            telegramMode, ownerPresence, closedUtc, statusLineMessageId, awaitingTest, done);
    }

    /// <summary>
    /// The status message id, once the app has posted it. Persisted so a RESTART edits that
    /// message instead of posting a second one — which is the defect this whole feature replaces.
    ///
    /// It carries a wasSet flag because it must be resettable to NULL: `/clear` tears the topic
    /// down and recreates it, and a stale id then points at a message that no longer exists — the
    /// orchestration would never get a status line again. Null cannot mean both "unchanged" and
    /// "cleared", which is what this file's own docstring says and what I did not do.
    /// </summary>
    /// <summary>
    /// Forgets the status message — used when the topic it lived in no longer exists, so the next
    /// tick POSTS a fresh one instead of editing into a void forever.
    /// </summary>
    public static IOrchestrationSession CreateFrom_Existing_WithoutStatusLineMessageId(IOrchestrationSession existing)
    {
        return CreateFrom_Existing(existing, statusLineMessageId: null, statusLineMessageIdWasSet: true);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithStatusLineMessageId(IOrchestrationSession existing, long messageId)
    {
        return CreateFrom_Existing(existing, statusLineMessageId: messageId, statusLineMessageIdWasSet: true);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithTopicId(IOrchestrationSession existing, long topicId)
    {
        return CreateFrom_Existing(existing, telegramTopicId: topicId);
    }

    /// <summary>Also stamps SupervisorSpawnedUtc — the pid change IS a spawn (watchdog grace source).</summary>
    public static IOrchestrationSession CreateFrom_Existing_WithSupervisorPid(IOrchestrationSession existing, int? pid)
    {
        return CreateFrom_Existing(existing, supervisorPid: pid, supervisorSpawnedUtc: DateTime.UtcNow, supervisorPidWasSet: true);
    }

    /// <summary>Stamps the communicator spawn grace — its pid lives only in its pid file (the liveness source).</summary>
    public static IOrchestrationSession CreateFrom_Existing_WithCommunicatorSpawnedNow(IOrchestrationSession existing)
    {
        return CreateFrom_Existing(existing, communicatorSpawnedUtc: DateTime.UtcNow);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithDisplayName(IOrchestrationSession existing, string displayName)
    {
        return CreateFrom_Existing(existing, displayName: displayName);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithSupervisorModelOverride(IOrchestrationSession existing, string? model)
    {
        return CreateFrom_Existing(existing, supervisorModelOverride: model, supervisorModelWasSet: true);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithImplementerModelOverride(IOrchestrationSession existing, string? model)
    {
        return CreateFrom_Existing(existing, implementerModelOverride: model, implementerModelWasSet: true);
    }

    public static IOrchestrationSession CreateFrom_Existing_WithMembers(
        IOrchestrationSession existing,
        IReadOnlyList<IOrchestrationMember> members)
    {
        return CreateFrom_Existing(existing, members: members);
    }

    /// <summary>Per-topic delivery mode — overrides the app-wide setting unless it is Normal.</summary>
    public static IOrchestrationSession CreateFrom_Existing_WithTelegramMode(IOrchestrationSession existing, TelegramDeliveryModes mode)
    {
        return CreateFrom_Existing(existing, telegramMode: mode);
    }

    /// <summary>
    /// Finished-but-untested. Orthogonal to the delivery mode, which /test sets to Silenced beside
    /// this — see IOrchestrationSession.AwaitingTest for why it is a flag and not a fourth mode.
    /// </summary>
    public static IOrchestrationSession CreateFrom_Existing_WithAwaitingTest(IOrchestrationSession existing, bool awaitingTest)
    {
        return CreateFrom_Existing(existing, awaitingTest: awaitingTest, awaitingTestWasSet: true);
    }

    /// <summary>
    /// FINISHED AND LEFT OPEN. Like the awaiting-test flag beside it, /done also sets the delivery
    /// mode to Silenced — but as a separate statement, so that un-muting a done topic does not
    /// silently declare it unfinished, and finishing a topic does not lose what the owner had set.
    /// </summary>
    public static IOrchestrationSession CreateFrom_Existing_WithDone(IOrchestrationSession existing, bool done)
    {
        return CreateFrom_Existing(existing, done: done, doneWasSet: true);
    }

    /// <summary>Where the owner IS — orthogonal to the delivery mode, which stays as they set it.</summary>
    public static IOrchestrationSession CreateFrom_Existing_WithOwnerPresence(IOrchestrationSession existing, OwnerPresenceModes presence)
    {
        return CreateFrom_Existing(existing, ownerPresence: presence);
    }

    public static IOrchestrationSession CreateFrom_Existing_Closed(IOrchestrationSession existing, DateTime closedUtc)
    {
        return CreateFrom_Existing(existing, closedUtc: closedUtc);
    }

    /// <summary>
    /// One copy-with-overrides in place of a full argument list per mutation — the repeated lists
    /// were where a newly added field silently got dropped. Nullable overrides that may legitimately
    /// be set BACK to null carry an explicit "wasSet" flag, since null cannot mean both "unchanged"
    /// and "cleared".
    /// </summary>
    static IOrchestrationSession CreateFrom_Existing(
        IOrchestrationSession existing,
        long? telegramTopicId = null,
        int? supervisorPid = null,
        bool supervisorPidWasSet = false,
        DateTime? supervisorSpawnedUtc = null,
        DateTime? communicatorSpawnedUtc = null,
        string? displayName = null,
        string? supervisorModelOverride = null,
        bool supervisorModelWasSet = false,
        string? implementerModelOverride = null,
        bool implementerModelWasSet = false,
        IReadOnlyList<IOrchestrationMember>? members = null,
        TelegramDeliveryModes? telegramMode = null,
        OwnerPresenceModes? ownerPresence = null,
        DateTime? closedUtc = null,
        long? statusLineMessageId = null,
        bool statusLineMessageIdWasSet = false,
        bool awaitingTest = false,

        // A bool cannot say "unchanged" on its own, so it carries the same wasSet flag the nullable
        // overrides above use — without it, every unrelated copy would silently clear this.
        bool awaitingTestWasSet = false,
        bool done = false,

        // Same wasSet dance as awaitingTest, and for the same reason: a bare bool cannot say
        // "leave this alone", so without it every unrelated copy would quietly un-finish the topic.
        bool doneWasSet = false)
    {
        return Create(
            existing.OrchId,
            existing.RepoName,
            existing.RepoPath,
            existing.CreatedUtc,
            telegramTopicId ?? existing.TelegramTopicId,
            supervisorPidWasSet ? supervisorPid : existing.SupervisorPid,
            supervisorSpawnedUtc ?? existing.SupervisorSpawnedUtc,
            communicatorSpawnedUtc ?? existing.CommunicatorSpawnedUtc,
            displayName ?? existing.DisplayName,
            supervisorModelWasSet ? supervisorModelOverride : existing.SupervisorModelOverride,
            implementerModelWasSet ? implementerModelOverride : existing.ImplementerModelOverride,
            members ?? existing.Members,
            telegramMode ?? existing.TelegramMode,
            closedUtc ?? existing.ClosedUtc,
            statusLineMessageIdWasSet ? statusLineMessageId : existing.StatusLineMessageId,
            ownerPresence ?? existing.OwnerPresence,
            awaitingTestWasSet ? awaitingTest : existing.AwaitingTest,
            doneWasSet ? done : existing.Done);
    }
}
