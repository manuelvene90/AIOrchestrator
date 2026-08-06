using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;
using AIOrchestratorCoreLib.Telegram.TelegramUpdatesBatch;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// Parses a getUpdates JSON response and filters it down to the OWNER's text messages in the
/// supervision supergroup. Everything else still advances MaxUpdateId so the offset moves on.
/// </summary>
public static class TelegramUpdates_Parser
{
    public static ITelegramUpdatesBatch Parse_OwnerMessages(
        string updatesJson,
        long supergroupChatId,
        long ownerUserId)
    {
        List<ITelegramOwnerMessage> ownerMessages = [];
        long? maxUpdateId = null;

        if (JsonNode.Parse(updatesJson) is not JsonObject root)
            return TelegramUpdatesBatch_Factory.Create(null, ownerMessages);

        if (root["result"] is not JsonArray updates)
            return TelegramUpdatesBatch_Factory.Create(null, ownerMessages);

        foreach (var updateNode in updates)
        {
            if (updateNode is not JsonObject update)
                continue;

            var updateIdNode = update["update_id"];
            if (updateIdNode == null)
                continue;

            var updateId = updateIdNode.GetValue<long>();

            if (maxUpdateId == null || updateId > maxUpdateId.Value)
                maxUpdateId = updateId;

            var ownerMessage = Parse_OwnerMessage_OrNull(update, updateId, supergroupChatId, ownerUserId);
            if (ownerMessage != null)
                ownerMessages.Add(ownerMessage);
        }

        return TelegramUpdatesBatch_Factory.Create(maxUpdateId, ownerMessages);
    }

    static ITelegramOwnerMessage? Parse_OwnerMessage_OrNull(
        JsonObject update,
        long updateId,
        long supergroupChatId,
        long ownerUserId)
    {
        if (update["message"] is not JsonObject message)
            return null;

        var photoFileId = Get_LargestPhotoFileId_OrNull(message);
        var text = Get_TextOrCaption_OrNull(message);

        if (text == null && photoFileId == null)
            return null;

        if (message["chat"] is not JsonObject chat)
            return null;

        var chatIdNode = chat["id"];
        if (chatIdNode == null || chatIdNode.GetValue<long>() != supergroupChatId)
            return null;

        if (message["from"] is not JsonObject from)
            return null;

        var fromIdNode = from["id"];
        if (fromIdNode == null || fromIdNode.GetValue<long>() != ownerUserId)
            return null;

        var threadIdNode = message["message_thread_id"];
        long? threadId = threadIdNode == null ? null : threadIdNode.GetValue<long>();

        return TelegramOwnerMessage_Factory.Create(
            updateId,
            supergroupChatId,
            ownerUserId,
            threadId,
            text ?? string.Empty,
            photoFileId);
    }

    static string? Get_TextOrCaption_OrNull(JsonObject message)
    {
        var textNode = message["text"];
        if (textNode != null)
            return textNode.GetValue<string>();

        var captionNode = message["caption"];
        if (captionNode != null)
            return captionNode.GetValue<string>();

        return null;
    }

    /// <summary>Telegram sends photos as an array of sizes, smallest first — the last is full resolution.</summary>
    static string? Get_LargestPhotoFileId_OrNull(JsonObject message)
    {
        if (message["photo"] is not JsonArray photoSizes || photoSizes.Count == 0)
            return null;

        if (photoSizes[photoSizes.Count - 1] is not JsonObject largest)
            return null;

        return largest["file_id"]?.GetValue<string>();
    }
}
