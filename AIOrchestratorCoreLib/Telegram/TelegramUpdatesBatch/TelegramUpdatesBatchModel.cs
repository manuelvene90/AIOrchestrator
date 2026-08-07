using AIOrchestratorCoreLib.Telegram.TelegramCallbackTap;
using AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

namespace AIOrchestratorCoreLib.Telegram.TelegramUpdatesBatch;

internal sealed class TelegramUpdatesBatchModel(
    long? maxUpdateId,
    IReadOnlyList<ITelegramOwnerMessage> ownerMessages,
    IReadOnlyList<ITelegramCallbackTap> callbackTaps,
    IReadOnlyList<long> topicServiceMessageIds) : ITelegramUpdatesBatch
{
    public long? MaxUpdateId { get; } = maxUpdateId;
    public IReadOnlyList<ITelegramOwnerMessage> OwnerMessages { get; } = ownerMessages;
    public IReadOnlyList<ITelegramCallbackTap> CallbackTaps { get; } = callbackTaps;
    public IReadOnlyList<long> TopicServiceMessageIds { get; } = topicServiceMessageIds;
}
