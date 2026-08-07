using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;

namespace AIOrchestratorCoreLib.Usage;

/// <summary>
/// Makes usage totals honest across respawns. Each session's statusline overwrites its
/// .usage.json with CURRENT-SESSION totals, so every respawn silently reset the orchestration's
/// displayed cost/tokens. This accumulator keeps per-source baselines in the orchestration's
/// .usage-lifetime.json: when a source's token count DROPS (the reset signal — tokens only grow
/// within a session), the last-seen values are folded into the baseline. Lifetime = baseline +
/// current. Called from the UI refresh; failures degrade to current-session values, never throw.
/// </summary>
public static class UsageLifetime_Accumulator
{
    const string STATE_FILE_NAME = ".usage-lifetime.json";

    public static (double Cost, long Tokens) Accumulate(string orchestrationFolder, string sourceKey, double currentCost, long currentTokens)
    {
        try
        {
            var stateFile = Path.Combine(orchestrationFolder, STATE_FILE_NAME);
            var root = Load_State(stateFile);

            if (root[sourceKey] is not JsonObject entry)
            {
                entry = new JsonObject();
                root[sourceKey] = entry;
            }

            var baselineCost = entry["baselineCost"]?.GetValue<double>() ?? 0.0;
            var baselineTokens = entry["baselineTokens"]?.GetValue<long>() ?? 0L;
            var lastCost = entry["lastCost"]?.GetValue<double>() ?? 0.0;
            var lastTokens = entry["lastTokens"]?.GetValue<long>() ?? 0L;

            // Tokens only grow within one session — a drop means the session was respawned and
            // the file now shows the NEW session's totals. Fold the dead session into the baseline.
            if (currentTokens < lastTokens)
            {
                baselineCost += lastCost;
                baselineTokens += lastTokens;
            }

            var changed = currentCost != lastCost || currentTokens != lastTokens
                || entry["baselineCost"] == null;

            entry["baselineCost"] = baselineCost;
            entry["baselineTokens"] = baselineTokens;
            entry["lastCost"] = currentCost;
            entry["lastTokens"] = currentTokens;

            if (changed)
                File.WriteAllText(stateFile, root.ToJsonString(JsonWriting.INDENTED));

            return (baselineCost + currentCost, baselineTokens + currentTokens);
        }
        catch
        {
            return (currentCost, currentTokens);
        }
    }

    static JsonObject Load_State(string stateFile)
    {
        try
        {
            if (!File.Exists(stateFile))
                return new JsonObject();

            return JsonNode.Parse(File.ReadAllText(stateFile)) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }
}
