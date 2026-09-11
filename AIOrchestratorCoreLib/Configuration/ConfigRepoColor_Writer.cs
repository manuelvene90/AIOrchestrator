using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Configuration;

/// <summary>
/// Records a repository's assigned topic colour in config.json — brief F1.
///
/// <para>
/// ON THE RAW TREE, and for the reason <see cref="ConfigRepos_Reorderer"/> gives in full: agents
/// edit config.json at runtime, so anything that reads the file into this app's model and writes
/// the model back deletes every key this app version does not know about. Only the one repo's
/// <c>topicColor</c> is touched; everything else in the file — including the rest of that repo's
/// object — is left byte for byte.
/// </para>
/// <para>
/// AND IT NEVER THROWS. This is called from the topic-creation path, where the colour is the least
/// important thing happening: a config.json that cannot be parsed or written must cost a dot beside
/// a topic name, never the topic. The failure is reported by the return value, and the caller logs
/// it — the topic is created either way.
/// </para>
/// </summary>
public static class ConfigRepoColor_Writer
{
    /// <summary>Whether the colour reached the file. False for every reason, all of them survivable.</summary>
    public static bool Persist_Colour(ISupervisionPaths paths, string repoName, int colour)
    {
        try
        {
            if (!File.Exists(paths.ConfigFile))
                return false;

            if (JsonNode.Parse(File.ReadAllText(paths.ConfigFile)) is not JsonObject root)
                return false;

            if (root["repos"] is not JsonArray reposArray)
                return false;

            var written = false;

            foreach (var node in reposArray)
            {
                if (node is not JsonObject repoObject)
                    continue;

                if (!string.Equals(repoObject["name"]?.GetValue<string>(), repoName, StringComparison.OrdinalIgnoreCase))
                    continue;

                repoObject["topicColor"] = colour;
                written = true;
                break;
            }

            if (!written)
                return false;

            // Atomic for the same reason every other write to this file is: an interrupted one
            // leaves a zero-length config.json, which is an app with no repos and no chat id.
            Atomic_FileWriter.Write_AllText(paths.ConfigFile, root.ToJsonString(JsonWriting.INDENTED));

            return true;
        }
        catch
        {
            // Broad by intent: locked, denied, malformed — the cause changes nothing. A colour is
            // not worth failing a topic for, and the caller logs the miss.
            return false;
        }
    }
}
