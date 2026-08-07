using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Configuration;

/// <summary>
/// Loads and saves the orchestrator configuration. Non-secret settings live in config.json;
/// the bot token lives in secrets.json so the config file can be shared/backed up freely.
/// </summary>
public static class OrchestratorConfig_Loader
{
    public static IOrchestratorConfig Load_OrEmpty(ISupervisionPaths paths)
    {
        var configRoot = Read_JsonObject_OrNull(paths.ConfigFile);
        var secretsRoot = Read_JsonObject_OrNull(paths.SecretsFile);

        if (configRoot == null && secretsRoot == null)
            return OrchestratorConfig_Factory.Create_Empty();

        var repos = Parse_Repos(configRoot);

        return OrchestratorConfig_Factory.Create(
            repos,
            Get_String_OrNull(configRoot, "supervisorModel"),
            Get_String_OrNull(configRoot, "implementerModel"),
            Get_String_OrNull(configRoot, "generalSupervisorModel"),
            Get_String_OrNull(configRoot, "communicatorModel"),
            Get_Long_OrNull(configRoot, "telegramSupergroupChatId"),
            Get_Long_OrNull(configRoot, "telegramOwnerUserId"),
            Get_String_OrNull(secretsRoot, "telegramBotToken"),
            Get_Bool_OrNull(configRoot, "telegramItalianLayer"),
            Get_String_OrNull(configRoot, "voiceTranscribeCommand"));
    }

    public static void Save(IOrchestratorConfig config, ISupervisionPaths paths)
    {
        Directory.CreateDirectory(paths.Root);

        var reposArray = new JsonArray();
        foreach (var repo in config.Repos)
        {
            reposArray.Add(new JsonObject
            {
                ["name"] = repo.Name,
                ["path"] = repo.Path,
            });
        }

        var configRoot = new JsonObject
        {
            ["repos"] = reposArray,
            ["supervisorModel"] = config.SupervisorModel,
            ["implementerModel"] = config.ImplementerModel,
            ["generalSupervisorModel"] = config.GeneralSupervisorModel,
            ["communicatorModel"] = config.CommunicatorModel,
            ["telegramSupergroupChatId"] = config.TelegramSupergroupChatId,
            ["telegramOwnerUserId"] = config.TelegramOwnerUserId,
            ["telegramItalianLayer"] = config.TelegramItalianLayer,
            ["voiceTranscribeCommand"] = config.VoiceTranscribeCommand,
        };

        File.WriteAllText(paths.ConfigFile, configRoot.ToJsonString(JsonWriting.INDENTED));

        var secretsRoot = new JsonObject
        {
            ["telegramBotToken"] = config.TelegramBotToken,
        };

        File.WriteAllText(paths.SecretsFile, secretsRoot.ToJsonString(JsonWriting.INDENTED));
    }

    static JsonObject? Read_JsonObject_OrNull(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var text = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return JsonNode.Parse(text) as JsonObject;
    }

    static IReadOnlyList<IRepoEntry> Parse_Repos(JsonObject? configRoot)
    {
        List<IRepoEntry> repos = [];

        if (configRoot == null)
            return repos;

        if (configRoot["repos"] is not JsonArray reposArray)
            return repos;

        foreach (var node in reposArray)
        {
            if (node is not JsonObject repoObject)
                continue;

            var name = Get_String_OrNull(repoObject, "name");
            var path = Get_String_OrNull(repoObject, "path");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
                continue;

            repos.Add(RepoEntry_Factory.Create(name, path));
        }

        return repos;
    }

    static string? Get_String_OrNull(JsonObject? root, string key)
    {
        if (root == null)
            return null;

        var node = root[key];
        if (node == null)
            return null;

        return node.GetValue<string?>();
    }

    static long? Get_Long_OrNull(JsonObject? root, string key)
    {
        if (root == null)
            return null;

        var node = root[key];
        if (node == null)
            return null;

        return node.GetValue<long>();
    }

    static bool? Get_Bool_OrNull(JsonObject? root, string key)
    {
        if (root == null)
            return null;

        var node = root[key];
        if (node == null)
            return null;

        return node.GetValue<bool>();
    }
}
