namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// The PLAN.md sections that are NOT the ledger, and therefore never reach the progress bar.
///
/// WHY THIS IS ENFORCED AND NOT MERELY WRITTEN DOWN (owner directive, 2026-08-14). The complaint was
/// that orchestrations lose their objectives: an implementer or a reviewer reads a file while doing
/// the work, notices something unrelated, reports it, and it becomes work — so the horizon explodes,
/// the endeavour never lands, and the things the owner actually asked for get forgotten underneath
/// the discoveries. The remedy is a place to PARK a discovery that is visibly not part of the
/// endeavour: written down, so nothing is lost, and outside the denominator, so nothing the owner did
/// not ask for can move their progress bar.
///
/// A rule that lives only in the role commands cannot deliver that. `PlanLedger_Parser` matches a
/// task line ANYWHERE in the file, so one `- [ ]` written under a "parked" heading — the natural way
/// to write a parked item, and the shape every example in those commands uses — silently adds itself
/// to the total. The section would read as parked and count as owed, which is the failure with the
/// documentation on top of it. Hooks advise, the app enforces (decision 21): the parser is the point
/// of effect, so the boundary is defined here and applied there.
///
/// SECTION-SCOPED, NOT A TRUNCATION. Skipping the rest of the file from the first such heading would
/// make the ledger's meaning depend on where a section was pasted, and a supervisor who put PARKED in
/// the middle would silently lose every task below it — trading a bar that over-counts for one that
/// under-counts. Task lines resume at the next heading that is not one of these.
/// </summary>
public static class PlanLedger_Sections
{
    /// <summary>
    /// Matched on the heading text, case-insensitively, as a PREFIX — "## PARKED — found, not asked
    /// for" and "## Parked questions (ask when the owner is reachable)" are both the parked section,
    /// and requiring the exact title would have excluded the ones already written in the field.
    /// </summary>
    public static readonly IReadOnlyList<string> NON_LEDGER_HEADING_PREFIXES =
    [
        "PARKED",
        "OWNER REQUESTS",
    ];

    /// <summary>
    /// Whether this line opens a section whose task lines are not the ledger's. False for any line
    /// that is not a heading, so a caller can feed it every line of the file.
    /// </summary>
    public static bool Opens_NonLedgerSection(string line)
    {
        var title = Read_HeadingTitle_OrNull(line);

        if (title == null)
            return false;

        foreach (var prefix in NON_LEDGER_HEADING_PREFIXES)
        {
            if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Whether this line is a heading of any level — the thing that ENDS a section.</summary>
    public static bool Is_Heading(string line)
    {
        return Read_HeadingTitle_OrNull(line) != null;
    }

    /// <summary>
    /// The text after the `#`s, or null when the line is not a heading. A `#` with no space after it
    /// is not a heading in markdown, and a line of `###` alone is a heading with no title — neither
    /// can name a section, so both answer null rather than the empty string.
    /// </summary>
    static string? Read_HeadingTitle_OrNull(string line)
    {
        var text = line.TrimEnd('\r').TrimStart();

        if (!text.StartsWith('#'))
            return null;

        var hashes = 0;

        while (hashes < text.Length && text[hashes] == '#')
            hashes++;

        if (hashes >= text.Length || text[hashes] != ' ')
            return null;

        var title = text[hashes..].Trim();

        return title.Length == 0 ? null : title;
    }
}
