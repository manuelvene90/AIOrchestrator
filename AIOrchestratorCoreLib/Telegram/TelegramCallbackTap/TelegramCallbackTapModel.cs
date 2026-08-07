namespace AIOrchestratorCoreLib.Telegram.TelegramCallbackTap;

internal sealed class TelegramCallbackTapModel(
    long updateId,
    string callbackQueryId,
    string data,
    long? messageThreadId,
    long? messageId) : ITelegramCallbackTap
{
    public long UpdateId { get; } = updateId;
    public string CallbackQueryId { get; } = callbackQueryId;
    public string Data { get; } = data;
    public long? MessageThreadId { get; } = messageThreadId;
    public long? MessageId { get; } = messageId;
}
