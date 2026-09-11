using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Channels;

namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>The marker lines an agent wrote, as extracted — nothing judged yet.</summary>
public sealed record OwnerQuestionDraft(
    IReadOnlyList<string> QuestionLines,
    IReadOnlyList<string> Options,
    IReadOnlyList<string> Recommendations,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Rows);

/// <summary>What a draft is missing. Every one of them is reported at once, never the first alone.</summary>
public enum QuestionFaults
{
    NoQuestion,
    FewerThanTwoOptions,
    NoRecommendation,
    NoRisk,
    RiskNotHighOrLow,
    NoRow,
    RowNotACodeOrNone,
}

/// <summary>A question the app will actually put in front of the owner.</summary>
public sealed record OwnerQuestion(
    string Question,
    IReadOnlyList<string> Options,
    string Recommendation,
    bool DeclaredHighRisk,
    string? RowCode);

/// <summary>
/// A QUESTION TO THE OWNER IS AN OBJECT WITH REQUIRED FIELDS, and the app refuses to forward one
/// that is missing any of them — the same fields the delivery tracker requires of its own
/// <c>askQuestion</c>, for the reason its instructions give: a question carrying options and a
/// recommendation is a decision taken in thirty seconds, and the same question without them is a
/// decision deferred for a week.
///
/// <para>
/// Measured on one topic, 2026-09-07: questions arrived one at a time for forty minutes, several
/// with no options and none with a recommendation, and the owner had to ask "what are these
/// methods?" and "which part are you talking about?" before he could answer anything. The grammar
/// this replaces accepted <c>OPTION:</c> lines with no question at all and derived one from the
/// last sentence ending in a question mark.
/// </para>
/// <para>
/// A REFUSAL LISTS EVERY FAULT AT ONCE. Acting on the first alone turns one refusal into four
/// round trips, which is the tracker's own rule about refusals and it is right.
/// </para>
/// <para>
/// <c>RISK:</c> is DECLARED by the asker and combined by the engine with the pattern classifier over
/// the question and its options — a declaration can only ADD a lock, never remove one. <c>ROW:</c>
/// names the plan row the decision belongs to, or the word <c>none</c>: written out, so an absent
/// row is a statement rather than a forgetting.
/// </para>
/// </summary>
public static class OwnerQuestion_Contract
{
    /// <summary>
    /// THE BARE WORDS, DERIVED. This class matched `QUESTION` while two others matched `QUESTION:` —
    /// the exact drift E3 exists to end. They are the one grammar marker with its colon trimmed now,
    /// so a change moves both spellings at once and neither can be edited alone.
    /// </summary>
    public static readonly string QUESTION_MARKER = ChannelGrammar.Bare(ChannelGrammar.QUESTION);
    public static readonly string OPTION_MARKER = ChannelGrammar.Bare(ChannelGrammar.OPTION);
    public static readonly string RECOMMEND_MARKER = ChannelGrammar.Bare(ChannelGrammar.RECOMMEND);
    public static readonly string RISK_MARKER = ChannelGrammar.Bare(ChannelGrammar.RISK);
    public static readonly string ROW_MARKER = ChannelGrammar.Bare(ChannelGrammar.ROW);

    /// <summary>The word that says "this decision belongs to no row", spelled out rather than left blank.</summary>
    public const string NO_ROW = "none";

