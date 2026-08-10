namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// Decides what actually reaches the owner's PHONE. Everything the supervisor writes still lands in
/// owner-channel.md and is readable in the app — this only governs the push.
///
/// The owner, after a transcript of running commentary: "I answer the sup a question, and then the
/// sup doesn't disturb me anymore unless it has another question. A brief every 30 minutes about
/// how the work is going is fine, but not the waterfall of messages I get now."
///
/// So the phone gets exactly three things:
///   - a QUESTION (it needs them to decide, and it stops the conversation until they do),
///   - the ANSWER to something they asked (they are waiting for it),
///   - a BLOCKED flag (work has stopped and only they can restart it).
/// Progress narration is not one of them. It is not lost; it is simply not a notification.
/// </summary>
public static class OwnerPush_Policy
{
    /// <summary>Written by the supervisor when it needs a decision — rendered as tappable buttons.</summary>
    public const string QUESTION_MARKER = "QUESTION:";
    public const string OPTION_MARKER = "OPTION:";

    /// <summary>Work has stopped and only the owner can restart it.</summary>
    public const string BLOCKED_MARKER = "BLOCKED ON OWNER";

    /// <summary>
    /// ownerIsWaitingForAReply: the owner sent something the supervisor has not answered yet, so
    /// THIS entry is that answer and must go through whatever else it contains.
    /// </summary>
    public static bool Should_Push(string rawEntryText, bool ownerIsWaitingForAReply)
    {
        if (ownerIsWaitingForAReply)
            return true;

        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        return Carries_Question(rawEntryText)
            || rawEntryText.Contains(BLOCKED_MARKER, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Carries_Question(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        return rawEntryText.Contains(QUESTION_MARKER, StringComparison.Ordinal)
            || rawEntryText.Contains(OPTION_MARKER, StringComparison.Ordinal);
    }
}
