namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// WHERE THE DELIVERY-MODE GLYPHS (🌙 🔕 ✈ 🤐 💻) ARE DRAWN — the catalogue's <c>topic.modeGlyphs</c>.
///
/// <para>
/// The fork moved them off the topic NAME because every rename is an <c>editForumTopic</c> call and
/// every rename writes a service message into the thread: an app-wide mode change wrote a line into
/// every one of the owner's topics to tell them something they had just done. In PULSE's header the
/// same fact costs one silent edit of a message that was being edited anyway (spec §7.4). Master kept
/// them on the name, where they are visible from the topic list without opening anything.
/// </para>
/// </summary>
public enum ModeGlyphPlacements
{
    /// <summary>On the topic name, as master drew them.</summary>
    Name,

    /// <summary>In PULSE's header line. The shipped default.</summary>
    PulseHeader,
}

/// <summary>Reads a <c>topic.modeGlyphs</c> word without a switch at each call site.</summary>
public static class ModeGlyph_Placements
{
    /// <summary>The catalogue's words for <c>topic.modeGlyphs</c> — the drift between the two is pinned by <c>PhoneSettingsJsonTests</c>.</summary>
    public const string NAME_TEXT = "name";
    public const string PULSE_HEADER_TEXT = "pulseHeader";

    /// <summary>
    /// Parses the catalogue path <c>topic.modeGlyphs</c>. Ordinal-case-insensitive; an absent, blank
    /// or UNRECOGNISED word reads as <see cref="ModeGlyphPlacements.PulseHeader"/>, the catalogue's
    /// shipped default.
    /// </summary>
    public static ModeGlyphPlacements Parse_OrPulseHeader(string? word)
    {
        return string.Equals(word?.Trim(), NAME_TEXT, StringComparison.OrdinalIgnoreCase)
            ? ModeGlyphPlacements.Name
            : ModeGlyphPlacements.PulseHeader;
    }
}
