using System.Globalization;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingEditor;

/// <summary>
/// DECIDES EVERYTHING ABOUT A ROW'S CONTROL, ONCE, WHERE A TEST CAN SEE IT (plan 04 Task 9). The WPF window's
/// template selector looks its template up by <see cref="ISettingEditor.Kind"/> and binds the rest; if any of
/// what follows lived there it would be decided in a project no test can reach.
/// </summary>
public static class SettingEditor_Factory
{
    public static ISettingEditor Create_ForReading(ISettingReading reading)
    {
        var kind = Decide_Kind(reading);
        var isList = kind is SettingEditorKinds.WordPicker or SettingEditorKinds.FreeTextList;
        JsonNode?[] elements = isList && reading.Value_OrNull is JsonArray array ? array.ToArray() : [];

        return new SettingEditorModel(
            reading,
            kind,
            editText: kind is SettingEditorKinds.Number or SettingEditorKinds.Text ? Describe_EditText(reading.Value_OrNull) : string.Empty,
            rangeHint_OrNull: kind == SettingEditorKinds.Number ? Describe_Range_OrNull(reading.Definition) : null,
            isOn: kind == SettingEditorKinds.Toggle && reading.Value_OrNull is JsonValue flag && flag.TryGetValue<bool>(out var on) && on,
            currentOffer_OrNull: (kind is SettingEditorKinds.Toggle or SettingEditorKinds.Choice) && reading.OfferedValues.Contains(reading.DisplayValue)
                ? reading.DisplayValue
                : null,
            items: Describe_Items(elements),
            remainingOffers: kind == SettingEditorKinds.WordPicker ? Find_RemainingOffers(reading.OfferedValues, elements) : [],
            canReset: reading.IsEditable && reading.Origin == SettingOrigins.ConfigFile);
    }

    /// <summary>
    /// THE ONE PLACE A ROW'S CONTROL IS CHOSEN. A row that is not editable is ReadOnly whatever its renderer —
    /// the reading's <c>IsEditable</c> is the writer's own rule (P4), so a control is never drawn for a row the
    /// writer would refuse. An OrderedList splits on whether the reading offers words (P5): a picker when it
    /// does, a typed list when it does not.
    /// </summary>
    static SettingEditorKinds Decide_Kind(ISettingReading reading)
    {
        if (!reading.IsEditable)
            return SettingEditorKinds.ReadOnly;

        if (reading.Definition.Path == SettingsSnapshot_Reader.MASKED_SECRET_PATH)
            return SettingEditorKinds.Secret;

        return reading.Definition.Renderer switch
        {
            SettingRenderers.Toggle => SettingEditorKinds.Toggle,
            SettingRenderers.Choice => SettingEditorKinds.Choice,
            SettingRenderers.Number => SettingEditorKinds.Number,
            SettingRenderers.Text => SettingEditorKinds.Text,
            SettingRenderers.OrderedList => reading.OfferedValues.Count > 0 ? SettingEditorKinds.WordPicker : SettingEditorKinds.FreeTextList,
            SettingRenderers.ReadOnly => SettingEditorKinds.ReadOnly,
            _ => throw new InvalidOperationException($"Unhandled SettingRenderers: {reading.Definition.Renderer}"),
        };
    }

    /// <summary>
    /// The value's own text, for a box to hold and hand back to the parser: a string as itself, a number as its
    /// invariant JSON text, NOTHING as empty. Not <c>DisplayValue</c>: that is cut to 40 characters and reads
    /// "not set" for nothing — an Apply that sent either back would write a word the owner never typed.
    /// </summary>
    static string Describe_EditText(JsonNode? value)
    {
        if (value == null)
            return string.Empty;

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
            return text;

        return value.ToJsonString();
    }

    /// <summary>The web page's own words for a bound (<c>rangeHint</c> in <c>Web/Assets/settings.html</c>), so the two desktop surfaces read alike.</summary>
    static string? Describe_Range_OrNull(ISettingDefinition definition)
    {
        var minimum = definition.Minimum?.ToString(CultureInfo.InvariantCulture);
        var maximum = definition.Maximum?.ToString(CultureInfo.InvariantCulture);

        if (minimum != null && maximum != null)
            return $"{minimum} – {maximum}";

        if (minimum != null)
            return $"at least {minimum}";

        return maximum != null ? $"at most {maximum}" : null;
    }

    static IReadOnlyList<(string Caption, bool CanMoveUp, bool CanMoveDown)> Describe_Items(IReadOnlyList<JsonNode?> elements)
    {
        List<(string Caption, bool CanMoveUp, bool CanMoveDown)> items = [];

        for (var index = 0; index < elements.Count; index++)
            items.Add((SettingValue_Formatter.Describe_ListWord(elements[index]), index > 0, index < elements.Count - 1));

        return items;
    }

    /// <summary>What a picker can still add: an offer already in the list is not offered twice, as on the web page.</summary>
    static IReadOnlyList<string> Find_RemainingOffers(IReadOnlyList<string> offers, IReadOnlyList<JsonNode?> elements)
    {
        HashSet<string> present = new(StringComparer.Ordinal);

        foreach (var element in elements)
        {
            if (element is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var word))
                present.Add(word);
        }

        return offers.Where(offer => !present.Contains(offer)).ToArray();
    }
}
