namespace AIOrchestratorCoreLib.Sessions;

/// <summary>
/// THE PLATFORM CODES, once — the two-or-three letters every topic name starts with.
///
/// The owner asked for them so they could speak faster to the general supervisor and read a topic
/// list at a glance (2026-08-19): *"I want the topic name to always start with the abbreviation of
/// the platform we're working on"*.
///
/// IT MAPS CODE → PLATFORM NAME, DELIBERATELY NOT CODE → REPO PATH. The paths live in the owner's
/// own project registry in `~/.claude/CLAUDE.md`, which is theirs and is not in this repository.
/// Copying them here would be a second registry to drift — the exact failure
/// <see cref="Planning.PlanLedger_Markers"/> exists to prevent, one level up. The general supervisor
/// already resolves a platform NAME to a path; this only adds the code in front of it.
///
/// SUB-PRODUCTS RESOLVE TO THEIR PARENT AND STILL SHOW AS THEMSELVES, which was the owner's
/// clarification: *"if I say I want to work on IS the general supervisor should understand that I
/// mean on SL, and the topic name should still indicate IS"*. So `IS` carries `SL` as its parent —
/// the repo to open is Strategy Lab's, the name on the topic stays `IS`.
///
/// The sub-product codes are NOT invented here. They are the internal codes the Strategy Lab repo's
/// own CLAUDE.md has always used, which is why `IS` was already familiar to the owner when they
/// listed it. Where a code already exists somewhere authoritative, this table quotes it.
/// </summary>
public static class Platform_Abbreviations
{
    /// <summary>
    /// Code, the platform it names, and the parent code when it is a sub-product of another.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Platform, string? ParentCode)> ALL =
    [
        ("SL", "Strategy Lab", null),
        ("AS", "Arb Studio", null),
        ("OL", "Option Lab", null),
        ("SK-C", "Skeleton Client", null),
        ("SK-M", "Skeleton Master", null),
        ("AI-Orch", "AI Orchestrator", null),
        ("SS", "Seasonal Studio", null),
        ("ODP", "Option Database Preprocessor", null),
        ("UPD", "Updater", null),
        ("CRM", "CRM", null),
        ("TKT", "Tickets", null),

        // Strategy Lab's own sub-products, quoting the internal codes its CLAUDE.md already defines.
        ("SB", "Strategy Builder", "SL"),
        ("NO", "Noise Adder", "SL"),
        ("DA", "Data Analyzer", "SL"),
        ("PB", "Portfolio Builder", "SL"),
        ("IS", "Invest Studio", "SL"),
        ("TKL", "Tracker", "SL"),
        ("API", "Trading System Bridge", "SL"),
    ];

    /// <summary>
    /// The code whose REPO should be opened for this code — itself, or its parent's when it is a
    /// sub-product. Null when the code is not one of ours, which is a question, not a guess.
    /// </summary>
    public static string? Resolve_RepoCode_OrNull(string code)
    {
        foreach (var entry in ALL)
        {
            if (!string.Equals(entry.Code, code, StringComparison.OrdinalIgnoreCase))
                continue;

            return entry.ParentCode ?? entry.Code;
        }

        return null;
    }

    /// <summary>
    /// The table as markdown, for any document that has to teach it. One renderer, so no document
    /// can teach a different list — the lesson PlanLedger_Markers paid for.
    /// </summary>
    public static string Describe_Legend()
    {
        return string.Join(" · ", ALL.Select(entry =>
            entry.ParentCode == null
                ? $"`{entry.Code}` {entry.Platform}"
                : $"`{entry.Code}` {entry.Platform} (in {entry.ParentCode})"));
    }
}
