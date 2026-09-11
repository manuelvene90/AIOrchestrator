using AIOrchestratorCoreLib.Sessions.OrchestrationSession;

namespace AIOrchestratorCoreLib.Bridge.TopicDeletion;

/// <summary>
/// WHICH TOPICS THIS START STILL OWES TELEGRAM A DELETE FOR.
///
/// <para>
/// The half of brief E1 that survives a restart. An in-process retry covers a rate limit and a short
/// outage; it cannot cover the app being closed, killed or crashed while a delete was still failing —
/// and that is the case that leaves an orphan topic for ever, because nothing else ever looks at a
/// closed orchestration again.
/// </para>
/// <para>
/// PENDING IS THE KEY, AND ITS ABSENCE IS LOAD-BEARING. Every orchestration closed before this
/// feature shipped has no pending stamp and no deleted stamp, and its topic was very probably
/// deleted successfully at the time. Selecting on "closed and not recorded deleted" would re-attempt
/// every one of them at the next start — a burst of deletes against thread ids that are already gone,
/// on an app whose rate limit is a real constraint. Selecting on the PENDING STAMP means only a
/// delete this feature actually started and did not finish is ever retried.
/// </para>
/// </summary>
public static class TopicDeleteSweep_Planner
{
    /// <summary>
    /// The orchestrations to retry, oldest pending first — so a backlog is worked through in the
    /// order it accumulated rather than in whatever order the store happened to load.
    /// </summary>
    public static IReadOnlyList<IOrchestrationSession> Select_PendingDeletes(IReadOnlyList<IOrchestrationSession> sessions)
    {
        List<IOrchestrationSession> pending = [];

        foreach (var session in sessions)
        {
            if (Needs_Delete(session))
                pending.Add(session);
        }

        pending.Sort((left, right) => Nullable.Compare(left.TelegramTopicDeletePendingUtc, right.TelegramTopicDeletePendingUtc));

        return pending;
    }

    /// <summary>
    /// A delete is owed when one was started, has not been recorded as landed, and there is still a
    /// thread id to aim at. The closed check is deliberately NOT here: the pending stamp is only ever
    /// written by the close path, so asking again would be a second copy of the same fact — and a
    /// second copy is how the two readings drift.
    /// </summary>
    public static bool Needs_Delete(IOrchestrationSession session)
    {
        return session.TelegramTopicId != null
            && session.TelegramTopicDeletePendingUtc != null
            && session.TelegramTopicDeletedUtc == null;
    }
}
