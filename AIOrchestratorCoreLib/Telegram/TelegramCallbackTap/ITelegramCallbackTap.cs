namespace AIOrchestratorCoreLib.Telegram.TelegramCallbackTap;

/// <summary>The owner tapped an inline decision button — a callback_query from getUpdates.</summary>
public interface ITelegramCallbackTap
{
    long UpdateId { get; }

    /// <summary>Must be answered (answerCallbackQuery) or the button spinner hangs on the phone.</summary>
    string CallbackQueryId { get; }

    /// <summary>The button's callback_data — resolved against the engine's in-memory option registry.</summary>
    string Data { get; }

    /// <summary>Topic the button message lives in; null outside topics.</summary>
    long? MessageThreadId { get; }
}
