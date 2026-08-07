namespace AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

internal sealed class TelegramOwnerMessageModel(
    long updateId,
    long chatId,
    long fromUserId,
    long? messageThreadId,
    string text,
    string? photoFileId,
    string? voiceFileId) : ITelegramOwnerMessage
{
    public long UpdateId { get; } = updateId;
    public long ChatId { get; } = chatId;
    public long FromUserId { get; } = fromUserId;
    public long? MessageThreadId { get; } = messageThreadId;
    public string Text { get; } = text;
    public string? PhotoFileId { get; } = photoFileId;
    public string? VoiceFileId { get; } = voiceFileId;
}
