using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingEditor;

internal sealed class SettingEditorModel(
    ISettingReading reading,
    SettingEditorKinds kind,
    string editText,
    string? rangeHint_OrNull,
    bool isOn,
    string? currentOffer_OrNull,
    IReadOnlyList<(string Caption, bool CanMoveUp, bool CanMoveDown)> items,
    IReadOnlyList<string> remainingOffers,
    bool canReset) : ISettingEditor
{
    public ISettingReading Reading { get; } = reading;
    public SettingEditorKinds Kind { get; } = kind;
    public string EditText { get; } = editText;
    public string? RangeHint_OrNull { get; } = rangeHint_OrNull;
    public bool IsOn { get; } = isOn;
    public string? CurrentOffer_OrNull { get; } = currentOffer_OrNull;
    public IReadOnlyList<(string Caption, bool CanMoveUp, bool CanMoveDown)> Items { get; } = items;
    public IReadOnlyList<string> RemainingOffers { get; } = remainingOffers;
    public bool CanReset { get; } = canReset;

    public JsonNode? Build_FromText(string text)
    {
        return SettingValue_Parser.Parse(Reading.Definition, text);
    }

    public JsonNode? Build_FromToggle(bool isOn)
    {
        return SettingValue_Parser.Parse(Reading.Definition, isOn ? SettingValue_Formatter.ON : SettingValue_Formatter.OFF);
    }

    public JsonNode Build_Moved(int index, int delta)
    {
        var elements = Read_Elements();
        var target = index + delta;

        Require_Index(elements, index);
        Require_Index(elements, target);

        var moved = elements[index];
        elements.RemoveAt(index);
        elements.Insert(target, moved);

        return To_Array(elements);
    }

    public JsonNode Build_Removed(int index)
    {
        var elements = Read_Elements();

        Require_Index(elements, index);
        elements.RemoveAt(index);

        return To_Array(elements);
    }

    public JsonNode? Build_Added_OrNull(string text)
    {
        var elements = Read_Elements();

        if (SettingValue_Parser.Parse(Reading.Definition, text) is not JsonArray typed || typed.Count == 0)
            return null;

        foreach (var word in typed)
            elements.Add(word?.DeepClone());

        return To_Array(elements);
    }

    /// <summary>
    /// The list's own elements, cloned — never words re-read from a caption. A list edit on anything but a list
    /// is a caller's bug (the window draws list buttons only on list kinds), so it throws naming the row.
    /// </summary>
    List<JsonNode?> Read_Elements()
    {
        if (Kind is not (SettingEditorKinds.WordPicker or SettingEditorKinds.FreeTextList))
            throw new InvalidOperationException($"'{Reading.Definition.Path}' is a {Kind} row, not a list — it has no elements to edit.");

        return Reading.Value_OrNull is JsonArray array
            ? array.Select(element => element?.DeepClone()).ToList()
            : [];
    }

    void Require_Index(List<JsonNode?> elements, int index)
    {
        if (index < 0 || index >= elements.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"'{Reading.Definition.Path}' holds {elements.Count} element(s).");
    }

    static JsonArray To_Array(List<JsonNode?> elements)
    {
        var array = new JsonArray();

        foreach (var element in elements)
            array.Add(element);

        return array;
    }
}
