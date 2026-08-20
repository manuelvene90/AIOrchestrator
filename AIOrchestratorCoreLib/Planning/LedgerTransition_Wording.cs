using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// How a ledger movement reaches the owner's phone: as few words as carry the fact.
///
/// They asked for "a very short and fast message" (2026-08-20), and short is the requirement rather
/// than a nicety — this fires whenever work moves, so anything wordy becomes the waterfall the whole
/// day was spent removing. One glyph and the line's own text, nothing else. No counts, no
/// percentages: the status line and the half-hourly digest already carry those, and repeating them
/// here would put the same number on their phone three ways.
/// </summary>
public static class LedgerTransition_Wording
{
    public const string FINISHED_GLYPH = "✔";
    public const string STARTED_GLYPH = "▶";
    public const string RECAP_GLYPH = "🏁";

    public static string Describe(LedgerTransition transition)
    {
        List<string> lines = [];

        foreach (var text in transition.Finished)
            lines.Add($"{FINISHED_GLYPH} {text}");

        foreach (var text in transition.Started)
            lines.Add($"{STARTED_GLYPH} {text}");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// The end-of-endeavour recap. It lists the macro lines rather than counting them, because "11/11
    /// done" is what the bar has said all along — the thing they cannot get anywhere else is WHAT was
    /// delivered, in one place, at the end.
    ///
    /// Not truncated. A ledger is meant to be 7-8 macro lines by the owner's own rule, so a recap
    /// that fits is the normal case and one that does not is telling them something true about the
    /// ledger's shape.
    /// </summary>
    public static string Describe_Recap(string displayName, IPlanProgress progress)
    {
        var delivered = progress.Lines
            .Where(line => !line.IsSubTask && line.Marker == "x")
            .Select(line => $"{FINISHED_GLYPH} {line.Text}")
            .ToList();

        var dropped = progress.NotDoing > 0 ? $" · {progress.NotDoing} not doing" : "";

        var header = $"{RECAP_GLYPH} {displayName} — everything asked for is done. {progress.Done}/{progress.Total}{dropped}.";

        return delivered.Count == 0 ? header : $"{header}\n{string.Join("\n", delivered)}";
    }
}
