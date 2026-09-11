using System.Globalization;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// Decides what goes ON the inline decision buttons and what has to go in the message ABOVE them.
///
/// WHY THIS EXISTS — the owner, 2026-08-24: *"buttons don't wrap, so when a session asks me a
/// question I often can't read all the button text."* An agent writes `OPTION:` lines and the app
/// hangs one inline button per option off the question message, the button's label being the whole
/// option text. Telegram does NOT wrap a button label: it cuts it and puts an ellipsis on the end.
/// So two thorough options that differ only in their second half arrive on the phone as two buttons
/// reading the same thing, and the owner cannot tell what they are choosing between — the one tap
/// in this system that cannot be taken back.
///
/// THE FIX: when an option is too long to read on a button, the FULL texts move into the message
/// body as a numbered list — the body is ordinary text, and text wraps — and the buttons keep only
/// the numbers. The number on the button then points at a line the owner can actually read.
///
/// WHEN EVERY OPTION IS ALREADY SHORT, NOTHING CHANGES. Today's behaviour is good in that case, and
/// a numbered list under a question with two three-word answers would be ceremony around nothing.
/// That path is pinned by a test so a later tidy-up cannot quietly make numbering unconditional.
///
/// ALL-OR-NOTHING NUMBERING. Once one option needs the treatment, every option is numbered, not just
/// the long ones: a keyboard mixing "1) rebase onto ma…" with "cancel" reads as two different kinds
/// of button and hides which list line "cancel" corresponds to. Uniform is worse for nobody.
///
/// PURE, AND INDEX-FAITHFUL. The caller registers the callback payload for option[i] against
/// label[i], so this never reorders, never deduplicates and never drops an entry — the output count
/// always equals the input count. A "helpful" dedupe here would silently rewire the owner's tap to
/// the wrong answer.
/// </summary>
public static class OptionButtons_Layout
{
    /// <summary>
    /// How many characters of a full-width inline button the owner can actually read.
    ///
    /// Telegram gives a single-column inline button the width of the message bubble and truncates
    /// what does not fit — on a narrow phone (the owner reads this system on a phone, never on a
    /// desktop client) that lands at roughly 30 characters, and it varies with the device font size
    /// and with how wide the glyphs are. 28 is that ~30 with two characters of margin, because the
    /// failure is asymmetric: numbering an option that would have just fitted costs the owner
    /// nothing, while leaving one that gets cut costs them the ability to answer.
    ///
    /// Counted in TEXT ELEMENTS, not in `char`s — see <see cref="Read_TextElements"/>.
    /// </summary>
    public const int READABLE_LABEL_WIDTH = 28;

    /// <summary>
    /// The single-character ellipsis, never "..." — three dots would spend three of the ~28 readable
    /// characters on punctuation. It appears only when text was actually cut, so its presence is
    /// information: no ellipsis means the owner is reading the whole option.
    /// </summary>
    public const string ELLIPSIS = "…";

    /// <summary>
    /// What sits between the number and the text, on the button and on the matching list line. The
    /// two must be composed identically or the owner has to work out which line "3)" refers to.
    /// </summary>
    public const string NUMBER_SEPARATOR = ") ";

    /// <summary>
    /// ButtonLabels is what to render on the inline buttons, one per input option, in the same order.
    /// OptionListText is the numbered full-text block to append to the question message, or null when
    /// the options are short enough to read on the buttons themselves.
    /// </summary>
    public static (IReadOnlyList<string> ButtonLabels, string? OptionListText) Build(IReadOnlyList<string> optionTexts)
    {
        // No options means no buttons and no question — the caller sends an ordinary message. This
        // returns empty rather than throwing because it sits on the mirror path, where an agent's
        // malformed entry must never be able to take the bridge down.
        if (optionTexts is null || optionTexts.Count == 0)
            return ([], null);

        // THE NO-CHANGE PATH. The measurement ignores surrounding whitespace (invisible on a button,
        // so it must not be what tips a question into numbering), but what comes back is the option
        // texts UNTOUCHED: the buttons the owner sees today stay the buttons they see tomorrow.
        //
        // A FIFTH OPTION FORCES NUMBERING WHATEVER THE LABELS MEASURE (owner, brief C/E2: options are
        // two to four; past that the buttons are numbered and the supervisor is coached). Length was
        // the only trigger before, so five short options rendered as five bare buttons — and a column
        // of five phrases with no numbers is the shape the numbered list exists to replace: the owner
        // has to re-read all of them to find the one they mean, and there is nothing to say back.
        // Numbering also gives them a way to answer in one character.
        var tooManyToReadUnnumbered = optionTexts.Count > Bridge.Decisions.OwnerQuestion_Contract.MAXIMUM_OPTIONS;

        if (!tooManyToReadUnnumbered && optionTexts.All(Fits_OnAButton))
            return ([.. optionTexts], null);

        var buttonLabels = new List<string>(optionTexts.Count);
        var listLines = new List<string>(optionTexts.Count);

        for (var index = 0; index < optionTexts.Count; index++)
        {
            // Numbering the owner reads starts at 1; the index is ours.
            var number = index + 1;

            // Trimmed only on this path. The caller already trims each `OPTION:` line, so this
            // guards a direct call rather than real traffic — and it keeps a stray leading space
            // from eating one of the few readable characters on the button.
            var optionText = Trim_OrEmpty(optionTexts[index]);

            buttonLabels.Add(Compose_ButtonLabel(number, optionText));
            listLines.Add(Compose_ListLine(number, optionText));
        }

        return (buttonLabels, string.Join("\n", listLines));
    }

