using AIOrchestratorCoreLib.Channels;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>One way an entry breaks the owner-message contract.</summary>
public enum OwnerMessageFaults
{
    /// <summary>More than one `QUESTION:` line. The owner cannot answer two things with one reply.</summary>
    TwoQuestions,

    /// <summary>Prose below the question. A question is the last thing in an entry or it is a moving target.</summary>
    ProseAfterTheQuestion,

    /// <summary>Line one says "I read you" instead of saying the thing.</summary>
    OpensWithAReceipt,

    /// <summary>Code, paths or stack traces in a message to someone who does not read code.</summary>
    CarriesCode,

    /// <summary>Over the length the owner will actually read.</summary>
    TooLong,

    /// <summary>
    /// More options than the owner will weigh on a phone. Owner, brief C/E2: options are two to
    /// four.
    ///
    /// <para>
    /// IT COACHES AND DOES NOT REFUSE, and that is the whole reason it lives here rather than beside
    /// the lower bound. `OwnerQuestion_Contract.FewerThanTwoOptions` REFUSES to forward — a question
    /// with one option is not a choice, so nothing is lost by sending it back. A question with five
    /// options IS a choice; refusing it would lose the owner a decision they can perfectly well make,
    /// which is the worst outcome this system has. So the question goes out — with every button
    /// numbered, which is what makes five readable — and the supervisor is told afterwards.
    /// </para>
    /// </summary>
    TooManyOptions,
}

/// <summary>
/// THE RULES THE PROTOCOL ASKS FOR, CHECKED INSTEAD OF HOPED FOR.
///
/// <para>
/// The supervisor's protocol is 1,164 lines carrying 229 imperative rules, and a mechanical audit on
/// 2026-09-07 found 14 pairs that cannot both be obeyed. The owner's complaints all trace to the same
/// shape: a rule near the top ("lead with the decision", "one open question at a time") is contradicted
/// by another 300 to 780 lines below it, and the lower one wins because it is what the model read last.
/// </para>
/// <para>
/// <b>A SENTENCE IN A PROMPT IS A WISH; A CHECK IS A RULE.</b> Nothing here argues with the model. It
/// reads what was actually written and names what is wrong with it, so the handful of rules that can
/// be settled mechanically stop depending on which of 229 instructions the model happened to weigh.
/// </para>
/// <para>
/// <b>IT REPORTS, IT DOES NOT SWALLOW.</b> A validator that silently dropped an owner-facing entry
/// would turn a formatting fault into a lost answer — the single worst outcome in this system, and
/// the one the whole question/answer audit was about. Faults are logged and coached back to the
/// session; the owner still gets the message.
/// </para>
/// <para>
/// <b>ONLY WHAT A MACHINE CAN SETTLE.</b> "Answer completely" and "say it in plain words" are real
/// rules and they are not here, because a check that guesses would train the session to write for the
/// checker. Counting question markers, spotting a file path and reading the first line are not guesses.
/// </para>
/// </summary>
public static class OwnerMessage_Contract
{
    /// <summary>
    /// ONE CEILING, READ FROM ONE HOME — <see cref="Brevity_Policy"/> (owner, brief C/E2: 5 lines,
    /// 600 characters).
    ///
    /// <para>
    /// These were literals here, and they had already drifted: this class said SIX lines while
    /// `Brevity_Policy` said five, so an entry of six lines was coached as too long by one surface
    /// and passed as fine by the other, and the supervisor could obey whichever it happened to be
    /// told. The characters agreed at 600 only by luck — two literals, no link.
    /// </para>
    /// <para>
    /// `Brevity_Policy` is the home because it is the one that MEASURES what was actually mirrored
    /// and reports the numbers; this class checks the entry before it goes. Same rule, two moments.
    /// </para>
    /// </summary>
    public const int MAXIMUM_LINES = Brevity_Policy.MAX_LINES;

    public const int MAXIMUM_CHARACTERS = Brevity_Policy.MAX_CHARACTERS;

    // FROM THE GRAMMAR, and `static readonly` rather than `const` because of it: the grammar is a
    // FILE both this app and the bash tool read, so its values arrive at runtime. A `const` would
    // have to be a literal here, which is the ninth copy E3 removes.
    static readonly string QUESTION_MARKER = ChannelGrammar.QUESTION;
    static readonly string OPTION_MARKER = ChannelGrammar.OPTION;

