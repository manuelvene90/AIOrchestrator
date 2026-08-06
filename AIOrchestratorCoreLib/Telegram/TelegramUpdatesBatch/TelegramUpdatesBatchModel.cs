using AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

namespace AIOrchestratorCoreLib.Telegram.TelegramUpdatesBatch;

internal sealed class TelegramUpdatesBatchModel(
    long? maxUpdateId,
    IReadOnlyList<ITelegramOwnerMessage> ownerMessages) : ITelegramUpdatesBatch
{
    public long? MaxUpdateId { get; } = maxUpdateId;
    public IReadOnlyList<ITelegramOwnerMessage> OwnerMessages { get; } = ownerMessages;
}
