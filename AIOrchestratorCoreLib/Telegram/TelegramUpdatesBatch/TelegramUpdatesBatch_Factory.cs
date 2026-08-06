using AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

namespace AIOrchestratorCoreLib.Telegram.TelegramUpdatesBatch;

public static class TelegramUpdatesBatch_Factory
{
    public static ITelegramUpdatesBatch Create(
        long? maxUpdateId,
        IReadOnlyList<ITelegramOwnerMessage> ownerMessages)
    {
        return new TelegramUpdatesBatchModel(maxUpdateId, ownerMessages);
    }
}
