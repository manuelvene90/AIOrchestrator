namespace AIOrchestratorCoreLib.Telegram.TelegramCallbackTap;

public static class TelegramCallbackTap_Factory
{
    public static ITelegramCallbackTap Create(long updateId, string callbackQueryId, string data, long? messageThreadId, long? messageId)
    {
        return new TelegramCallbackTapModel(updateId, callbackQueryId, data, messageThreadId, messageId);
    }
}
