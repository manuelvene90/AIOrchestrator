using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Telegram.TelegramCallbackTap;
using AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;
using AIOrchestratorCoreLib.Telegram.TelegramUpdatesBatch;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// Parses a getUpdates JSON response and filters it down to the OWNER's traffic in the
/// supervision supergroup: text/photo/voice messages plus inline-button taps (callback_query).
/// Everything else still advances MaxUpdateId so the offset moves on.
/// </summary>
public static class TelegramUpdates_Parser
{
    public static ITelegramUpdatesBatch Parse_OwnerMessages(
        string updatesJson,
        long supergroupChatId,
        long ownerUserId)
    {
        List<ITelegramOwnerMessage> ownerMessages = [];
        List<ITelegramCallbackTap> callbackTaps = [];
        List<long> topicServiceMessageIds = [];
        long? maxUpdateId = null;

        if (JsonNode.Parse(updatesJson) is not JsonObject root)
            return TelegramUpdatesBatch_Factory.Create(null, ownerMessages, callbackTaps, topicServiceMessageIds);

        if (root["result"] is not JsonArray updates)
            return TelegramUpdatesBatch_Factory.Create(null, ownerMessages, callbackTaps, topicServiceMessageIds);

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

            var callbackTap = Parse_CallbackTap_OrNull(update, updateId, ownerUserId);
            if (callbackTap != null)
                callbackTaps.Add(callbackTap);

            var serviceMessageId = Parse_TopicServiceMessageId_OrNull(update);
            if (serviceMessageId != null)
                topicServiceMessageIds.Add(serviceMessageId.Value);
        }

        return TelegramUpdatesBatch_Factory.Create(maxUpdateId, ownerMessages, callbackTaps, topicServiceMessageIds);
    }

    /// <summary>
    /// Telegram posts a service message into a topic whenever its name or icon changes. The
    /// bridge renames topics to show their delivery mode, so those notices are OUR noise to clean
    /// up — this returns their ids for deletion.
    /// </summary>
    static long? Parse_TopicServiceMessageId_OrNull(JsonObject update)
    {
        if (update["message"] is not JsonObject message)
            return null;

        if (message["forum_topic_edited"] == null)
            return null;

        var messageIdNode = message["message_id"];

        if (messageIdNode == null)
            return null;

        return messageIdNode.GetValue<long>();
    }

    static ITelegramCallbackTap? Parse_CallbackTap_OrNull(JsonObject update, long updateId, long ownerUserId)
    {
        if (update["callback_query"] is not JsonObject callbackQuery)
            return null;

        var queryId = callbackQuery["id"]?.GetValue<string>();
        var data = callbackQuery["data"]?.GetValue<string>();

        if (queryId == null || data == null)
            return null;

        var fromIdNode = (callbackQuery["from"] as JsonObject)?["id"];
        if (fromIdNode == null || fromIdNode.GetValue<long>() != ownerUserId)
            return null;

        var messageObject = callbackQuery["message"] as JsonObject;

        var threadIdNode = messageObject?["message_thread_id"];
        long? threadId = threadIdNode == null ? null : threadIdNode.GetValue<long>();

        var messageIdNode = messageObject?["message_id"];
        long? messageId = messageIdNode == null ? null : messageIdNode.GetValue<long>();

        return TelegramCallbackTap_Factory.Create(updateId, queryId, data, threadId, messageId);
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
        var voiceFileId = (message["voice"] as JsonObject)?["file_id"]?.GetValue<string>();
        var text = Get_TextOrCaption_OrNull(message);

        if (text == null && photoFileId == null && voiceFileId == null)
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

        var messageIdNode = message["message_id"];

        return TelegramOwnerMessage_Factory.Create(
            updateId,
            messageIdNode?.GetValue<long>(),
            supergroupChatId,
            ownerUserId,
            threadId,
            text ?? string.Empty,
            photoFileId,
            voiceFileId,
            Get_ReplyToText_OrNull(message, threadId));
    }

    /// <summary>
    /// The text of the message this one replies to, or null when it is not a real reply.
    ///
    /// THE THREAD-ROOT TEST IS THE WHOLE FUNCTION. In a forum supergroup Telegram attaches a
    /// `reply_to_message` to EVERY message in a topic, pointing at the topic's root — so reading the
    /// field at face value would tag every message the owner ever sends with the same phantom quote.
    /// The root's message_id IS the thread id, so a target equal to the thread id is Telegram's
    /// bookkeeping and a target different from it is the owner actually pointing at something.
    ///
    /// Falls back to the caption, so replying to a photo carries what the photo said.
    /// </summary>
    static string? Get_ReplyToText_OrNull(JsonObject message, long? threadId)
    {
        if (message["reply_to_message"] is not JsonObject repliedTo)
            return null;

        var repliedToIdNode = repliedTo["message_id"];

        if (repliedToIdNode != null && threadId != null && repliedToIdNode.GetValue<long>() == threadId.Value)
            return null;

        var text = Get_TextOrCaption_OrNull(repliedTo);

        return string.IsNullOrWhiteSpace(text) ? null : text;
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
