using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Configuration;

/// <summary>
/// Persists a new repo order into config.json (the UI's drag/drop reorder). Operates on the raw
/// JSON so every other key — including ones this app version does not know about (agents edit
/// config.json at runtime) — survives untouched; only the repos array order changes. Names not in
/// the requested order keep their relative position at the end (OrderBy is stable).
/// </summary>
public static class ConfigRepos_Reorderer
{
    public static void Persist_Order(ISupervisionPaths paths, IReadOnlyList<string> repoNamesInOrder)
    {
        if (!File.Exists(paths.ConfigFile))
            return;

        var root = JsonNode.Parse(File.ReadAllText(paths.ConfigFile)) as JsonObject
            ?? throw new Exception($"config.json at '{paths.ConfigFile}' is not a JSON object — cannot reorder repos");

        if (root["repos"] is not JsonArray reposArray)
            return;

        List<JsonNode?> nodes = [.. reposArray];
        reposArray.Clear();

        foreach (var node in nodes.OrderBy(node => Get_OrderIndex(node, repoNamesInOrder)))
            reposArray.Add(node);

        File.WriteAllText(paths.ConfigFile, root.ToJsonString(JsonWriting.INDENTED));
    }

    static int Get_OrderIndex(JsonNode? node, IReadOnlyList<string> repoNamesInOrder)
    {
        var name = (node as JsonObject)?["name"]?.GetValue<string>();

        if (name == null)
            return int.MaxValue;

        for (var i = 0; i < repoNamesInOrder.Count; i++)
        {
            if (string.Equals(repoNamesInOrder[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return int.MaxValue;
    }
}
