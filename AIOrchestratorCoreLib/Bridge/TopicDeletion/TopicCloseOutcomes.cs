namespace AIOrchestratorCoreLib.Bridge.TopicDeletion;

/// <summary>
/// What one <c>closeForumTopic</c> attempt told us — the <c>close</c> half of <c>topic.onClose</c>
/// (plan 03 Task 10). Four answers, but only two next steps, because a close carries none of the
/// delete's debt: every settled outcome ends the attempt, and <see cref="NotClosed"/> ends it too,
/// with a log line.
/// </summary>
public enum TopicCloseOutcomes
{
    /// <summary>Telegram accepted it. The topic is closed and still in the owner's list.</summary>
    Closed,

    /// <summary>
    /// The topic was ALREADY closed — Telegram answers a second close <c>TOPIC_NOT_MODIFIED</c>. That is
    /// the state we were trying to reach, and reading it as success is what makes closing twice
    /// idempotent rather than a warning every time the owner repeats themselves.
    /// </summary>
    AlreadyClosed,

    /// <summary>
    /// There is no such topic — the owner deleted it by hand. Nothing to close, and nothing to rename
    /// either, which is why the engine records it as deleted.
    /// </summary>
    AlreadyGone,

    /// <summary>
    /// Anything else — a refusal, a rate limit, no answer. The topic is merely still OPEN: its name
    /// already reads 🏁, and the next close attempt fixes it. See
    /// <see cref="TopicClose_Decider.MAXIMUM_ATTEMPTS"/> for why that earns no retry.
    /// </summary>
    NotClosed,
}
