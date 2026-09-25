namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// HOW THE APP SAYS IT HAS THE OWNER'S MESSAGE — the catalogue's <c>phone.receipts</c>. Master edited a
/// ✓ receipt line under the message; the fork reacts to the owner's own message instead (brief D), so
/// no message is added to the chat at all.
/// </summary>
public enum ReceiptStyles
{
    /// <summary>A receipt line the app edits as the message moves through delivery. The shipped default.</summary>
    Ticks,

    /// <summary>A reaction on the owner's own message; nothing new is posted.</summary>
    Reactions,
}

/// <summary>Reads a <c>phone.receipts</c> word without a switch at each call site.</summary>
public static class Receipt_Styles
{
    /// <summary>The catalogue's words for <c>phone.receipts</c> — the drift between the two is pinned by <c>PhoneSettingsJsonTests</c>.</summary>
    public const string TICKS_TEXT = "ticks";
    public const string REACTIONS_TEXT = "reactions";

    /// <summary>
    /// Parses the catalogue path <c>phone.receipts</c>. Ordinal-case-insensitive; an absent, blank or
    /// UNRECOGNISED word reads as <see cref="ReceiptStyles.Ticks"/>, the catalogue's shipped default.
    /// </summary>
    public static ReceiptStyles Parse_OrTicks(string? word)
    {
        return string.Equals(word?.Trim(), REACTIONS_TEXT, StringComparison.OrdinalIgnoreCase)
            ? ReceiptStyles.Reactions
            : ReceiptStyles.Ticks;
    }
}
