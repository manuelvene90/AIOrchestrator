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
        bool done = false,
        DateTime? telegramTopicDeletePendingUtc = null,
        DateTime? telegramTopicDeletedUtc = null,
        bool telegramTopicDeleteFailureReported = false)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"OrchId must be non-empty (repo '{repoName}' at '{repoPath}')");

        return new OrchestrationSessionModel(
            orchId, repoName, repoPath, createdUtc, telegramTopicId, supervisorPid, supervisorSpawnedUtc,
            communicatorSpawnedUtc, displayName, supervisorModelOverride, implementerModelOverride, members,
            telegramMode, ownerPresence, closedUtc, statusLineMessageId, awaitingTest, done,
            telegramTopicDeletePendingUtc, telegramTopicDeletedUtc, telegramTopicDeleteFailureReported);
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
    /// <summary>
    /// TAKES THE ORCHESTRATION BACK TO BASIC — the only thing in this system that can, and for most
    /// of this repo's life nothing could.
    ///
    /// `SupervisorSpawnedUtc` IS the shape (OrchestrationShape.Is_BasicOrchestration), it is stamped
    /// before a supervisor spawn is attempted, and no path cleared it — including every close path.
    /// That is why the promotion prompt, the solo's role command and three code comments all called
    /// promotion one-way. The owner asked for the way back on 2026-08-25: *"with the same command I
    /// transform an orchestra into solo, and a solo into an orchestra."*
    ///
    /// The PID goes with the stamp, deliberately: leaving a supervisor pid behind on an orchestration
    /// that now has no supervisor slot would leave the watchdog holding a pid it must never respawn
    /// and the terminator holding one it has already killed.
    /// </summary>
    public static IOrchestrationSession CreateFrom_Existing_WithoutSupervisor(IOrchestrationSession existing)
    {
        return CreateFrom_Existing(
            existing,
            supervisorPid: null,
            supervisorPidWasSet: true,
            supervisorSpawnedUtc: null,
            supervisorSpawnedUtcWasSet: true);
    }

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

    /// <summary>
    /// A topic delete has been ASKED FOR and not yet confirmed — written before the first attempt,
    /// so a process that dies mid-retry still leaves the record the start-up sweep reads.
    /// </summary>
    public static IOrchestrationSession CreateFrom_Existing_WithTopicDeletePending(IOrchestrationSession existing, DateTime pendingUtc)
    {
        return CreateFrom_Existing(existing, telegramTopicDeletePendingUtc: pendingUtc);
    }

    /// <summary>The topic is gone — Telegram deleted it, or answered that no such thread exists.</summary>
    public static IOrchestrationSession CreateFrom_Existing_WithTopicDeleted(IOrchestrationSession existing, DateTime deletedUtc)
    {
        return CreateFrom_Existing(existing, telegramTopicDeletedUtc: deletedUtc);
    }

    /// <summary>The owner has been told once that this topic will not delete. Never unset.</summary>
    public static IOrchestrationSession CreateFrom_Existing_WithTopicDeleteFailureReported(IOrchestrationSession existing)
    {
        return CreateFrom_Existing(existing, telegramTopicDeleteFailureReported: true, telegramTopicDeleteFailureReportedWasSet: true);
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

        // The same wasSet dance the pid and the two bools use, and here it is load-bearing for the
        // SHAPE: null must be able to mean "cleared — this is basic again" and not only "unchanged".
        // Without it a demotion is unexpressible, which is precisely why there was no demotion.
        bool supervisorSpawnedUtcWasSet = false,
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
        bool doneWasSet = false,
        DateTime? telegramTopicDeletePendingUtc = null,
        DateTime? telegramTopicDeletedUtc = null,
        bool telegramTopicDeleteFailureReported = false,

        // The delete-failure alert is a bool that must be settable to TRUE and never silently back
        // to false, and the two stamps beside it are set once and never cleared — so only this one
        // needs the wasSet dance, and it needs it for the same reason `done` does.
        bool telegramTopicDeleteFailureReportedWasSet = false)
    {
        return Create(
            existing.OrchId,
            existing.RepoName,
            existing.RepoPath,
            existing.CreatedUtc,
            telegramTopicId ?? existing.TelegramTopicId,
            supervisorPidWasSet ? supervisorPid : existing.SupervisorPid,
            supervisorSpawnedUtcWasSet ? supervisorSpawnedUtc : supervisorSpawnedUtc ?? existing.SupervisorSpawnedUtc,
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
            doneWasSet ? done : existing.Done,
            telegramTopicDeletePendingUtc ?? existing.TelegramTopicDeletePendingUtc,
            telegramTopicDeletedUtc ?? existing.TelegramTopicDeletedUtc,
            telegramTopicDeleteFailureReportedWasSet ? telegramTopicDeleteFailureReported : existing.TelegramTopicDeleteFailureReported);
    }
}
