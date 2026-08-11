using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Limits;

/// <summary>
/// Reads the KNOWN status-line rate-limit shape (verified against Claude Code 2.1.223):
///   "rate_limits": { "five_hour": { "used_percentage": 46, "resets_at": &lt;unix&gt; },
///                    "seven_day": { "used_percentage": 49, "resets_at": &lt;unix&gt; } }
/// plus the context window's own usage. Payloads without these keys simply yield nothing — the
/// tolerant LimitData_Parser stays the fallback that drives the automatic alerts.
/// </summary>
public static class RateLimits_Reader
{
    /// <summary>One limit window as the owner reads it: "5h — 46% — resets in 2 h 14 min".</summary>
    public static IReadOnlyList<(string Window, double Percent, DateTime? ResetsAtLocal)> Read_Windows(string rawStatuslineJson)
    {
        List<(string Window, double Percent, DateTime? ResetsAtLocal)> windows = [];

        try
        {
            if (JsonNode.Parse(rawStatuslineJson) is not JsonObject root)
                return windows;

            if (root["rate_limits"] is not JsonObject rateLimits)
                return windows;

            foreach (var pair in rateLimits)
            {
                if (pair.Value is not JsonObject window)
                    continue;

                var percentNode = window["used_percentage"];

                if (percentNode == null)
                    continue;

                windows.Add((Describe_Window(pair.Key), percentNode.GetValue<double>(), Read_ResetsAt_OrNull(window)));
            }
        }
        catch
        {
            // A half-written probe file contributes nothing.
        }

        return windows;
    }

