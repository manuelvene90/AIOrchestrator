using System.Text.RegularExpressions;

namespace AIOrchestratorCoreLib.Formatting;

/// <summary>
/// Builds the SHORT question that sits immediately above a set of decision buttons.
///
/// WHY THIS EXISTS: agents write long, thorough messages and then hang the options off the bottom
/// of them. On a phone that arrives as a wall of text with taps underneath and no visible question —
/// the owner has to reconstruct what is being asked before they can answer. So the buttons never
/// ride on the body any more: the body is sent as ordinary messages, and the buttons go on their
/// OWN short message carrying just the question.
///
/// The question comes from an explicit `QUESTION:` line when the agent wrote one (the role commands
/// require it). When it did not, one is derived from the body's last question sentence, and if even
/// that fails there is a canned prompt — the owner must never see naked buttons.
/// </summary>
public static class QuestionPrompt_Builder
{
    /// <summary>Beyond this a "question" is really a paragraph, and showing it defeats the purpose.</summary>
    public const int MAX_DERIVED_LENGTH = 200;

    /// <summary>Canned, so it stays English like every other app string (owner directive).</summary>
    public const string FALLBACK_PROMPT = "Your call:";

    public const string PREFIX = "❓ ";

    public static string Build(IReadOnlyList<string> questionLines, string bodyText)
    {
        var explicitQuestion = string.Join(" ", questionLines.Where(line => !string.IsNullOrWhiteSpace(line))).Trim();

        if (explicitQuestion.Length > 0)
            return $"{PREFIX}{explicitQuestion}";

        return $"{PREFIX}{Derive_OrNull(bodyText) ?? FALLBACK_PROMPT}";
    }

    /// <summary>
    /// The last sentence of the body that ends in '?'. Anything longer than MAX_DERIVED_LENGTH is
    /// rejected rather than truncated — half a question is worse than the canned prompt.
    /// </summary>
    public static string? Derive_OrNull(string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
            return null;

        // Fenced blocks (mockups, snippets) are not prose and must never be mined for a question.
        var (withoutBlocks, _) = MonospaceBlocks_Formatter.Extract_Blocks(bodyText);

        var lastMark = withoutBlocks.LastIndexOf('?');

        if (lastMark < 0)
            return null;

        var start = 0;

        for (var i = lastMark - 1; i >= 0; i--)
        {
            var character = withoutBlocks[i];

            if (character == '.' || character == '!' || character == '?' || character == '\n')
            {
                start = i + 1;
                break;
            }
        }

        var sentence = withoutBlocks[start..(lastMark + 1)].Trim();

        // A speaker prefix ("🔴 Sup: ") landing at the front of the first sentence is chrome.
        sentence = Regex.Replace(sentence, @"^[^\s]{1,3}\s*[A-Za-z-]{2,8}:\s*", "");
        sentence = sentence.Trim();

        if (sentence.Length == 0 || sentence.Length > MAX_DERIVED_LENGTH)
            return null;

        return sentence;
    }

    /// <summary>
    /// The question message after the owner has tapped: the question stays visible, with the choice
    /// under it. Telegram's tap toast is transient and the keyboard disappears, so without this the
    /// chat keeps no record of WHAT was chosen.
    /// </summary>
    public static string Build_AnsweredText(string questionText, string chosenLabel)
    {
        return $"{questionText}\n\n✅ {chosenLabel}";
    }
}
