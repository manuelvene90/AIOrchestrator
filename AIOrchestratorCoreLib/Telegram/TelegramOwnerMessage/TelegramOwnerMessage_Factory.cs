namespace AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

public static class TelegramOwnerMessage_Factory
{
    public static ITelegramOwnerMessage Create(
        long updateId,
        long chatId,
        long fromUserId,
        long? messageThreadId,
        string text,
        string? photoFileId)
    {
        return new TelegramOwnerMessageModel(updateId, chatId, fromUserId, messageThreadId, text, photoFileId);
    }
}
