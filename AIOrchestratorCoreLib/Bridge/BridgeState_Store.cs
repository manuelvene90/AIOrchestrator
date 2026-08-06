using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// Persists bridge progress (.bridge-state.json): per-file mirror offsets and the last processed
/// Telegram update id, so an app restart neither re-mirrors old entries nor re-routes old messages.
/// </summary>
public static class BridgeState_Store
{
    public static (IReadOnlyDictionary<string, long> FileOffsets, long LastUpdateId) Load_OrEmpty(ISupervisionPaths paths)
    {
        Dictionary<string, long> offsets = [];

        if (!File.Exists(paths.BridgeStateFile))
            return (offsets, 0L);

        var text = File.ReadAllText(paths.BridgeStateFile);
        if (string.IsNullOrWhiteSpace(text))
            return (offsets, 0L);

        if (JsonNode.Parse(text) is not JsonObject root)
            return (offsets, 0L);

        if (root["fileOffsets"] is JsonObject offsetsObject)
        {
            foreach (var pair in offsetsObject)
            {
                if (pair.Value != null)
                    offsets[pair.Key] = pair.Value.GetValue<long>();
            }
        }

        var lastUpdateIdNode = root["lastUpdateId"];
        var lastUpdateId = lastUpdateIdNode == null ? 0L : lastUpdateIdNode.GetValue<long>();

        return (offsets, lastUpdateId);
    }

    public static void Save(ISupervisionPaths paths, IReadOnlyDictionary<string, long> fileOffsets, long lastUpdateId)
    {
        Directory.CreateDirectory(paths.Root);

        var offsetsObject = new JsonObject();
        foreach (var pair in fileOffsets)
            offsetsObject[pair.Key] = pair.Value;

        var root = new JsonObject
        {
            ["fileOffsets"] = offsetsObject,
            ["lastUpdateId"] = lastUpdateId,
        };

        File.WriteAllText(paths.BridgeStateFile, root.ToJsonString(JsonWriting.INDENTED));
    }
}
