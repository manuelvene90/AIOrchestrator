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
/// The question comes from the `QUESTION:` line, and from nowhere else: `OwnerQuestion_Contract`
/// refuses to forward a question that has none, so by the time this runs there is always one.
///
/// <para>
/// THE DERIVATION IS RETIRED, AND ITS ABSENCE IS THE POINT. Until 2026-09-07 a missing
/// `QUESTION:` line was papered over — the last sentence of the body ending in '?' was mined for
/// one, and failing that the owner got a canned "Your call:" over a set of buttons. Both were
/// rescues of a malformed question, and a rescue is why nobody ever fixed the shape: the owner
/// spent an afternoon answering questions with questions ("which part are you talking about?"),
/// which is what a derived question reads like. A question is now complete or it is not sent.
/// </para>
/// </summary>
public static class QuestionPrompt_Builder
{
    public const string PREFIX = "❓ ";

    /// <summary>
    /// The glyph and the question, and nothing else to decide. It throws on an empty question
    /// rather than substituting one: the only caller is the send path, which is reached only
    /// through <c>OwnerQuestion_Contract.Build</c>, and that cannot produce an empty question — so
    /// an empty one here is a broken invariant, not an input to be tolerated.
    /// </summary>
    public static string Build(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("a question reached the prompt builder empty — the contract should have refused it", nameof(question));

        return $"{PREFIX}{question.Trim()}";
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

    /// <summary>Beyond this the owner's reply is a paragraph, and the record becomes the message.</summary>
    public const int MAX_ANSWER_PREVIEW_LENGTH = 80;

    /// <summary>When they answered with something that leaves no usable preview — a photo, a sticker.</summary>
    public const string ANSWERED_IN_WRITING = "answered by message";

    /// <summary>
    /// The same record, for an answer the owner TYPED instead of tapping.
    ///
    /// Without it a question answered in writing kept no trace of having been answered: the tap path
    /// stamped its choice here and the typed path stamped nothing, so the owner scrolled back to a
    /// question that still read as open. Their words, 2026-08-24, after answering one by message
    /// because the reply needed more than a label: *"The unanswered question — or rather, the one I
    /// answered with a message instead of a tap — dated back further."*
    ///
    /// IT SAYS "answered", NOT the label of a choice, because no choice was made. The preview is
    /// their own first line, truncated: a typed answer can be a pasted conversation, and the record
    /// has to stay a record rather than become a second copy of the message.
    /// </summary>
    public static string Build_AnsweredByMessageText(string questionText, string ownerText)
    {
        return $"{questionText}\n\n✅ answered: {Preview_OrDefault(ownerText)}";
    }

    /// <summary>
    /// The question message after a HIGH-RISK option has been tapped: the choice is shown as
    /// PROPOSED, not taken, with the four digits that will take it.
    ///
    /// <para>
    /// IT NAMES THE OPERATION BACK. "You are about to: &lt;option&gt;" is the read-back — the owner
    /// confirms what they are approving, not merely that they meant to press something. That is the
    /// whole content of the second gesture, and it is why the option text is repeated here even
    /// though it is already on the button they just pressed.
    /// </para>
    /// </summary>
    public static string Build_ConfirmationText(string questionText, string chosenLabel, string code, int expiryMinutes)
    {
        return $"{questionText}\n\n🔐 You are about to: {chosenLabel}\n\nReply with the code {code} within {expiryMinutes} minutes to confirm. Anything else leaves it undone.";
    }

    /// <summary>
    /// The question message after the owner tapped "💬 Let's talk": the question stays visible, with
    /// the acknowledgement under it in place of a choice.
    ///
    /// <para>
    /// A TAP THAT CHANGES NOTHING READS AS A BUTTON THAT DOES NOTHING. That was the defect — the
    /// only feedback on this button was Telegram's transient toast, so the owner tapped it twelve
    /// times in one afternoon and could not tell whether any of them had landed. It records no
    /// choice, because none was made; what it records is that the app heard them.
    /// </para>
    /// </summary>
    public static string Build_TalkText(string questionText)
    {
        return $"{questionText}\n\n{Bridge.OwnerPush_Policy.TALK_ACKNOWLEDGEMENT}";
    }

    /// <summary>
    /// The record left on a question the asker moved past AFTER the owner had replied in words.
    ///
    /// <para>
    /// With two questions open a typed reply binds to neither (the app will not guess which one was
    /// meant), so both stayed open — for hours, on the PULSE line, as "waiting on you" (measured
    /// 2026-09-10: three questions from 15:58, 16:46 and 16:56 still listed at 21:50). The reply
    /// followed by a NEWER question from the same asker is the app's evidence that the older ones
    /// were dealt with in prose; they are closed as superseded and say so. Nothing is invented: if
    /// one still mattered, the asker re-asks it.
    /// </para>
    /// </summary>
    public static string Build_SupersededText(string questionText)
    {
        return $"{questionText}\n\n{SUPERSEDED_SUFFIX}";
    }

    public const string SUPERSEDED_SUFFIX = "⏭ superseded — you had replied in words and a newer question followed. It will be re-asked if it still matters.";

    /// <summary>The record left on a question that nobody answered before its deadline.</summary>
    public static string Build_TimedOutText(string questionText, string outcome)
    {
        return $"{questionText}\n\n⏳ {outcome}";
    }

    static string Preview_OrDefault(string ownerText)
    {
        var firstLine = (ownerText ?? "")
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);

        if (string.IsNullOrEmpty(firstLine))
            return ANSWERED_IN_WRITING;

        return firstLine.Length <= MAX_ANSWER_PREVIEW_LENGTH
            ? firstLine
            : $"{firstLine[..MAX_ANSWER_PREVIEW_LENGTH]}…";
    }
}
