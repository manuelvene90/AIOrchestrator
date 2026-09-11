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

            var callbackTap = Parse_CallbackTap_OrNull(update, updateId, supergroupChatId, ownerUserId);
            if (callbackTap != null)
                callbackTaps.Add(callbackTap);

            var serviceMessageId = Parse_TopicServiceMessageId_OrNull(update, supergroupChatId);
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
    static long? Parse_TopicServiceMessageId_OrNull(JsonObject update, long supergroupChatId)
    {
        if (update["message"] is not JsonObject message)
            return null;

        // OUR SUPERGROUP OR NOTHING (brief F6), and this is the filter that matters most of the
        // three. The id this returns is fed to `deleteMessage` — so a `forum_topic_edited` from ANY
        // other chat the bot has been added to made the bridge try to delete a message in a
        // stranger's group. It would mostly fail for want of rights, which is not a defence: it is
        // an unauthorised write attempt, once per rename, in a chat nobody here has ever seen.
        // The owner-message parser has filtered on the chat id all along; these two never did.
        if (!Is_OurSupergroup(message, supergroupChatId))
            return null;

        if (message["forum_topic_edited"] == null)
            return null;

        var messageIdNode = message["message_id"];

        if (messageIdNode == null)
            return null;

        return messageIdNode.GetValue<long>();
    }

    static ITelegramCallbackTap? Parse_CallbackTap_OrNull(JsonObject update, long updateId, long supergroupChatId, long ownerUserId)
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

        // THE OWNER CHECK IS NOT THIS CHECK (brief F6). "From the owner" says WHO tapped; it says
        // nothing about WHERE, and the two are independent: the owner is a person who is in other
        // chats, and a bot added to one of them would carry their taps here. The tap's `data` is a
        // token this app minted, so a foreign tap is normally inert — but "inert because the
        // payload will not match" is a property of the registry, not a boundary, and the tap is
        // answered, counted and logged as the owner's before anything looks at the payload.
        //
        // A callback_query with no `message` at all (an inline-mode tap) has no chat to check and
        // is refused for that reason: this bridge only ever puts buttons in its own supergroup.
        if (messageObject == null || !Is_OurSupergroup(messageObject, supergroupChatId))
            return null;

        // No `?.` on messageObject from here down: it was rejected as null above, and a leftover
        // null-conditional reads as if the case were still live.
        var threadIdNode = messageObject["message_thread_id"];
        long? threadId = threadIdNode == null ? null : threadIdNode.GetValue<long>();

        var messageIdNode = messageObject["message_id"];
        long? messageId = messageIdNode == null ? null : messageIdNode.GetValue<long>();

        return TelegramCallbackTap_Factory.Create(updateId, queryId, data, threadId, messageId);
    }

    /// <summary>
    /// Whether a message object belongs to the supervision supergroup. One reading of the fact for
    /// all three parsers, rather than the two that had it and the two that did not.
    /// </summary>
    static bool Is_OurSupergroup(JsonObject message, long supergroupChatId)
    {
        if (message["chat"] is not JsonObject chat)
            return false;

        // TOLERANT, BECAUSE A THROW HERE REPLAYS THE WHOLE BATCH FOR EVER. GetValue<long>() throws
        // on a chat.id that is not a JSON number, and this predicate is now on the callback and
        // service-message paths, which never read `chat` before — so the throw surface is wider
        // than it was. Parse_OwnerMessages has no catch of its own and the inbound loop backs off
        // WITHOUT advancing the offset, so one malformed update from a proxy or gateway would be
        // re-served until someone noticed. A chat id we cannot read is not our supergroup.
        try
        {
            return chat["id"]?.GetValue<long>() == supergroupChatId;
        }
        catch (Exception)
        {
            // Broad by intent: this is a parser of untrusted external data, and the file's contract
            // is that anything it cannot read is filtered out rather than thrown over.
            return false;
        }
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
        var document = Get_Document_OrNull(message);
        var text = Get_TextOrCaption_OrNull(message);

        // A DOCUMENT IS A MESSAGE, WITH OR WITHOUT WORDS. Left out of this test, a file sent with no
        // caption made the whole update parse to nothing: no owner message, no log line, and the
        // offset moved on. The owner had sent a file and the bridge had never heard of it.
        if (text == null && photoFileId == null && voiceFileId == null && document == null)
            return null;

        if (!Is_OurSupergroup(message, supergroupChatId))
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
            Get_ReplyToText_OrNull(message, threadId),
            document: document);
    }

    /// <summary>
    /// The document's own handle, name, type and declared size.
    ///
    /// <para>
    /// FLAT, UNLIKE A PHOTO. A photo arrives as an ARRAY of sizes and the largest is the last entry
    /// (<see cref="Get_LargestPhotoFileId_OrNull"/>); a document has one `file_id` directly on the
    /// object, like a voice note. Nothing about the photo shape transfers.
    /// </para>
    /// </summary>
    static TelegramOwnerMessage.TelegramDocumentRef? Get_Document_OrNull(JsonObject message)
    {
        if (message["document"] is not JsonObject document)
            return null;

        var fileId = document["file_id"]?.GetValue<string>();

        // A document object with no file_id is nothing anyone can download. Treated as absent
        // rather than as an empty document, so the caller's null check is the only test needed.
        if (string.IsNullOrWhiteSpace(fileId))
            return null;

        return new TelegramOwnerMessage.TelegramDocumentRef
        {
            FileId = fileId,
            FileName = document["file_name"]?.GetValue<string>(),
            MimeType = document["mime_type"]?.GetValue<string>(),
            SizeBytes = document["file_size"] == null ? null : document["file_size"]!.GetValue<long>(),
        };
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
