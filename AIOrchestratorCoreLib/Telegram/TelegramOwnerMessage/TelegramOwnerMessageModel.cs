namespace AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

internal sealed class TelegramOwnerMessageModel(
    long updateId,
    long? messageId,
    long chatId,
    long fromUserId,
    long? messageThreadId,
    string text,
    string? photoFileId,
    string? voiceFileId,
    string? replyToText) : ITelegramOwnerMessage
{
    public long UpdateId { get; } = updateId;
    public long? MessageId { get; } = messageId;
    public long ChatId { get; } = chatId;
    public long FromUserId { get; } = fromUserId;
    public long? MessageThreadId { get; } = messageThreadId;
    public string? ReplyToText { get; } = replyToText;
    public string Text { get; } = text;
    public string? PhotoFileId { get; } = photoFileId;
    public string? VoiceFileId { get; } = voiceFileId;
}
