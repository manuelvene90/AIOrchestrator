using AIOrchestratorCoreLib.Telegram.TelegramCallbackTap;
using AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

namespace AIOrchestratorCoreLib.Telegram.TelegramUpdatesBatch;

public static class TelegramUpdatesBatch_Factory
{
    public static ITelegramUpdatesBatch Create(
        long? maxUpdateId,
        IReadOnlyList<ITelegramOwnerMessage> ownerMessages,
        IReadOnlyList<ITelegramCallbackTap> callbackTaps)
    {
        return new TelegramUpdatesBatchModel(maxUpdateId, ownerMessages, callbackTaps);
    }
}
