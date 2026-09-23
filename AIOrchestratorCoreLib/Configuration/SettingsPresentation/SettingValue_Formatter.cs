using System.Globalization;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation;

/// <summary>
/// ONE WAY A VALUE READS AS TEXT, because the alternative is three. A WPF label, a Telegram button caption
/// and a web table cell showing "30", "30 min" and "30 minutes" for the same row is CLAUDE.md decision 12's
/// drift wearing three hats, and unlike a formatter inside one file it cannot be found by reading any one
/// of them. Every renderer shows <c>ISettingReading.DisplayValue</c>, and this is the only thing that
/// writes it.
///
/// <para>
/// ONE PHRASE FOR "NOTHING" (ruling P19, 2026-09-23). Absent, JSON null and a blank string all read
/// <see cref="NOT_SET"/>; an empty list reads <see cref="NONE"/>. What nothing MEANS for a given row — "no
/// --effort flag", "voice notes are not transcribed", "no bar at all" — already lives in that row's
/// Description, and a path-keyed table of phrases here would be a second home for catalogue facts, found
/// out of date the first time a row was added without it.
/// </para>
/// <para>
/// BOUNDED, ALWAYS (<see cref="MAX_LENGTH"/>). The same text is a Telegram button caption, and two of the
/// kinds grow without limit in the owner's hands — a free-text list and a composite repo list. A cut list
/// says how many it hid, and a cut string ends in an ellipsis, because a reading that looked whole and was
/// not is worse than one that is visibly short. The full value is the reading's <c>Value_OrNull</c>, for a
/// renderer with room to show it.
/// </para>
/// </summary>
public static class SettingValue_Formatter
{
    /// <summary>The one phrase for an absent, null or blank value. Also a Choice's offer for "state nothing" (ruling P3).</summary>
    public const string NOT_SET = "not set";

    /// <summary>An empty list — classic's <c>general.buttons</c>, an owner's "nothing is high risk".</summary>
    public const string NONE = "none";

    public const string ON = "on";

    public const string OFF = "off";

    /// <summary>
    /// Characters, the most any reading may take. Chosen for a phone: a Telegram inline button shows about
    /// this much before it clips, and a caption the phone clips silently is the "looked whole and was not"
    /// failure this bound exists to replace with a visible one.
    /// </summary>
    public const int MAX_LENGTH = 40;

    const string ELLIPSIS = "…";

    public static string Describe(ISettingDefinition definition, JsonNode? value)
    {
        if (value == null)
            return NOT_SET;

        return definition.Kind switch
        {
            SettingKinds.Bool => Describe_Bool(value),
            SettingKinds.Enum => Describe_Text(value),
            SettingKinds.Int => Describe_Int(value),
            SettingKinds.String => Describe_Text(value),
            SettingKinds.StringList => Describe_List(value),
            SettingKinds.Composite => Describe_Composite(value),
            _ => throw new InvalidOperationException($"Unhandled SettingKinds: {definition.Kind}"),
        };
    }

    static string Describe_Bool(JsonNode value)
    {
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var flag))
            return flag ? ON : OFF;

        return Describe_Unexpected(value);
    }

    static string Describe_Int(JsonNode value)
    {
        if (value is JsonValue jsonValue && SettingDefinitionModel.Try_GetLong(jsonValue, out var number))
            return number.ToString(CultureInfo.InvariantCulture);

        return Describe_Unexpected(value);
    }

    static string Describe_Text(JsonNode value)
    {
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text))
            return Describe_Unexpected(value);

        return string.IsNullOrWhiteSpace(text) ? NOT_SET : Cut(text);
    }

    static string Describe_List(JsonNode value)
    {
        if (value is not JsonArray array)
            return Describe_Unexpected(value);

        List<string> words = [];

        foreach (var element in array)
        {
            if (element is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var word))
                words.Add(word);
            else
                words.Add(element?.ToJsonString() ?? "null");
        }

        return Describe_Words(words);
    }

    /// <summary>
    /// As many words as fit, then "+N more". A first word too long to fit alone is not cut mid-word into
    /// something that reads like a different word: the list reads as its size instead.
    /// </summary>
    static string Describe_Words(IReadOnlyList<string> words)
    {
        if (words.Count == 0)
            return NONE;

        var whole = One_Line(string.Join(", ", words));

        if (whole.Length <= MAX_LENGTH)
            return whole;

        List<string> shown = [];

        foreach (var word in words)
        {
            var hidden = words.Count - shown.Count - 1;
            var candidate = One_Line(string.Join(", ", shown.Append(word))) + (hidden > 0 ? $", +{hidden} more" : string.Empty);

            if (candidate.Length > MAX_LENGTH)
                break;

            shown.Add(word);
        }

        if (shown.Count == 0)
            return Describe_Count(words.Count);

        return $"{One_Line(string.Join(", ", shown))}, +{words.Count - shown.Count} more";
    }

    /// <summary>
    /// A STRUCTURE READS AS ITS SHAPE, never as its JSON: a list as its size ("3 entries"), an object as its
    /// scalar members ("kind: external, assembly: …"), cut to the bound. The named parser of a Composite is
    /// the authority on what the structure MEANS; this only has to say, in one line, that it is there and
    /// roughly what it holds.
    /// </summary>
    static string Describe_Composite(JsonNode value)
    {
        return value switch
        {
            JsonArray array => array.Count == 0 ? NONE : Describe_Count(array.Count),
            JsonObject obj => obj.Count == 0 ? NONE : Cut(string.Join(", ", obj.Select(member => $"{member.Key}: {Describe_Member(member.Value)}"))),
            _ => Describe_Unexpected(value),
        };
    }

    static string Describe_Member(JsonNode? member)
    {
        return member switch
        {
            null => "null",
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out var text) => text,
            JsonValue jsonValue => jsonValue.ToJsonString(),
            JsonArray array => Describe_Count(array.Count),
            _ => ELLIPSIS,
        };
    }

    static string Describe_Count(int count)
    {
        return count == 1 ? "1 entry" : $"{count.ToString(CultureInfo.InvariantCulture)} entries";
    }

    /// <summary>
    /// A value whose shape disagrees with its row's kind. The resolver never hands one over — every layer's
    /// answer passed <c>Validate_OrNull</c> — but this formatter is public, and a renderer showing a value
    /// the owner has just typed must not throw over it: the JSON text, bounded like everything else.
    /// </summary>
    static string Describe_Unexpected(JsonNode value)
    {
        return Cut(value.ToJsonString());
    }

    static string Cut(string text)
    {
        var line = One_Line(text);

        return line.Length <= MAX_LENGTH ? line : line[..(MAX_LENGTH - ELLIPSIS.Length)] + ELLIPSIS;
    }

    static string One_Line(string text)
    {
        return text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
    }
}