    /// <summary>Context-window pressure of ONE session — the long-task hazard that precedes a compaction.</summary>
    public static double? Read_ContextPercent_OrNull(string rawStatuslineJson)
    {
        try
        {
            var node = (JsonNode.Parse(rawStatuslineJson) as JsonObject)?["context_window"]?["used_percentage"];

            if (node == null)
                return null;

            return node.GetValue<double>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The session's own transcript file, straight from the status line — replaces guessing the
    /// projects-folder slug and grepping for the role command.
    /// </summary>
    public static string? Read_TranscriptPath_OrNull(string rawStatuslineJson)
    {
        try
        {
            var node = (JsonNode.Parse(rawStatuslineJson) as JsonObject)?["transcript_path"];

            if (node == null)
                return null;

            return node.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    public static string? Read_ModelName_OrNull(string rawStatuslineJson)
    {
        try
        {
            var node = (JsonNode.Parse(rawStatuslineJson) as JsonObject)?["model"]?["display_name"];

            if (node == null)
                return null;

            return node.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Highest reading per window across every session's probe file — the number that constrains you.</summary>
    public static IReadOnlyList<(string Window, double Percent, DateTime? ResetsAtLocal, string Models)> Read_WorstAcrossSessions(
        IReadOnlyList<string> usageFilePaths)
    {
        return Read_WorstAcrossSessions(usageFilePaths, DateTime.Now);
    }

    /// <summary>
    /// Clock-injectable overload. The expiry and window-instance rules below ARE this reader, and
    /// they cannot be tested against a hidden <see cref="DateTime.Now"/>.
    ///
    /// Probe files are never deleted, so this set spans days of history and therefore several
    /// distinct limit WINDOWS. Comparing readings across them is what made /limits lie: on
    /// 2026-08-11 a closed `crm-2` from five days earlier still reported five_hour 99% while every
    /// live session sat at 19%, and 99% is the number the owner was shown. Two rules fix it — an
    /// expired window is discarded, and a newer window instance REPLACES an older one instead of
    /// competing with it on percentage.
    /// </summary>
    public static IReadOnlyList<(string Window, double Percent, DateTime? ResetsAtLocal, string Models)> Read_WorstAcrossSessions(
        IReadOnlyList<string> usageFilePaths,
        DateTime nowLocal)
    {
        Dictionary<string, (double Percent, DateTime? ResetsAtLocal, SortedSet<string> Models)> worst = [];

        foreach (var usageFile in usageFilePaths)
        {
            var rawJson = UsageTotals_Reader.Read_Text_Safe(usageFile);
            var modelName = Read_ModelName_OrNull(rawJson) ?? "unknown model";

            foreach (var window in Read_Windows(rawJson))
            {
                // An expired window is not a reading: it describes an allowance already handed back.
                if (Is_ExpiredWindow(window.ResetsAtLocal, nowLocal))
                    continue;

                if (!worst.TryGetValue(window.Window, out var known))
                {
                    worst[window.Window] = (window.Percent, window.ResetsAtLocal, [modelName]);
                    continue;
                }

                var instance = Compare_WindowInstance(window.ResetsAtLocal, known.ResetsAtLocal);

                // A LATER reset stamp is a DIFFERENT, newer window. The percentage being held
                // describes a window that has since rolled over, so it is not evidence about this
                // one — it is replaced outright rather than max-ed against.
                if (instance > 0)
                {
                    worst[window.Window] = (window.Percent, window.ResetsAtLocal, [modelName]);
                    continue;
                }

                if (instance < 0)
                    continue;

                known.Models.Add(modelName);

                // SAME window instance: usage inside one window is cumulative and account-wide, so
                // the highest reading is the one that constrains you. The stamp always travels with
                // the percent it came from.
                if (window.Percent > known.Percent)
                    worst[window.Window] = (window.Percent, window.ResetsAtLocal, known.Models);
                else
                    worst[window.Window] = (known.Percent, known.ResetsAtLocal, known.Models);
            }
        }

        List<(string Window, double Percent, DateTime? ResetsAtLocal, string Models)> results = [];

        foreach (var pair in worst.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            results.Add((pair.Key, pair.Value.Percent, pair.Value.ResetsAtLocal, string.Join(", ", pair.Value.Models)));

        return results;
    }

    /// <summary>
    /// The probe files that may speak about the CURRENT limits — i.e. those carrying at least one
    /// window that has not already reset. Probe files are never deleted, so without this the alert
    /// path folded five-day-old closed orchestrations into "the account's usage right now".
    ///
    /// It exists beside <see cref="UsageTotals_Reader.Find_AllUsageFiles"/> rather than replacing
    /// it: lifetime cost and token totals MUST keep reading every file, closed sessions included.
    /// A file with no readable window at all is dropped here — it contributes nothing to a limits
    /// reading either way.
    /// </summary>
    public static IReadOnlyList<string> Find_UsageFiles_WithLiveWindow(ISupervisionPaths paths, DateTime nowLocal)
    {
        List<string> live = [];

        foreach (var usageFile in UsageTotals_Reader.Find_AllUsageFiles(paths))
        {
            if (Has_LiveWindow(UsageTotals_Reader.Read_Text_Safe(usageFile), nowLocal))
                live.Add(usageFile);
        }

        return live;
    }

    /// <summary>True when the payload carries a window that has not already reset.</summary>
    public static bool Has_LiveWindow(string rawStatuslineJson, DateTime nowLocal)
    {
        foreach (var window in Read_Windows(rawStatuslineJson))
        {
            if (!Is_ExpiredWindow(window.ResetsAtLocal, nowLocal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A window whose reset stamp has passed is spent. An ABSENT stamp is never treated as expired:
    /// older status-line versions omit it, and guessing "stale" there would silence the reading
    /// entirely rather than merely misdate it.
    /// </summary>
    static bool Is_ExpiredWindow(DateTime? resetsAtLocal, DateTime nowLocal)
    {
        return resetsAtLocal != null && resetsAtLocal.Value <= nowLocal;
    }

    /// <summary>
    /// Orders two readings of the same window NAME by which window INSTANCE they describe:
    /// positive when the candidate is newer than the one held, negative when older, zero when they
    /// are the same instance (or neither can be dated, in which case they are treated as comparable
    /// so the old highest-wins behaviour still applies). An undated reading never displaces a dated
    /// one — it cannot be shown to be current.
    /// </summary>
    static int Compare_WindowInstance(DateTime? candidateResetsAtLocal, DateTime? knownResetsAtLocal)
    {
        if (candidateResetsAtLocal == null && knownResetsAtLocal == null)
            return 0;

        if (candidateResetsAtLocal == null)
            return -1;

        if (knownResetsAtLocal == null)
            return 1;

        return candidateResetsAtLocal.Value.CompareTo(knownResetsAtLocal.Value);
    }

    static DateTime? Read_ResetsAt_OrNull(JsonObject window)
    {
        var node = window["resets_at"];

        if (node == null)
            return null;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(node.GetValue<long>()).LocalDateTime;
        }
        catch
        {
            return null;
        }
    }

    static string Describe_Window(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "five_hour" => "5h",
            "seven_day" => "weekly",
            _ => key.Replace('_', ' '),
        };
    }
}
