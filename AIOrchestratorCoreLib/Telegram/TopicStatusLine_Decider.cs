namespace AIOrchestratorCoreLib.Telegram;

/// <summary>What the app should do with a topic's status line on this tick.</summary>
public enum TopicStatusActions
{
    /// <summary>Nothing to say, or nothing has changed. The common case, and it must cost nothing.</summary>
    None,

    /// <summary>No message exists in this topic yet.</summary>
    Post,

    /// <summary>A message exists and is still where the owner is looking: change it in place, silently.</summary>
    Edit,

    /// <summary>
    /// The message exists but later traffic has buried it and the topic has gone quiet: DELETE it and
    /// post it again at the bottom. Telegram cannot MOVE a message, so this is the only way to put it
    /// back where the owner is looking.
    ///
    /// Appended rather than slotted next to None: the values are compared, never persisted, but a
    /// renumbering is a silent change to every stored or logged reading of them and buys nothing.
    ///
    /// It is the one status-line action that NOTIFIES, which is why it waits for two quiet minutes,
    /// obeys the delivery gate the POST obeys, and is never chosen while the line is already last.
    /// </summary>
    Repost,
}

/// <summary>
/// Post, edit, or do nothing — extracted from the engine so the three properties that define this
/// feature can be tested at all, rather than asserted in a commit message.
///
/// The one that has to be right is RESTART. The remembered text lives in memory and the message id
/// lives in session.json, so after a restart there is an id and no remembered text — which must EDIT
/// the existing message, not post beside it. A second status message appearing after every restart
/// is the defect this whole feature replaces, and it is invisible until someone restarts the app.
/// </summary>
public static class TopicStatusLine_Decider
{
    /// <summary>
    /// Telegram's answer when an edit would change nothing: the desired state already holds, so it is
    /// a SUCCESS. Lives HERE rather than in the engine because the engine is `internal sealed` with no
    /// InternalsVisibleTo — a finder deleted three of its guards at once and the suite stayed green at
    /// 610. It is not untested, it is unreachable, so anything that can be a pure function must be one.
    /// </summary>
    public static bool Is_MessageAlreadyCurrent(string errorMessage)
    {
        return errorMessage.Contains("message is not modified", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The message this id names is GONE — the topic was torn down and recreated while the id
    /// survived. Terminal for that id.
    ///
    /// "message can't be edited" is deliberately NOT here: it means the message EXISTS and is not
    /// editable, so clearing the id would post a second line while the frozen one stayed up — two
    /// status lines in one topic, which is the defect this feature exists to prevent, through another
    /// door. That case falls to the backoff instead.
    ///
    /// "message to delete not found" is Telegram's wording for the same state on the REPOST path,
    /// which deletes before it sends. Without it, an owner who deleted the status line by hand left a
    /// topic reposting into a delete that can never succeed, once per backoff period forever — the
    /// edit path that clears the id is never reached there, because the text of a quiet orchestration
    /// never changes.
    /// </summary>
    public static bool Is_MessageGone(string errorMessage)
    {
        return errorMessage.Contains("message to edit not found", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("message to delete not found", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("MESSAGE_ID_INVALID", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The delete was REFUSED, not failed — the message is still there and always will be, so
    /// retrying can only loop. Two triggers, and the second is the one that matters: Telegram will
    /// not delete a message past its 48-HOUR window, which needs no missing permission at all and is
    /// exactly the fate of a buried status line on a quiet orchestration; and the bot may lack
    /// `can_delete_messages`, which persists until an admin changes it.
    ///
    /// DELIBERATELY NOT PART OF <see cref="Is_MessageGone"/>. The message EXISTS: clearing the id
    /// would post a second line beside an undeletable one, which is the two-status-lines defect
    /// through a third door — the identical unsoundness that keeps "message can't be edited" out of
    /// that predicate. A refusal instead LATCHES the topic: it stops trying to move its line and
    /// keeps editing in place, which is the behaviour that existed before the repost was added.
    /// </summary>
    public static bool Is_DeleteRefused(string errorMessage)
    {
        return errorMessage.Contains("message can't be deleted", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("message can not be deleted", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("message cannot be deleted", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("not enough rights", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("CHAT_ADMIN_REQUIRED", StringComparison.OrdinalIgnoreCase);
    }

    public static TopicStatusActions Decide(string statusText, string? lastWrittenText, long? existingMessageId)
    {
        // ONE RULE, no empty special case. Emptiness reaching here means there is genuinely nothing
        // to say AND no message up — the builder emits the bare title instead when one is posted, so
        // the case "empty with a message" is not producible and is not special-cased.
        //
        // An earlier version handled it here and short-circuited ABOVE the identical-text check, so a
        // stable emptied orchestration compared empty against a cached title forever and rejected an
        // edit every two seconds. The decision must be made on the text that is actually sent.
        if (string.IsNullOrWhiteSpace(statusText))
            return TopicStatusActions.None;

        // An edit that writes the same text is a wasted API call, and against the 429 limit already
        // on the ledger it is a real cost rather than a tidiness point.
        if (lastWrittenText == statusText)
            return TopicStatusActions.None;

        if (existingMessageId == null)
            return TopicStatusActions.Post;

        return TopicStatusActions.Edit;
    }
}
