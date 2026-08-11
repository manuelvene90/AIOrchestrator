using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// The one wording for "how far along is this" — "57/76 done (75%) · 2 running · 1 BLOCKED".
/// Shared by /progress, /status and the periodic push so the three can never quote different
/// figures for the same ledger.
/// </summary>
public static class PlanProgress_Formatter
{
    public static string Describe_Counts(IPlanProgress progress)
    {
        var blockedPart = progress.Blocked > 0 ? $" · {progress.Blocked} BLOCKED" : "";
        var runningPart = progress.InProgress > 0 ? $" · {progress.InProgress} running" : "";

        return $"{progress.Done}/{progress.Total} done{Describe_Percent(progress)}{runningPart}{blockedPart}";
    }

    /// <summary>
    /// Truncated, never rounded: 75 of 76 tasks must not read as "100%". The only way to see 100%
    /// is for every task to be done, which is the one case where the number has to be trusted.
    /// </summary>
    static string Describe_Percent(IPlanProgress progress)
    {
        if (progress.Total <= 0)
            return "";

        return $" ({progress.Done * 100 / progress.Total}%)";
    }
}
