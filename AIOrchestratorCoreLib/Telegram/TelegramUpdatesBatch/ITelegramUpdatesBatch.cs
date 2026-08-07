using AIOrchestratorCoreLib.Telegram.TelegramCallbackTap;
using AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

namespace AIOrchestratorCoreLib.Telegram.TelegramUpdatesBatch;

/// <summary>
/// Result of parsing one getUpdates response. MaxUpdateId covers EVERY update in the response
/// (owner message or not) so the poll offset always advances past processed updates.
/// </summary>
public interface ITelegramUpdatesBatch
{
    long? MaxUpdateId { get; }
    IReadOnlyList<ITelegramOwnerMessage> OwnerMessages { get; }

    /// <summary>Inline decision-button taps by the owner.</summary>
    IReadOnlyList<ITelegramCallbackTap> CallbackTaps { get; }

    /// <summary>
    /// Ids of Telegram's own SERVICE messages ("changed the topic name/icon"), which a rename
    /// emits into the topic. The bridge deletes them so toggling a mode leaves no litter.
    /// </summary>
    IReadOnlyList<long> TopicServiceMessageIds { get; }
}
