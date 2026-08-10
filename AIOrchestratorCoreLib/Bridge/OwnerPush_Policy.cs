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
            || Asks_InProse(rawEntryText)
            || rawEntryText.Contains(BLOCKED_MARKER, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A question asked WITHOUT the markers still has to reach the owner. Relying on the markers
    /// alone made this filter capable of swallowing a real question in prose — the supervisor asks,
    /// the owner never sees it, and both wait forever. That is the worst failure this component
    /// could have, and it outweighs the cost of the opposite mistake by a wide margin.
    ///
    /// So the rule is ASK-SHAPED MEANS PUSH, and the bias is deliberate: a false positive costs the
    /// owner one extra message they can ignore, a false negative costs them a deadlock they cannot
    /// see. The question mark is checked outside fenced blocks so a mockup or a snippet containing
    /// one is not mistaken for a question.
    /// </summary>
    public static bool Asks_InProse(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        var withoutBlocks = Strip_FencedBlocks(rawEntryText);

        return withoutBlocks.Contains('?');
    }

    static string Strip_FencedBlocks(string text)
    {
        var parts = text.Split("```");
        var kept = new System.Text.StringBuilder();

        // Even indices are outside fences, odd indices are inside them.
        for (var i = 0; i < parts.Length; i += 2)
            kept.Append(parts[i]);

        return kept.ToString();
    }

    /// <summary>
    /// Added to EVERY question automatically. A question on a phone is compressed to a couple of
    /// lines, so the owner regularly needs the reasoning behind it before they can choose — and
    /// without a button the only way to ask is to type, which defeats the point of tappable options.
    /// </summary>
    public const string MORE_DETAIL_LABEL = "❔ Explain the options";

    /// <summary>
    /// What the SUPERVISOR actually receives when that button is tapped. It is deliberately fuller
    /// than the label: the button is one tap, the instruction behind it has to be unambiguous, and
    /// it must end by re-asking so the decision is not left dangling.
    /// </summary>
    public const string MORE_DETAIL_REQUEST =
        "Explain this decision before I choose: what each option actually means in practice, what "
        + "differs between them, what it costs to get wrong, and which one you recommend and why. "
        + "Keep it short. Then ask the question again.";

    public static bool Carries_Question(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        return rawEntryText.Contains(QUESTION_MARKER, StringComparison.Ordinal)
            || rawEntryText.Contains(OPTION_MARKER, StringComparison.Ordinal);
    }
}
