namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// How a topic's outbound traffic is treated. The distinction is the whole point: DEFERRED keeps
/// everything and replays it later (the owner is away), SILENCED throws it away (the owner is
/// reading the same content live in the terminal and does not want it twice).
/// </summary>
public enum TelegramDeliveryModes
{
    /// <summary>Messages are texted as they happen.</summary>
    Normal,

    /// <summary>Do-Not-Disturb: nothing is texted and NOTHING IS LOST — it arrives on the next Normal tick.</summary>
    Deferred,

    /// <summary>Dropped outright while it lasts; the channel files remain the record.</summary>
    Silenced,
}

/// <summary>Topic-name decoration so the owner sees a topic's mode in the Telegram topic list.</summary>
public static class TelegramDeliveryMode_Glyphs
{
    public const string DEFERRED = "🌙";
    public const string SILENCED = "🔕";

    /// <summary>Prefixes the topic name with the mode's glyph (Normal = the bare name).</summary>
    public static string Decorate_TopicName(string baseName, TelegramDeliveryModes mode)
    {
        return mode switch
        {
            TelegramDeliveryModes.Normal => baseName,
            TelegramDeliveryModes.Deferred => $"{DEFERRED} {baseName}",
            TelegramDeliveryModes.Silenced => $"{SILENCED} {baseName}",
            _ => throw new Exception($"Unhandled TelegramDeliveryModes: {mode}"),
        };
    }

    /// <summary>Strips any mode glyph, so a decorated name never gets decorated twice.</summary>
    public static string Strip_Glyph(string topicName)
    {
        var stripped = topicName.Trim();

        if (stripped.StartsWith(DEFERRED, StringComparison.Ordinal) || stripped.StartsWith(SILENCED, StringComparison.Ordinal))
            return stripped[DEFERRED.Length..].Trim();

        return stripped;
    }
}
