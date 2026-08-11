using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Limits;

/// <summary>
/// The on-disk shape of the per-window alert latches (.limit-alerts.json), as pure text↔state so
/// the MIGRATION is testable. It is the migration that carries the risk here, not the file I/O: a
/// latch read back against the wrong window silently suppresses every alert beneath it, which is
/// exactly the failure being repaired (a stored 100 blinded the owner's weekly window at 89% and
/// climbing, 2026-08-11).
///
/// Two shapes are read. Current: <c>{"key":{"threshold":95,"window":1786953600}}</c>. Pre-identity:
/// <c>{"key":95}</c>, which is deliberately read as a latch against an UNKNOWN window — a latch
/// whose window cannot be named must never suppress an alert, so the old file re-arms on sight
/// rather than carrying its number into a window it was never about.
/// </summary>
public static class LimitAlertState_Store
{
    public const string THRESHOLD_FIELD = "threshold";
    public const string WINDOW_FIELD = "window";

    public static IReadOnlyDictionary<string, (double Threshold, double? WindowIdentity)> Parse(string rawJson)
    {
        Dictionary<string, (double Threshold, double? WindowIdentity)> state = [];

        try
        {
            if (JsonNode.Parse(rawJson) is not JsonObject root)
                return state;

            foreach (var pair in root)
            {
                if (pair.Value == null)
                    continue;

                if (pair.Value is JsonObject entry)
                    state[pair.Key] = (Read_Number_OrNull(entry[THRESHOLD_FIELD]) ?? 0, Read_Number_OrNull(entry[WINDOW_FIELD]));
                else
                    state[pair.Key] = (Read_Number_OrNull(pair.Value) ?? 0, null);
            }
        }
        catch
        {
            // Corrupt state → re-alert once, which is the safe direction to fail in.
        }

        return state;
    }

    public static string To_Json(IReadOnlyDictionary<string, (double Threshold, double? WindowIdentity)> state)
    {
        var root = new JsonObject();

        foreach (var pair in state)
        {
            var entry = new JsonObject
            {
                [THRESHOLD_FIELD] = pair.Value.Threshold,
            };

            // Omitted rather than written as null: an absent window and an unreadable one mean the
            // same thing when read back, and one representation cannot drift from the other.
            if (pair.Value.WindowIdentity != null)
                entry[WINDOW_FIELD] = pair.Value.WindowIdentity.Value;

            root[pair.Key] = entry;
        }

        return root.ToJsonString();
    }

    static double? Read_Number_OrNull(JsonNode? node)
    {
        if (node == null)
            return null;

        try
        {
            return node.GetValue<double>();
        }
        catch
        {
            return null;
        }
    }
}