    static bool Fits_OnAButton(string optionText)
    {
        return Count_TextElements(Trim_OrEmpty(optionText)) <= READABLE_LABEL_WIDTH;
    }

    /// <summary>
    /// "3) shorten me until it f…" — the WHOLE label, number and separator included, stays within
    /// the readable width. Budgeting only the text would put the label back over the edge as soon as
    /// a question had ten options and the prefix grew a digit.
    /// </summary>
    static string Compose_ButtonLabel(int number, string optionText)
    {
        var prefix = $"{number}{NUMBER_SEPARATOR}";
        var budget = READABLE_LABEL_WIDTH - Count_TextElements(prefix);

        // A question with 10^26 options cannot leave room for even one character of text. It cannot
        // happen, and a label of just the number is still a valid, tappable button pointing at its
        // list line — which is the property that must hold no matter how absurd the input.
        if (budget < 1)
            return prefix.TrimEnd();

        var shortened = Shorten_ToTextElements(optionText, budget);

        // AN EMPTY OR ALL-WHITESPACE OPTION STILL GETS A LABEL: the bare number, e.g. "2)". The
        // agent wrote a blank option, so there is no text to show and none is invented; what matters
        // is that the button still exists, still carries its number, and still maps to option[i] by
        // index. Dropping it would shift every later option onto the wrong callback payload.
        return shortened.Length == 0 ? prefix.TrimEnd() : prefix + shortened;
    }

    /// <summary>
    /// The list line the button number points at: the option IN FULL, never shortened — the whole
    /// point of moving the text into the body is that the body wraps.
    /// </summary>
    static string Compose_ListLine(int number, string optionText)
    {
        return optionText.Length == 0
            ? $"{number})"
            : $"{number}{NUMBER_SEPARATOR}{optionText}";
    }

    /// <summary>
    /// Cuts text down to at most <paramref name="maximumTextElements"/> text elements, spending one
    /// of them on the ellipsis when it actually cut something.
    ///
    /// Text elements, not chars: an emoji is two chars (a surrogate pair) and a flag or a family
    /// emoji is many more, joined by zero-width joiners. `text[..n]` would happily cut between the
    /// two halves of a surrogate pair and send the owner a button ending in the replacement glyph —
    /// a visibly broken message, on the one screen that has to inspire confidence.
    /// </summary>
    static string Shorten_ToTextElements(string text, int maximumTextElements)
    {
        var textElements = Read_TextElements(text);

        if (textElements.Count <= maximumTextElements)
            return text;

        return string.Concat(textElements.Take(maximumTextElements - 1)) + ELLIPSIS;
    }

    /// <summary>
    /// ONE reader of what a "character" means here (decision 12: never a second copy of a
    /// formatter). Measuring with one API and cutting with another is how a label ends up one
    /// element over the limit for exactly the inputs — emoji — that the limit exists to protect.
    /// The lists are a handful of short strings per question, so the allocation is not worth
    /// optimising away at the cost of that guarantee.
    /// </summary>
    static List<string> Read_TextElements(string text)
    {
        List<string> textElements = [];

        var enumerator = StringInfo.GetTextElementEnumerator(text);

        while (enumerator.MoveNext())
            textElements.Add((string)enumerator.Current);

        return textElements;
    }

    static int Count_TextElements(string text) => Read_TextElements(text).Count;

    static string Trim_OrEmpty(string? text) => text is null ? string.Empty : text.Trim();
}