    /// <summary>Markers that legitimately follow a question — they are part of it, not prose.</summary>
    static readonly string[] QUESTION_COMPANIONS =
    [
        OPTION_MARKER, ChannelGrammar.DEADLINE, ChannelGrammar.DEFAULT, ChannelGrammar.IMAGE, ChannelGrammar.ATTACH,
    ];

    /// <summary>
    /// How a reply that is only an acknowledgement opens. Deliberately anchored to the START of the
    /// line: "noted" opening an entry is a receipt, the same word inside a sentence is not.
    /// </summary>
    static readonly string[] RECEIPT_OPENERS =
    [
        "noted", "got it", "ok,", "okay,", "understood", "will do", "on it", "roger",
        "reading", "picking this up", "picking it up", "starting", "working on",
        "i'll ", "i will ", "i'm going to ", "im going to ", "received", "thanks", "sure,",
    ];

    /// <summary>
    /// Checks one owner-facing entry body. Empty result means it conforms.
    /// </summary>
    public static IReadOnlyList<OwnerMessageFaults> Check(string body)
    {
        List<OwnerMessageFaults> faults = [];

        if (string.IsNullOrWhiteSpace(body))
            return faults;

        var lines = body.Replace("\r\n", "\n").Split('\n');

        var questionIndexes = new List<int>();
        var optionIndexes = new List<int>();

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].TrimStart();

