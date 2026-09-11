namespace AIOrchestratorCoreLib.Bridge.TopicDeletion;

/// <summary>What one <c>deleteForumTopic</c> attempt told us — the four answers that lead to
/// four different next steps, and the distinction the fire-and-forget delete never made.</summary>
public enum TopicDeleteOutcomes
{
    /// <summary>Telegram accepted it. The topic is gone; record the fact and stop.</summary>
    Deleted,

    /// <summary>
    /// The topic is ALREADY gone — the owner deleted it from their phone, or an earlier attempt
    /// landed and this process never learned. Indistinguishable from success in every way that
    /// matters, and treated as success on purpose: the goal is the topic's absence, not our having
    /// been the one to cause it. Read as a failure it would be retried for ever against a thread id
    /// that can never answer anything else.
    /// </summary>
    AlreadyGone,

    /// <summary>
    /// Telegram REFUSED on the merits and will keep refusing until something outside this app
    /// changes — the bot lost <c>can_delete_messages</c>, or was demoted. Retrying inside the
    /// process can only loop, so the owner is told once and the attempt is left for the next start
    /// (where a restored permission is picked up at no cost).
    /// </summary>
    Refused,

    /// <summary>
    /// Nobody knows. A 429, a 5xx, a dropped connection, a timeout: the request may or may not have
    /// taken effect. Worth retrying now with backoff, and worth carrying across a restart as PENDING
    /// if the in-process attempts run out — which is the orphan this whole component exists to stop.
    /// </summary>
    OutcomeUnknown,
}