    /// <summary>
    /// `FIN-D-277`, `FIN-D-277a`, `AI-ORCH-D-012b` — a product prefix, a letter, a number, and an
    /// optional addendum letter. Deliberately not a free string: a row that is not addressable is a
    /// row nobody can look up, and the owner asked three times in one afternoon for the code beside
    /// the words.
    /// </summary>
    static readonly Regex ROW_CODE = new(@"^[A-Z][A-Z0-9-]*-[A-Z]-\d+[a-z]?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// THE UPPER BOUND ON OPTIONS — two to four (owner, brief C/E2). The lower bound is
    /// <see cref="QuestionFaults.FewerThanTwoOptions"/>, immediately below, so the option-count rule
    /// has one home even though its two halves are enforced by different means.
    ///
    /// <para>
    /// AND THEY ARE ENFORCED DIFFERENTLY ON PURPOSE. Too FEW is refused here: a question with one
    /// option is not a choice, and sending it back costs the owner nothing. Too MANY is coached by
    /// <see cref="OwnerMessageFaults.TooManyOptions"/> after the question has gone out, with every
    /// button numbered so five are still readable — because refusing a real choice would lose the
    /// owner a decision they can perfectly well make.
    /// </para>
    /// </summary>
    public const int MAXIMUM_OPTIONS = 4;

    /// <summary>Whether the agent was trying to ask at all — one marker present is an attempt.</summary>
    public static bool Is_Attempted(OwnerQuestionDraft draft)
    {
        return Any(draft.QuestionLines)
            || Any(draft.Options)
            || Any(draft.Recommendations)
            || Any(draft.Risks)
            || Any(draft.Rows);
    }

    public static IReadOnlyList<QuestionFaults> Check(OwnerQuestionDraft draft)
    {
        List<QuestionFaults> faults = [];

        if (!Any(draft.QuestionLines))
            faults.Add(QuestionFaults.NoQuestion);

        if (draft.Options.Count(option => !string.IsNullOrWhiteSpace(option)) < 2)
            faults.Add(QuestionFaults.FewerThanTwoOptions);

        if (!Any(draft.Recommendations))
            faults.Add(QuestionFaults.NoRecommendation);

        if (!Any(draft.Risks))
            faults.Add(QuestionFaults.NoRisk);
        else if (Parse_Risk_OrNull(First(draft.Risks)) == null)
            faults.Add(QuestionFaults.RiskNotHighOrLow);

        if (!Any(draft.Rows))
            faults.Add(QuestionFaults.NoRow);
        else if (!Is_CodeOrNone(First(draft.Rows)))
            faults.Add(QuestionFaults.RowNotACodeOrNone);

        return faults;
    }

    public static OwnerQuestion Build(OwnerQuestionDraft draft)
    {
        var faults = Check(draft);

        if (faults.Count > 0)
            throw new InvalidOperationException($"OwnerQuestion_Contract.Build on a draft with faults: {string.Join(", ", faults)}");

        var row = First(draft.Rows);

        return new OwnerQuestion(
            Question: string.Join(' ', draft.QuestionLines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim())),
            Options: [.. draft.Options.Where(option => !string.IsNullOrWhiteSpace(option)).Select(option => option.Trim())],
            Recommendation: First(draft.Recommendations),
            DeclaredHighRisk: Parse_Risk_OrNull(First(draft.Risks))!.Value,
            RowCode: row.Equals(NO_ROW, StringComparison.OrdinalIgnoreCase) ? null : row);
    }

    /// <summary>What to WRITE, never merely what is wrong — the agent has to be able to act on it.</summary>
    public static string Describe(QuestionFaults fault)
    {
        return fault switch
        {
            QuestionFaults.NoQuestion => "QUESTION: is missing — one short, self-contained question the owner can answer from a lock screen.",
            QuestionFaults.FewerThanTwoOptions => "OPTION: lines — at least two, spelled out; a question with one option is not a choice.",
            QuestionFaults.NoRecommendation => "RECOMMEND: is missing — what you would do, and why, in one line.",
            QuestionFaults.NoRisk => "RISK: is missing — `high` or `low`. High means a tap is not enough and the owner types a read-back code.",
            QuestionFaults.RiskNotHighOrLow => "RISK: must be exactly `high` or `low` — nothing in between, because nothing in between has a behaviour.",
            QuestionFaults.NoRow => "ROW: is missing — the plan row this decision belongs to (e.g. FIN-D-277), or the word `none`.",
            QuestionFaults.RowNotACodeOrNone => "ROW: must be a row code such as FIN-D-277 (an addendum letter is fine) or the word `none`.",
            _ => fault.ToString(),
        };
    }

    static bool Any(IReadOnlyList<string> values) => values.Any(value => !string.IsNullOrWhiteSpace(value));

    /// <summary>
    /// The FIRST readable value wins, which is the rule <see cref="QuestionDirectives_Parser"/>
    /// already keeps for its own markers: a marker repeated at the bottom of an entry must not
    /// silently override the one a human reads at the top.
    /// </summary>
    static string First(IReadOnlyList<string> values) => values.First(value => !string.IsNullOrWhiteSpace(value)).Trim();

    static bool? Parse_Risk_OrNull(string value)
    {
        if (value.Equals("high", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Equals("low", StringComparison.OrdinalIgnoreCase))
            return false;

        return null;
    }

    static bool Is_CodeOrNone(string value)
    {
        return value.Equals(NO_ROW, StringComparison.OrdinalIgnoreCase) || ROW_CODE.IsMatch(value);
    }
}
