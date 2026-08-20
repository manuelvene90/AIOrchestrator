namespace AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

public static class TelegramOwnerMessage_Factory
{
    public static ITelegramOwnerMessage Create(
        long updateId,
        long? messageId,
        long chatId,
        long fromUserId,
        long? messageThreadId,
        string text,
        string? photoFileId,
        string? voiceFileId,

        // Trailing and optional: every existing caller predates replies and means "not a reply".
        string? replyToText = null)
    {
        return new TelegramOwnerMessageModel(updateId, messageId, chatId, fromUserId, messageThreadId, text, photoFileId, voiceFileId, replyToText);
    }
}
