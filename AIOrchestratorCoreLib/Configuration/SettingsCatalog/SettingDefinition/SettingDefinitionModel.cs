using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;

/// <summary>
/// One setting's definition as plain data. Built only through <see cref="SettingDefinition_Factory"/>,
/// which is where the invariants (blank label/description/path, a default of the wrong kind) are
/// refused — this model trusts what it is handed and spends its own logic entirely on
/// <see cref="Validate_OrNull"/>, the one behaviour a definition has.
/// </summary>
internal sealed class SettingDefinitionModel(
    string path,
    string? legacyPath_OrNull,
    SettingKinds kind,
    JsonNode? default_OrNull,
    IReadOnlyList<string> enumValues,
    int? minimum,
    int? maximum,
    SettingScopes scope,
    SettingCategories category,
    string label,
    string description,
    RestartKinds restart,
    SettingRenderers renderer,
    string? compositeParser_OrNull,
    string validatorName,
    bool nullable) : ISettingDefinition
{
    public string Path { get; } = path;
    public string? LegacyPath_OrNull { get; } = legacyPath_OrNull;
    public SettingKinds Kind { get; } = kind;
    public JsonNode? Default_OrNull { get; } = default_OrNull;
    public IReadOnlyList<string> EnumValues { get; } = enumValues;
    public int? Minimum { get; } = minimum;
    public int? Maximum { get; } = maximum;
    public SettingScopes Scope { get; } = scope;
    public SettingCategories Category { get; } = category;
    public string Label { get; } = label;
    public string Description { get; } = description;
    public RestartKinds Restart { get; } = restart;
    public SettingRenderers Renderer { get; } = renderer;
    public string? CompositeParser_OrNull { get; } = compositeParser_OrNull;

    public string? Validate_OrNull(JsonNode? value)
    {
        if (value == null)
            return nullable ? null : $"'{Path}' may not be empty";

        return Kind switch
        {
            SettingKinds.Bool => Validate_Bool_OrNull(value),
            SettingKinds.Enum => Validate_Enum_OrNull(value),
            SettingKinds.Int => Validate_Int_OrNull(value),
            SettingKinds.String => Validate_String_OrNull(value),
            SettingKinds.StringList => Validate_StringList_OrNull(value),
            SettingKinds.Composite => null,
            _ => throw new Exception($"Unhandled SettingKinds: {Kind}"),
        };
    }

    string? Validate_Bool_OrNull(JsonNode value)
    {
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<bool>(out _))
            return $"'{Path}' must be true or false";

        return null;
    }

    string? Validate_Enum_OrNull(JsonNode value)
    {
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var word))
            return $"'{Path}' must be one of: {string.Join(", ", EnumValues)}";

        if (!EnumValues.Contains(word))
            return $"'{word}' is not a valid value for '{Path}' — must be one of: {string.Join(", ", EnumValues)}";

        return null;
    }

    string? Validate_Int_OrNull(JsonNode value)
    {
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<int>(out var number))
            return $"'{Path}' must be a whole number";

        if (Minimum != null && number < Minimum.Value)
            return $"'{Path}' must be between {Minimum.Value} and {Maximum} — {number} is too low";

        if (Maximum != null && number > Maximum.Value)
            return $"'{Path}' must be between {Minimum} and {Maximum.Value} — {number} is too high";

        return null;
    }

    string? Validate_String_OrNull(JsonNode value)
    {
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out _))
            return $"'{Path}' must be text";

        return SettingValidators.Validate_OrNull(validatorName, value);
    }

    string? Validate_StringList_OrNull(JsonNode value)
    {
        if (value is not JsonArray array)
            return $"'{Path}' must be a list";

        foreach (var element in array)
        {
            if (element is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out _))
                return $"'{Path}' must be a list of text values";
        }

        return SettingValidators.Validate_OrNull(validatorName, value);
    }
}
