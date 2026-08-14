using System.Globalization;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Sessions.OrchestrationMember;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;

namespace AIOrchestratorCoreLib.Sessions;

/// <summary>session.json read/write. Round-trips the full IOrchestrationSession.</summary>
public static class SessionJson_Serializer
{
    public static string Serialize(IOrchestrationSession session)
    {
        var membersArray = new JsonArray();
        foreach (var member in session.Members)
        {
            membersArray.Add(new JsonObject
            {
                ["memberId"] = member.MemberId,
                ["pid"] = member.Pid,
                ["spawnedUtc"] = member.SpawnedUtc?.ToString("O", CultureInfo.InvariantCulture),
                ["closedUtc"] = member.ClosedUtc?.ToString("O", CultureInfo.InvariantCulture),
            });
        }

        var root = new JsonObject
        {
            ["orchId"] = session.OrchId,
            ["repoName"] = session.RepoName,
            ["repoPath"] = session.RepoPath,
            ["createdUtc"] = session.CreatedUtc.ToString("O", CultureInfo.InvariantCulture),
            ["telegramTopicId"] = session.TelegramTopicId,
            ["statusLineMessageId"] = session.StatusLineMessageId,
            ["supervisorPid"] = session.SupervisorPid,
            ["supervisorSpawnedUtc"] = session.SupervisorSpawnedUtc?.ToString("O", CultureInfo.InvariantCulture),
            ["communicatorSpawnedUtc"] = session.CommunicatorSpawnedUtc?.ToString("O", CultureInfo.InvariantCulture),
            ["displayName"] = session.DisplayName,
            ["supervisorModelOverride"] = session.SupervisorModelOverride,
            ["implementerModelOverride"] = session.ImplementerModelOverride,
            ["members"] = membersArray,
            ["telegramMode"] = session.TelegramMode.ToString(),
            ["ownerPresence"] = session.OwnerPresence.ToString(),
            ["closedUtc"] = session.ClosedUtc?.ToString("O", CultureInfo.InvariantCulture),
        };

        return root.ToJsonString(JsonWriting.INDENTED);
    }

    public static IOrchestrationSession Deserialize(string json, string sourceDescription)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new Exception($"session.json at '{sourceDescription}' is not a JSON object");

        var orchId = Get_RequiredString(root, "orchId", sourceDescription);
        var repoName = Get_RequiredString(root, "repoName", sourceDescription);
        var repoPath = Get_RequiredString(root, "repoPath", sourceDescription);
        var createdUtc = DateTime.Parse(
            Get_RequiredString(root, "createdUtc", sourceDescription),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        List<IOrchestrationMember> members = [];
        if (root["members"] is JsonArray membersArray)
        {
            foreach (var node in membersArray)
            {
                if (node is not JsonObject memberObject)
                    continue;

                var memberId = Get_RequiredString(memberObject, "memberId", sourceDescription);
                var pid = Get_Int_OrNull(memberObject, "pid");
                var spawnedUtc = Get_DateTime_OrNull(memberObject, "spawnedUtc");
                var memberClosedUtc = Get_DateTime_OrNull(memberObject, "closedUtc");

                members.Add(OrchestrationMember_Factory.Create(memberId, pid, spawnedUtc, memberClosedUtc));
            }
        }

        return OrchestrationSession_Factory.Create(
            orchId,
            repoName,
            repoPath,
            createdUtc,
            Get_Long_OrNull(root, "telegramTopicId"),
            Get_Int_OrNull(root, "supervisorPid"),
            Get_DateTime_OrNull(root, "supervisorSpawnedUtc"),
            Get_DateTime_OrNull(root, "communicatorSpawnedUtc"),
            Get_String_OrNull(root, "displayName"),
            Get_String_OrNull(root, "supervisorModelOverride"),
            Get_String_OrNull(root, "implementerModelOverride"),
            members,
            Read_TelegramMode(root),
            Get_DateTime_OrNull(root, "closedUtc"),
            Get_Long_OrNull(root, "statusLineMessageId"),
            Read_OwnerPresence(root));
    }

    /// <summary>
    /// Absent means REMOTE, which is what every session written before this field says. A missing
    /// key must not read as "the owner is at the terminal" — that would suppress the awaiting-answer
    /// flag for orchestrations nobody has ever put in terminal mode.
    /// </summary>
    static Telegram.OwnerPresenceModes Read_OwnerPresence(JsonObject root)
    {
        var node = root["ownerPresence"];

        if (node != null && Enum.TryParse<Telegram.OwnerPresenceModes>(node.GetValue<string>(), out var parsed))
            return parsed;

        return Telegram.OwnerPresenceModes.Remote;
    }

    /// <summary>Reads the mode, still honouring the older boolean "telegramSilenced" key.</summary>
    static Telegram.TelegramDeliveryModes Read_TelegramMode(JsonObject root)
    {
        var modeNode = root["telegramMode"];

        if (modeNode != null && Enum.TryParse<Telegram.TelegramDeliveryModes>(modeNode.GetValue<string>(), out var parsed))
            return parsed;

        var legacySilenced = root["telegramSilenced"];

        if (legacySilenced != null && legacySilenced.GetValue<bool>())
            return Telegram.TelegramDeliveryModes.Silenced;

        return Telegram.TelegramDeliveryModes.Normal;
    }

    static string? Get_String_OrNull(JsonObject root, string key)
    {
        var node = root[key];
        if (node == null)
            return null;

        return node.GetValue<string>();
    }

    static string Get_RequiredString(JsonObject root, string key, string sourceDescription)
    {
        var node = root[key]
            ?? throw new Exception($"session.json at '{sourceDescription}' is missing required key '{key}'");

        return node.GetValue<string>();
    }

    static int? Get_Int_OrNull(JsonObject root, string key)
    {
        var node = root[key];
        if (node == null)
            return null;

        return node.GetValue<int>();
    }

    static long? Get_Long_OrNull(JsonObject root, string key)
    {
        var node = root[key];
        if (node == null)
            return null;

        return node.GetValue<long>();
    }

    static DateTime? Get_DateTime_OrNull(JsonObject root, string key)
    {
        var node = root[key];
        if (node == null)
            return null;

        return DateTime.Parse(node.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
