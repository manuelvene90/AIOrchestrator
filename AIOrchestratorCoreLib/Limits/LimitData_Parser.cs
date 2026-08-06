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

    public static IReadOnlyDictionary<string, double> Extract_LimitPercents(string rawStatuslineJson)
    {
        Dictionary<string, double> results = [];

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

    static void Scan_Node(JsonNode node, string path, Dictionary<string, double> results)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var pair in jsonObject)
            {
                if (pair.Value == null)
                    continue;

                var childPath = path.Length == 0 ? pair.Key : $"{path}.{pair.Key}";

                if (pair.Value is JsonValue value && Try_GetPercent(pair.Key, childPath, value, out var percent))
                    results[childPath] = percent;
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
        if (number > 0 && number <= 1.0)
            number *= 100.0;

        if (number < 0 || number > 200)
            return false;

        percent = number;
        return true;
    }
}
