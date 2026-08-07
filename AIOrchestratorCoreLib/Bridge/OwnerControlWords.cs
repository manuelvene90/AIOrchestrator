namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// The owner's hold/release words for message aggregation.
///
/// WHY: most messages arrive alone, so the aggregation window can be short — but a short window
/// cuts a multi-message thought in half and delivers it to the session as two separate turns.
/// `WAIT` holds delivery until `GO`, which lets the default window be short for the common case
/// without penalising the case where the owner is still typing.
///
/// A control word must be the ENTIRE message (trimmed, any capitalisation, optional trailing
/// punctuation). "wait for imp-2 to finish" is a real instruction to the supervisor and must reach
/// it verbatim — matching anywhere inside the text would silently swallow content, which is the
/// worst failure this system can have.
/// </summary>
public static class OwnerControlWords
{
    public const string WAIT = "wait";
    public const string GO = "go";

    public static bool Is_Wait(string messageText)
    {
        return Matches(messageText, WAIT);
    }

    public static bool Is_Go(string messageText)
    {
        return Matches(messageText, GO);
    }

    static bool Matches(string messageText, string word)
    {
        if (messageText == null)
            return false;

        var trimmed = messageText.Trim().TrimEnd('.', '!', '?', ',', ';', ':');

        return trimmed.Equals(word, StringComparison.OrdinalIgnoreCase);
    }
}
