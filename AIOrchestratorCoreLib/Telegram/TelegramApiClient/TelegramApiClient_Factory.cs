using AIOrchestratorCoreLib.Telegram.TelegramSendBudget;

namespace AIOrchestratorCoreLib.Telegram.TelegramApiClient;

public static class TelegramApiClient_Factory
{
    /// <summary>
    /// <paramref name="budget"/> is handed in rather than built here because the SEND allowance
    /// outlives the client: it is restored from <c>.bridge-state.json</c> at start and written back
    /// on every tick, so the caller that owns that file owns the object (brief F5).
    /// </summary>
    public static ITelegramApiClient Create(string botToken, long supergroupChatId, ITelegramSendBudget budget)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            throw new ArgumentException($"Bot token must be non-empty (supergroupChatId was {supergroupChatId})");

        return new TelegramApiClientModel(botToken, supergroupChatId, budget);
    }

    /// <summary>
    /// THE HTTP TEST SEAM — not a production mode (brief F9). Every production caller uses
    /// <see cref="Create"/>, which builds the real transport; this one takes a fake handler so the
    /// suite can finally see what this client puts on the wire.
    ///
    /// <para>
    /// It is public for the same reason <c>BridgeEngine_Factory.Create_WithTelegramClient</c> is:
    /// the model is <c>internal sealed</c> and this repo has twice refused
    /// <c>InternalsVisibleTo</c>, so an additive factory overload is the in-idiom alternative to
    /// opening the assembly.
    /// </para>
    /// </summary>
    /// <param name="transport">
    /// NOT NULL, and guarded rather than trusted: the constructor reads a null transport as "use
    /// the real one", so a null slipping through here would quietly open real sockets to
    /// api.telegram.org with a test token — a hang on the 90 s timeout, with nothing anywhere
    /// saying the seam had been bypassed. The one place null must never appear is the one place
    /// it was silently accepted.
    /// <para>
    /// OWNERSHIP STAYS WITH THE CALLER: the client does not dispose a handler it was handed (it
    /// disposes only the one it built itself), so a caller may reuse one fake across two clients.
    /// </para>
    /// </param>
    public static ITelegramApiClient Create_WithTransport(
        string botToken,
        long supergroupChatId,
        ITelegramSendBudget budget,
        HttpMessageHandler transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        // The same guard Create applies. A seam that skips the production validation is a seam
        // whose tests pass on input production would refuse.
        if (string.IsNullOrWhiteSpace(botToken))
            throw new ArgumentException($"Bot token must be non-empty (supergroupChatId was {supergroupChatId})");

        return new TelegramApiClientModel(botToken, supergroupChatId, budget, transport);
    }
}
