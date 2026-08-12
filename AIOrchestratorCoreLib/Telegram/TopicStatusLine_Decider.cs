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
    public static TopicStatusActions Decide(string statusText, string? lastWrittenText, long? existingMessageId)
    {
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
