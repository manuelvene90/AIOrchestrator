namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// Splits mirror text into Telegram-sized chunks (the API caps sendMessage text at 4096 chars).
/// Splits on line boundaries where possible; hard-splits single over-long lines.
/// </summary>
public static class TelegramMessage_Chunker
{
    public const int TELEGRAM_MAX_MESSAGE_LENGTH = 4096;

    public static IReadOnlyList<string> Chunk(string text, int maxLength = TELEGRAM_MAX_MESSAGE_LENGTH)
    {
        if (maxLength < 1)
            throw new ArgumentException($"maxLength must be >= 1, got {maxLength}");

        List<string> chunks = [];
        var remaining = text;

        while (remaining.Length > maxLength)
        {
            var splitAt = remaining.LastIndexOf('\n', maxLength - 1);

            if (splitAt <= 0)
                splitAt = maxLength;

            // NEVER BETWEEN THE TWO HALVES OF AN EMOJI (brief F4). A newline is not a surrogate, so
            // only the hard split above can land there — but it lands there unconditionally when it
            // does, and the result is a pair of lone surrogates that Telegram answers with a 400.
            // The plain-text fallback then re-sends the same broken text into the same 400, so the
            // entry wedges: exactly the loss OwnerMessage_Chunker was written to prevent, reached
            // through the one door it did not close. Costs one code unit off the chunk.
            splitAt = TelegramText_Ruler.Move_OffSurrogatePair(remaining, splitAt);

            chunks.Add(remaining[..splitAt].TrimEnd('\n', '\r'));
            remaining = remaining[splitAt..].TrimStart('\n');
        }

        if (remaining.Length > 0)
            chunks.Add(remaining);

        return chunks;
    }
}