            if (trimmed.StartsWith(QUESTION_MARKER, StringComparison.Ordinal))
                questionIndexes.Add(index);
            else if (trimmed.StartsWith(OPTION_MARKER, StringComparison.Ordinal))
                optionIndexes.Add(index);
        }

        if (questionIndexes.Count > 1)
            faults.Add(OwnerMessageFaults.TwoQuestions);

        // `OPTION:` lines with no `QUESTION:` USED TO BE A FAULT HERE and is not any more: it is
        // one of the five things OwnerQuestion_Contract requires, and that one REFUSES to forward
        // the question rather than coaching after the fact. Two homes for one rule meant the agent
        // was told twice, and the older telling was by then untrue — it said "a tap answers
        // something that was never asked", of buttons the app no longer sends.

        if (questionIndexes.Count > 0 && Has_ProseAfter(lines, questionIndexes[^1]))
            faults.Add(OwnerMessageFaults.ProseAfterTheQuestion);

        if (Opens_WithAReceipt(lines))
            faults.Add(OwnerMessageFaults.OpensWithAReceipt);

        if (Carries_Code(body))
            faults.Add(OwnerMessageFaults.CarriesCode);

        if (Is_TooLong(lines, body))
            faults.Add(OwnerMessageFaults.TooLong);

        // COUNTED, NOT REFUSED — see the fault's own summary for why this bound coaches while the
        // lower one rejects.
        if (optionIndexes.Count > Decisions.OwnerQuestion_Contract.MAXIMUM_OPTIONS)
            faults.Add(OwnerMessageFaults.TooManyOptions);

        return faults;
    }

    /// <summary>What to write back to the session. One line per fault, in its own words.</summary>
    public static string Describe(OwnerMessageFaults fault)
    {
        return fault switch
        {
            OwnerMessageFaults.TooManyOptions =>
                $"OPTION: lines — {Decisions.OwnerQuestion_Contract.MAXIMUM_OPTIONS} at most. Past that the owner is reading a list, not making a choice; the buttons were numbered so it stays readable, but fold the near-duplicates together.",
            OwnerMessageFaults.TwoQuestions =>
                "Two QUESTION: lines in one entry. The owner answers with one message, so the second question "
                + "gets no answer and the app cannot tell which one they meant. Ask one; keep the other for after.",
            OwnerMessageFaults.ProseAfterTheQuestion =>
                "There is prose BELOW your QUESTION: line. The question must be the last thing in the entry — "
                + "anything after it arrives under a lock screen the owner has already stopped reading.",
            OwnerMessageFaults.OpensWithAReceipt =>
                "Your first line is a receipt, not the answer. The owner reads line one and often stops — put the "
                + "decision, the result or the answer there, and what you are about to do on line two.",
            OwnerMessageFaults.CarriesCode =>
                "This entry carries a file path, an identifier or a stack trace. The owner does not read code: "
                + "name the thing by what it does for the product, and keep the path for the channel.",
            OwnerMessageFaults.TooLong =>
                $"Over {MAXIMUM_LINES} lines or {MAXIMUM_CHARACTERS} characters. Cut the evidence, never the answer — "
                + "if they want the reasoning they will ask for it.",
            _ => "This entry breaks the owner-message contract.",
        };
    }

    static bool Has_ProseAfter(string[] lines, int lastQuestionIndex)
    {
        for (var index = lastQuestionIndex + 1; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();

            if (trimmed.Length == 0)
                continue;

            var isCompanion = false;

            foreach (var companion in QUESTION_COMPANIONS)
            {
                if (trimmed.StartsWith(companion, StringComparison.Ordinal))
                {
                    isCompanion = true;
                    break;
                }
            }

            if (!isCompanion)
                return true;
        }

        return false;
    }

    static bool Opens_WithAReceipt(string[] lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
                continue;

            // A MARKER LINE IS NOT PROSE and cannot be a receipt, so it is not the line under test.
            if (trimmed.StartsWith(ChannelGrammar.IMAGE, StringComparison.Ordinal) || trimmed.StartsWith(ChannelGrammar.ATTACH, StringComparison.Ordinal))
                continue;

            var lowered = trimmed.ToLowerInvariant();

            foreach (var opener in RECEIPT_OPENERS)
            {
                if (lowered.StartsWith(opener, StringComparison.Ordinal))
                    return true;
            }

            // Only the FIRST prose line is judged: a receipt on line two is where the contract puts it.
            return false;
        }

        return false;
    }

    static bool Carries_Code(string body)
    {
        foreach (var rawLine in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();

            // IMAGE: and ATTACH: carry a path BY DESIGN — it is how a screenshot or a file reaches the phone.
            if (line.StartsWith(ChannelGrammar.IMAGE, StringComparison.Ordinal) || line.StartsWith(ChannelGrammar.ATTACH, StringComparison.Ordinal))
                continue;

            if (Has_DeepPath(line) || Has_StackFrame(line) || Has_CodeIdentifier(line))
                return true;
        }

        return false;
    }

    /// <summary>Three or more path separators in one token — a real path, not a date or a fraction.</summary>
    static bool Has_DeepPath(string line)
    {
        foreach (var token in line.Split(' ', '\t'))
        {
            var separators = 0;

            foreach (var character in token)
            {
                if (character is '/' or '\\')
                    separators++;
            }

            if (separators >= 3)
                return true;
        }

        return false;
    }

    static bool Has_StackFrame(string line)
    {
        return line.StartsWith("at ", StringComparison.Ordinal) && line.Contains(':');
    }

    /// <summary>
    /// CamelCase joined by an underscore — the naming convention this codebase uses everywhere
    /// (`PlanLedger_Parser`, `Nudge_Decider`). Narrow on purpose: a member id like `imp-1` is how the
    /// owner refers to the crew themselves and must never be flagged.
    /// </summary>
    static bool Has_CodeIdentifier(string line)
    {
        foreach (var token in line.Split(' ', '\t', ',', '.', '(', ')'))
        {
            var underscore = token.IndexOf('_');

            if (underscore <= 0 || underscore == token.Length - 1)
                continue;

            if (char.IsUpper(token[0]) && char.IsUpper(token[underscore + 1]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// THE SAME COUNT THE TOOL MAKES, through the same rule — prose lines only (owner, 2026-09-10).
    ///
    /// It counted every non-blank line until then, and a well-formed question is six marker lines by
    /// construction: this method coached every question ever asked as too long. `Brevity_Policy` owns
    /// the counting now, so the ceiling and its measurement live together, and `channel-append.sh`
    /// refuses on the same basis one moment earlier.
    ///
    /// THE CHARACTER CEILING STILL COUNTS EVERYTHING: a wall of text is a wall whatever the marker at
    /// its left edge.
    /// </summary>
    static bool Is_TooLong(string[] lines, string body)
    {
        if (body.Length > MAXIMUM_CHARACTERS)
            return true;

        return Brevity_Policy.Count_Lines(string.Join('\n', lines)) > MAXIMUM_LINES;
    }
}
