namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// THE LEDGER MARKERS, once. Six of them since 2026-08-19.
///
/// Deliberately NOT "the six markers" in this line: the previous version of this sentence said FIVE
/// and had to be corrected by the change that added the sixth, which is the same staleness the class
/// exists to prevent, one level up. The count lives in ALL; the prose does not restate it.
///
/// They were written down five times — two role commands, the parser's docstring, the supervisor
/// ledger hook, and the seed text — and by the time anyone counted, TWO of the copies had drifted to
/// four markers.
///
/// The mechanism is worth stating because it is not carelessness and it will happen again: `[-]` was
/// added to the parser ADDITIVELY and safely — "no existing ledger contains one, so every file parses
/// exactly as it did" — which is true of the parser and false of everything that had copied its list.
/// An additive change that is safe for the original is silently wrong for every copy of it. The same
/// evening, the same additive marker took out two independent regexes in the same way.
///
/// So the list lives here and the seed text is BUILT from it rather than restating it. The parser's
/// regex still spells the markers out, because <c>GeneratedRegex</c> needs a literal — that pair is
/// held together by a test instead, along with the one copy that cannot import C# at all: the bash
/// hook.
/// </summary>
public static class PlanLedger_Markers
{
    /// <summary>The marker as written between the brackets, and what it means, in ledger order.</summary>
    public static readonly IReadOnlyList<(string Marker, string Meaning)> ALL =
    [
        (" ", "open"),
        (">", "in progress"),
        ("x", "done"),
        ("!", "blocked"),

        // BLOCKED ON THE OWNER SPECIFICALLY, and separate from `!` by the owner's own call
        // (2026-08-19, choosing this over inferring it from the line's words). `!` had been carrying
        // both kinds: `arb portfolio UX` showed a `[!]` line blocked on a REVIEWER while the owner
        // read the count as something waiting on them, asked what had got stuck, and nothing had.
        // Only the supervisor knows which kind it is, so only the supervisor can say.
        ("?", "blocked on the owner"),

        // NOT "dropped" and not "cancelled": it is the only marker that removes weight from the
        // denominator, and the wording has to survive being read by someone deciding whether to use
        // it. "Not doing" is what the owner called it.
        ("-", "not doing"),
    ];

    /// <summary>
    /// The legend as one line of markdown — "`- [ ]` open · `- [>]` in progress · …" — for any
    /// document that has to teach the convention. One renderer, so a document cannot teach four.
    /// </summary>
    public static string Describe_Legend()
    {
        return string.Join(" · ", ALL.Select(entry => $"`- [{entry.Marker}]` {entry.Meaning}"));
    }
}
