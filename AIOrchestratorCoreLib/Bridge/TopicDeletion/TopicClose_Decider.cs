using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge.TopicDeletion;

/// <summary>
/// WHAT A <c>closeForumTopic</c> TOLD US, AND WHY IT IS ASKED ONLY ONCE — the <c>close</c> value of
/// <c>topic.onClose</c> (plan 03 Task 10), beside <see cref="TopicDelete_Decider"/> because the two are
/// the two answers to one setting and the difference between them is the whole design.
///
/// <para>
/// THE DELETE GETS THE RETRY; THE CLOSE DOES NOT. <see cref="TopicDelete_Decider"/>'s bounded retry, the
/// pending stamp written before the ask and the start-up sweep all exist because a FAILED DELETE leaves
/// an orphan topic with no record anywhere that it should not be there (audit 2026-09-09, brief E1).
/// A FAILED CLOSE LEAVES A TOPIC THAT IS MERELY STILL OPEN: it is in the list either way — that is what
/// <c>close</c> chose — its name already reads 🏁 (the name sync renames a closed orchestration whose
/// topic was not deleted), and the next close attempt fixes it. Inheriting the delete's machinery would
/// buy retries, a debt record and a sweep for a state that owes nothing.
/// </para>
/// <para>
/// HERE RATHER THAN IN THE ENGINE for <see cref="TopicDelete_Decider"/>'s reason: <c>BridgeEngineModel</c>
/// is <c>internal sealed</c>, so a rule written inside it cannot be asserted by the suite.
/// </para>
/// </summary>
public static class TopicClose_Decider
{
    /// <summary>
    /// ONE. Not a retry budget that happens to be small — the statement that a close is not retried,
    /// for the reason the class summary gives, and a number the suite can compare against
    /// <see cref="TopicDelete_Decider.MAXIMUM_ATTEMPTS"/>.
    /// </summary>
    public const int MAXIMUM_ATTEMPTS = 1;

    /// <summary>
    /// Classified through <see cref="TelegramError_Table"/>, the one table this app reads Telegram's
    /// answers with, so a close and a rename cannot come to disagree about what TOPIC_NOT_MODIFIED or
    /// TOPIC_ID_INVALID means.
    ///
    /// <para>
    /// TOPIC_NOT_MODIFIED is the only answer a second close can get, and it is filed under the table's
    /// <see cref="TelegramErrorCases.TopicNameAlreadyCurrent"/> because the rename met it first; for a
    /// close it means "already closed".
    /// </para>
    /// </summary>
    public static TopicCloseOutcomes Classify(Exception? failure)
    {
        if (failure == null)
            return TopicCloseOutcomes.Closed;

        return TelegramError_Table.Classify(failure) switch
        {
            TelegramErrorCases.TopicNameAlreadyCurrent => TopicCloseOutcomes.AlreadyClosed,
            TelegramErrorCases.TopicGone => TopicCloseOutcomes.AlreadyGone,
            _ => TopicCloseOutcomes.NotClosed,
        };
    }

    /// <summary>Whether the topic is where <c>close</c> wanted it, or cannot be anywhere else.</summary>
    public static bool Is_Settled(TopicCloseOutcomes outcome)
    {
        return outcome is TopicCloseOutcomes.Closed or TopicCloseOutcomes.AlreadyClosed or TopicCloseOutcomes.AlreadyGone;
    }
}
