namespace AIOrchestratorCoreLib.Telegram;

/// <summary>What the app should do with a topic's status line on this tick.</summary>
public enum TopicStatusActions
{
    /// <summary>Nothing to say, or nothing has changed. The common case, and it must cost nothing.</summary>
    None,

    /// <summary>No message exists in this topic yet.</summary>
    Post,

    /// <summary>A message exists: change it in place, so it never notifies and never scrolls away.</summary>
    Edit,
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
    /// </summary>
    public static bool Is_MessageGone(string errorMessage)
    {
        return errorMessage.Contains("message to edit not found", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("MESSAGE_ID_INVALID", StringComparison.OrdinalIgnoreCase);
    }

    public static TopicStatusActions Decide(string statusText, string? lastWrittenText, long? existingMessageId)
    {
        // NOTHING TO SAY, WITH A MESSAGE ALREADY UP, IS NOT NOTHING TO DO. Closing the last live
        // member leaves the line's last words standing — "imp-1  fix the parser  12 min", with a
        // duration that keeps reading, for a member that no longer exists. That is the wrong state
        // sitting in front of the owner, which is the exact thing they refused pinning to avoid.
        //
        // So an empty line with an existing message EDITS, and the caller writes the bare title. Only
        // an empty line with NO message is silence.
        if (string.IsNullOrWhiteSpace(statusText))
            return existingMessageId == null ? TopicStatusActions.None : TopicStatusActions.Edit;

        // An edit that writes the same text is a wasted API call, and against the 429 limit already
        // on the ledger it is a real cost rather than a tidiness point.
        if (lastWrittenText == statusText)
            return TopicStatusActions.None;

        if (existingMessageId == null)
            return TopicStatusActions.Post;

        return TopicStatusActions.Edit;
    }
}
