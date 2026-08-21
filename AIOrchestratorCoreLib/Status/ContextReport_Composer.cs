using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Status.SessionContextUsage;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// The text of /context, in a class the suite can reach. The bridge engine is internal sealed with
/// no InternalsVisibleTo, so anything decided inside it can be changed - or deleted - without
/// reddening a single test; three gates once were. So the engine gathers the readings and this
/// composes them, and every judgement below is pinned by a test.
///
/// UNLIKE THE STATUS LINE AND THE DIGEST, NOTHING HERE IS FILTERED BY PERCENTAGE.
/// <see cref="ContextVisibility_Policy"/>'s thresholds exist to keep surfaces the owner did not ask
/// for quiet; /context IS the ask, and answering a direct question with a partial roster would be
/// worse than not answering it.
/// </summary>
public static class ContextReport_Composer
{
    /// <summary>
    /// Two minutes, matching what the rest of the app already treats as "recently alive" - the same
    /// window after which a finished turn stops counting as working now.
    /// </summary>
    public const int STALE_AFTER_MINUTES = 2;

    /// <summary>
    /// One session as the engine found it. <paramref name="Reading"/> is null when that session has
    /// never written a probe file, or runs a Claude Code whose status line carries no context data.
    /// </summary>
    public readonly record struct ContextRow(string Label, bool IsClosed, ISessionContextUsage? Reading);

    /// <summary>
    /// A CLOSED SESSION HAS NO WINDOW ANY MORE. Its probe file stays on disk as audit trail and the
    /// lifetime cost totals still count it deliberately - spend does not stop mattering when a
    /// session ends - but a context percentage is a statement about right now, and a closed member's
    /// last reading answers "how full is everything" with a number belonging to something gone.
    /// </summary>
    public static string Build_ForOrchestration(string title, IReadOnlyList<ContextRow> rows, DateTime nowUtc)
    {
        List<string> lines = [];

        foreach (var row in rows)
        {
            if (row.IsClosed || row.Reading == null)
                continue;

            lines.Add($"- {row.Label}: {ContextUsage_Formatter.Describe(row.Reading)}{Describe_Age_Suffix(row.Reading, nowUtc)}");
        }

        if (lines.Count == 0)
            return $"{title}: no session has reported its context yet";

        lines.Insert(0, $"CONTEXT - {title}");

        return string.Join('\n', lines);
    }

    /// <summary>
    /// THE FULLEST SESSION, NAMED. From General the owner is looking at every orchestration at once,
    /// so a bare percentage per orchestration would tell them something is nearly full without
    /// saying which window it is - and which window it is, is the only part they can act on.
    /// </summary>
    public static (string Label, double Percent)? Pick_Fullest_OrNull(IReadOnlyList<ContextRow> rows)
    {
        string? fullestLabel = null;
        var fullestPercent = 0.0;

        foreach (var row in rows)
        {
            if (row.IsClosed || row.Reading == null)
                continue;

            if (fullestLabel != null && row.Reading.UsedPercent <= fullestPercent)
                continue;

            fullestLabel = row.Label;
            fullestPercent = row.Reading.UsedPercent;
        }

        if (fullestLabel == null)
            return null;

        return (fullestLabel, fullestPercent);
    }

    /// <summary>
    /// How old the reading is, and ONLY when that is worth saying. A probe file is rewritten on
    /// every status-line render, so an ACTIVE session's figure is seconds old and stamping every row
    /// with an age would be noise on all of them. Past the threshold the session has not rendered in
    /// a while, and THAT the owner needs told rather than left to assume the number is live.
    ///
    /// A reading stamped in the FUTURE is treated as fresh rather than described with a negative
    /// age - the ruling SessionDuration_Formatter already carries for agent-written stamps, applied
    /// here to a clock-skewed file mtime.
    /// </summary>
    public static string Describe_Age_Suffix(ISessionContextUsage reading, DateTime nowUtc)
    {
        var age = nowUtc - reading.ProbeTimeUtc;

        if (age < TimeSpan.FromMinutes(STALE_AFTER_MINUTES))
            return "";

        return $" · {SessionDuration_Formatter.Describe(age)} old";
    }
}
