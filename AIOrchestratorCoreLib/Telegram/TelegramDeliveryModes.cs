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

    /// <summary>
    /// Away mode's own glyph. Deliberately NOT the moon — that already means Deferred, and two
    /// different states sharing a symbol in the topic list would be worse than no symbol.
    /// </summary>
    public const string AWAY = "✈";

    /// <summary>Prefixes the topic name with the mode's glyph (Normal = the bare name).</summary>
    public static string Decorate_TopicName(string baseName, TelegramDeliveryModes mode)
    {
        return Decorate_TopicName(baseName, mode, isAway: false);
    }

    /// <summary>
    /// Away is app-wide and orthogonal to the per-topic delivery mode, so both can show at once:
    /// "✈ 🔕 crm bug" reads as "the owner is away AND this topic is silenced".
    /// </summary>
    public static string Decorate_TopicName(string baseName, TelegramDeliveryModes mode, bool isAway)
    {
        var withMode = mode switch
        {
            TelegramDeliveryModes.Normal => baseName,
            TelegramDeliveryModes.Deferred => $"{DEFERRED} {baseName}",
            TelegramDeliveryModes.Silenced => $"{SILENCED} {baseName}",
            _ => throw new Exception($"Unhandled TelegramDeliveryModes: {mode}"),
        };

        return isAway ? $"{AWAY} {withMode}" : withMode;
    }

    /// <summary>
    /// Strips every leading state glyph, so a decorated name never gets decorated twice. Loops,
    /// because a name can carry both the away glyph and a mode glyph.
    /// </summary>
    public static string Strip_Glyph(string topicName)
    {
        var stripped = topicName.Trim();

        while (Starts_WithAnyGlyph(stripped))
            stripped = stripped[Leading_GlyphLength(stripped)..].Trim();

        return stripped;
    }

    static bool Starts_WithAnyGlyph(string topicName)
    {
        return topicName.StartsWith(DEFERRED, StringComparison.Ordinal)
            || topicName.StartsWith(SILENCED, StringComparison.Ordinal)
            || topicName.StartsWith(AWAY, StringComparison.Ordinal);
    }

    static int Leading_GlyphLength(string topicName)
    {
        if (topicName.StartsWith(AWAY, StringComparison.Ordinal))
            return AWAY.Length;

        return DEFERRED.Length;
    }
}
