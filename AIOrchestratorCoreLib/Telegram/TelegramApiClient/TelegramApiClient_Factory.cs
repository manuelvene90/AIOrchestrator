namespace AIOrchestratorCoreLib.Telegram.TelegramApiClient;

public static class TelegramApiClient_Factory
{
    public static ITelegramApiClient Create(string botToken, long supergroupChatId)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            throw new ArgumentException($"Bot token must be non-empty (supergroupChatId was {supergroupChatId})");

        return new TelegramApiClientModel(botToken, supergroupChatId);
    }
}
