using System.Globalization;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation;

/// <summary>
/// ONE WAY TYPED TEXT BECOMES A VALUE (ruling P13, 2026-09-23). Two renderers take text the owner typed —
/// the Telegram reply step (plan 04 Task 5) and the WPF window's number and text boxes (Task 9) — and each
/// would otherwise grow its own idea of what "12" or "a, b" is (CLAUDE.md decision 12). The web page does
/// not come through here: a PUT body is already JSON.
///
/// <para>
/// IT NEVER REFUSES (decision 21). What it cannot read as the row's kind it passes through RAW, as a JSON
/// string, so the definition refuses it — "'buttonExpiryMinutes' must be a whole number" — in the only words
/// a refusal may be in. A parser that answered "that is not a number" itself would be a second validator
/// with its own messages, and the Telegram and WPF refusals for one typo would stop matching.
/// </para>
/// <para>
/// INVARIANT CULTURE. The owner types from an Italian phone, where "1.000" is a thousand and "1,5" is one and
/// a half; neither is a whole number to the catalogue, and a culture-aware parse would quietly make one of
/// them so on one machine and not on another.
/// </para>
/// <para>
/// IT IS THE FORMATTER'S INVERSE FOR EVERY OFFER. <see cref="SettingValue_Formatter"/>'s words parse back to
/// what they describe — "on"/"off" to a bool, and <see cref="SettingValue_Formatter.NOT_SET"/> to JSON null
/// on a row that accepts null (ruling P3: the stated "nothing", never a Reset) — so a renderer can carry the
/// very caption it drew and hand it here. Whether a row accepts null is read off the definition itself
/// (<c>Validate_OrNull(null) == null</c>, ruling P19), never from a list kept here.
/// </para>
/// </summary>
public static class SettingValue_Parser
{
    const char LIST_SEPARATOR = ',';

    public static JsonNode? Parse(ISettingDefinition definition, string text)
    {
        if (Means_Nothing(text) && Accepts_Null(definition))
            return null;

        return definition.Kind switch
        {
            SettingKinds.Bool => Parse_Bool(text),
            SettingKinds.Int => Parse_Int(text),
            SettingKinds.Enum => JsonValue.Create(text.Trim()),
            SettingKinds.StringList => Parse_List(text),

            // FREE TEXT IS THE OWNER'S EXACTLY AS TYPED: a command line's spacing is not a parser's to tidy,
            // and a Composite is refused by the writer before any value is looked at (D12).
            SettingKinds.String => JsonValue.Create(text),
            SettingKinds.Composite => JsonValue.Create(text),
            _ => throw new InvalidOperationException($"Unhandled SettingKinds: {definition.Kind}"),
        };
    }

    /// <summary>Blank, or the formatter's own phrase for nothing, in any case — a phone capitalises the first letter.</summary>
    static bool Means_Nothing(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            || string.Equals(text.Trim(), SettingValue_Formatter.NOT_SET, StringComparison.OrdinalIgnoreCase);
    }

    static bool Accepts_Null(ISettingDefinition definition)
    {
        return definition.Validate_OrNull(null) == null;
    }

    static JsonNode Parse_Bool(string text)
    {
        var word = text.Trim();

        if (string.Equals(word, SettingValue_Formatter.ON, StringComparison.OrdinalIgnoreCase) || (bool.TryParse(word, out var isTrue) && isTrue))
            return JsonValue.Create(true);

        if (string.Equals(word, SettingValue_Formatter.OFF, StringComparison.OrdinalIgnoreCase) || (bool.TryParse(word, out var isFalse) && !isFalse))
            return JsonValue.Create(false);

        return JsonValue.Create(text);
    }

    /// <summary>
    /// A LONG, because three Int rows carry values past Int32 (a supergroup chat id). AllowLeadingSign and
    /// nothing else, after a trim: a thousands separator or a decimal point stays raw, for the definition
    /// to refuse as not a whole number.
    /// </summary>
    static JsonNode Parse_Int(string text)
    {
        if (long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            return JsonValue.Create(number);

        return JsonValue.Create(text);
    }

    /// <summary>
    /// Commas separate, each word is trimmed, and a blank word — a stray or trailing comma — is dropped.
    /// Blank text is the EMPTY list, which is a value (classic's <c>general.buttons</c>: no bar at all), not
    /// an absence. A word containing a comma cannot be typed this way; no list row in the catalogue has one.
    /// </summary>
    static JsonNode Parse_List(string text)
    {
        var array = new JsonArray();

        foreach (var word in text.Split(LIST_SEPARATOR))
        {
            var trimmed = word.Trim();

            if (trimmed.Length > 0)
                array.Add(JsonValue.Create(trimmed));
        }

        return array;
    }
}
