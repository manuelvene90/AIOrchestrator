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
}
