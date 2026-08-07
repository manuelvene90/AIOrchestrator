using System.Text.Json.Nodes;
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
        Dictionary<string, (double Percent, DateTime? ResetsAtLocal, SortedSet<string> Models)> worst = [];

        foreach (var usageFile in usageFilePaths)
        {
            var rawJson = UsageTotals_Reader.Read_Text_Safe(usageFile);
            var modelName = Read_ModelName_OrNull(rawJson) ?? "unknown model";

            foreach (var window in Read_Windows(rawJson))
            {
                if (!worst.TryGetValue(window.Window, out var known))
                {
                    worst[window.Window] = (window.Percent, window.ResetsAtLocal, [modelName]);
                    continue;
                }

                known.Models.Add(modelName);

                // Rate limits are account-wide, so readings agree; keep the highest anyway — a
                // stale probe file must never make the report look rosier than reality.
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
