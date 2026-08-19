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
    /// FINISHED, BUT NOT YET TESTED BY THE OWNER — so do not close it. Their own workflow, made
    /// visible: they were muting a completed endeavour and then remembering, unaided, which of the
    /// muted ones still needed testing (2026-08-19).
    ///
    /// It REPLACES the silenced glyph rather than sitting beside it. /test IS mute — the delivery
    /// mode really is Silenced underneath — so drawing 🔕 🧪 together would state one fact twice,
    /// the same reasoning that makes TERMINAL replace the mode glyph rather than accompany it.
    /// </summary>
    public const string AWAITING_TEST = "🧪";

    /// <summary>
    /// Away mode's own glyph — app-wide. Deliberately NOT the moon: that already means Deferred,
    /// and two different states sharing a symbol in the topic list is worse than no symbol.
    /// </summary>
    public const string AWAY = "✈";

    /// <summary>
    /// QUIET — this ONE orchestration has stopped asking after 3 unanswered messages. Per topic on
    /// purpose: the owner may be quiet here simply because they are working in another topic.
    /// </summary>
    public const string QUIET = "🤐";

    /// <summary>
    /// TERMINAL — the owner is in THIS orchestration's terminal, so nothing is pushed and nothing
    /// blocks on a tap. It REPLACES the mode glyph rather than sitting beside it: terminal already
    /// silences the topic, and drawing 💻 🔕 together would restate one fact twice on the title bar
    /// — the presence/delivery conflation this mode exists to remove, rendered.
    /// </summary>
    public const string TERMINAL = "💻";

    /// <summary>Prefixes the topic name with the mode's glyph (Normal = the bare name).</summary>
    public static string Decorate_TopicName(string baseName, TelegramDeliveryModes mode)
    {
        return Decorate_TopicName(baseName, mode, isAway: false, isQuiet: false);
    }

    public static string Decorate_TopicName(string baseName, TelegramDeliveryModes mode, bool isAway)
    {
        return Decorate_TopicName(baseName, mode, isAway, isQuiet: false);
    }

    /// <summary>
    /// Delivery mode is per topic; away is app-wide; quiet is per topic. Mode and presence are
    /// orthogonal and show together ("✈ 🔕 crm bug"), but AWAY SUPERSEDES QUIET — away already
    /// means every orchestration has stopped asking, so showing both would be noise.
    /// </summary>
    public static string Decorate_TopicName(string baseName, TelegramDeliveryModes mode, bool isAway, bool isQuiet)
    {
        return Decorate_TopicName(baseName, mode, isAway, isQuiet, OwnerPresenceModes.Remote);
    }

    /// <summary>
    /// TERMINAL presence REPLACES the delivery glyph — it already implies silence, so showing both
    /// would say one thing twice. Away still shows: it is app-wide and about the owner's phone,
    /// which is a different fact from where they are sitting for THIS orchestration.
    /// </summary>
    public static string Decorate_TopicName(
        string baseName,
        TelegramDeliveryModes mode,
        bool isAway,
        bool isQuiet,
        OwnerPresenceModes presence,
        bool isAwaitingTest = false)
    {
        // AWAITING-TEST OUTRANKS BOTH the mode glyph and terminal presence, and that ordering is the
        // point of the state. The other two describe how messages are being delivered right now;
        // this one says DO NOT CLOSE THIS YET. Losing it because the owner happens to be sitting in
        // the terminal would hide the reminder at exactly the moment they might act on it.
        if (isAwaitingTest)
        {
            var withTest = $"{AWAITING_TEST} {baseName}";

            return isAway ? $"{AWAY} {withTest}" : withTest;
        }

        if (presence == OwnerPresenceModes.Terminal)
        {
            var withTerminal = $"{TERMINAL} {baseName}";

            return isAway ? $"{AWAY} {withTerminal}" : withTerminal;
        }

        var withMode = mode switch
        {
            TelegramDeliveryModes.Normal => baseName,
            TelegramDeliveryModes.Deferred => $"{DEFERRED} {baseName}",
            TelegramDeliveryModes.Silenced => $"{SILENCED} {baseName}",
            _ => throw new Exception($"Unhandled TelegramDeliveryModes: {mode}"),
        };

        if (isAway)
            return $"{AWAY} {withMode}";

        return isQuiet ? $"{QUIET} {withMode}" : withMode;
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
            || topicName.StartsWith(AWAITING_TEST, StringComparison.Ordinal)
            || topicName.StartsWith(SILENCED, StringComparison.Ordinal)
            || topicName.StartsWith(AWAY, StringComparison.Ordinal)
            || topicName.StartsWith(QUIET, StringComparison.Ordinal)
            || topicName.StartsWith(TERMINAL, StringComparison.Ordinal);
    }

    /// <summary>Glyphs differ in UTF-16 length (✈ is one unit, the emoji are two) — measure, don't assume.</summary>
    static int Leading_GlyphLength(string topicName)
    {
        if (topicName.StartsWith(AWAY, StringComparison.Ordinal))
            return AWAY.Length;

        if (topicName.StartsWith(QUIET, StringComparison.Ordinal))
            return QUIET.Length;

        if (topicName.StartsWith(TERMINAL, StringComparison.Ordinal))
            return TERMINAL.Length;

        if (topicName.StartsWith(AWAITING_TEST, StringComparison.Ordinal))
            return AWAITING_TEST.Length;

        if (topicName.StartsWith(SILENCED, StringComparison.Ordinal))
            return SILENCED.Length;

        return DEFERRED.Length;
    }
}
