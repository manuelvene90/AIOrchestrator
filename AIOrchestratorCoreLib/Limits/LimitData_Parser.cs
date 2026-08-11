using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Limits;

/// <summary>
/// Tolerantly extracts usage-limit percentages from a Claude Code statusline payload (the status
/// line probe dumps the raw JSON into .usage.json files). The exact schema varies by Claude Code
/// version, so this scans for numeric percent-like fields under limit/usage-related paths instead
/// of pinning one shape. No recognizable data → empty result → the alert feature idles.
/// </summary>
public static class LimitData_Parser
{
    static readonly string[] PERCENT_FIELD_HINTS = ["percent", "pct", "utilization", "used"];
    static readonly string[] PATH_CONTEXT_HINTS = ["limit", "usage", "rate", "session", "week", "hour"];

    /// <summary>
    /// Field names that mark WHEN a window ends. Its value is the window's identity: when it
    /// changes, a different window is being described and alerts must re-arm. Same hint style as
    /// the two lists above, and the same tolerance — an unrecognised payload yields no identity
    /// rather than an error.
    /// </summary>
    static readonly string[] RESET_FIELD_HINTS = ["reset", "expires", "expiry", "until", "renew"];

    /// <summary>
    /// Shortens a limit key path for phone alerts: "rate_limits.five_hour.used_percentage" → "5h".
    /// Generic segments are dropped; known windows get compact names; unknown remainders pass
    /// through with underscores as spaces.
    /// </summary>
    public static string Build_ShortLabel(string limitKey)
    {
        string[] genericSegments = ["rate_limits", "rate", "limits", "limit", "usage", "used_percentage", "used_pct", "used", "percentage", "percent", "pct", "utilization"];

        List<string> keptSegments = [];

        foreach (var rawSegment in limitKey.Split('.'))
        {
            var segment = rawSegment.ToLowerInvariant();

            if (genericSegments.Contains(segment))
                continue;

            var compact = segment switch
            {
                "five_hour" => "5h",
                "fivehour" => "5h",
                "session" => "5h",
                "seven_day" => "weekly",
                "sevenday" => "weekly",
                "week" => "weekly",
                "weekly" => "weekly",
                _ => segment.Replace('_', ' '),
            };

            keptSegments.Add(compact);
        }

        if (keptSegments.Count == 0)
            return limitKey;

        return string.Join(' ', keptSegments);
    }

    public static IReadOnlyDictionary<string, double> Extract_LimitPercents(string rawStatuslineJson)
    {
        Dictionary<string, double> percents = [];

        foreach (var pair in Extract_LimitWindows(rawStatuslineJson))
            percents[pair.Key] = pair.Value.Percent;

        return percents;
    }

    /// <summary>
    /// The same tolerant scan, plus each window's IDENTITY where the payload offers one — the value
    /// of a reset-like field sitting beside the percentage, which changes when the window rolls
    /// over. Alerts key their de-duplication on it, so that a NEW window re-arms on identity rather
    /// than on a percentage happening to dip.
    ///
    /// Identity is null whenever it cannot be established, which is a supported outcome and not a
    /// failure: an older status line carries no reset field at all, and the caller falls back to
    /// its percentage rule rather than going quiet. It is deliberately only read when NUMERIC (a
    /// unix instant) or a parseable timestamp — an identity we cannot order is worse than none,
    /// because "is this window newer" would silently become unanswerable.
    /// </summary>
    public static IReadOnlyDictionary<string, (double Percent, double? WindowIdentity)> Extract_LimitWindows(string rawStatuslineJson)
    {
        Dictionary<string, (double Percent, double? WindowIdentity)> results = [];

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(rawStatuslineJson);
        }
        catch
        {
            return results;
        }

        if (root != null)
            Scan_Node(root, "", results);

        return results;
    }

    static void Scan_Node(JsonNode node, string path, Dictionary<string, (double Percent, double? WindowIdentity)> results)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var pair in jsonObject)
            {
                if (pair.Value == null)
                    continue;

                var childPath = path.Length == 0 ? pair.Key : $"{path}.{pair.Key}";

                // The identity comes from the percentage's OWN container: in every shape seen so
                // far the window is one object holding both ("five_hour": { used_percentage,
                // resets_at }), so the sibling is the reading's own window and not a neighbour's.
                if (pair.Value is JsonValue value && Try_GetPercent(pair.Key, childPath, value, out var percent))
                    results[childPath] = (percent, Find_WindowIdentity_OrNull(jsonObject));
                else
                    Scan_Node(pair.Value, childPath, results);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            for (var i = 0; i < jsonArray.Count; i++)
            {
                var element = jsonArray[i];

                if (element != null)
                    Scan_Node(element, $"{path}[{i}]", results);
            }
        }
    }

    static double? Find_WindowIdentity_OrNull(JsonObject container)
    {
        foreach (var pair in container)
        {
            if (pair.Value is not JsonValue value)
                continue;

            var nameLower = pair.Key.ToLowerInvariant();

            if (!RESET_FIELD_HINTS.Any(hint => nameLower.Contains(hint, StringComparison.Ordinal)))
                continue;

            if (value.TryGetValue<double>(out var numeric))
                return numeric;

            // A timestamp written as text still identifies the window; ticks keep it orderable.
            // Mixing scales across a schema change costs exactly one spurious re-arm, never silence.
            if (value.TryGetValue<string>(out var text) && DateTime.TryParse(text, out var parsed))
                return parsed.ToUniversalTime().Ticks;
        }

        return null;
    }

    static bool Try_GetPercent(string fieldName, string fullPath, JsonValue value, out double percent)
    {
        percent = 0;

        var nameLower = fieldName.ToLowerInvariant();
        if (!PERCENT_FIELD_HINTS.Any(hint => nameLower.Contains(hint, StringComparison.Ordinal)))
            return false;

        var pathLower = fullPath.ToLowerInvariant();
        if (!PATH_CONTEXT_HINTS.Any(hint => pathLower.Contains(hint, StringComparison.Ordinal)))
            return false;

        if (!value.TryGetValue<double>(out var number))
            return false;

        // Fractions (0..1) normalize to percent; already-percent values pass through.
        //
        // STRICTLY below 1, because 1 is not a fraction in this data: `"used_percentage": 1` means
        // one percent, and four sessions reporting exactly that in the current weekly window were
        // being read as 100%. That phantom pinned the alert latch at its ceiling, where it silenced
        // every genuine crossing beneath it — the reading was never stale, it was manufactured
        // (2026-08-11). A real fraction of exactly 1.0 now reads as 1%, which costs a late alert in
        // a schema nothing has ever emitted, rather than a false AT-LIMIT alert in the one we have.
        if (number > 0 && number < 1.0)
            number *= 100.0;

        if (number < 0 || number > 200)
            return false;

        percent = number;
        return true;
    }
}
