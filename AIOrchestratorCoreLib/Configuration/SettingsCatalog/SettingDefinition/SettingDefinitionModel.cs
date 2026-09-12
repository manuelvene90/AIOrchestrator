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
            _ => throw new InvalidOperationException($"Unhandled SettingKinds: {Kind}"),
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

    /// <summary>
    /// Reads as <see cref="long"/>, not <see cref="int"/>: three Kind=Int rows carry values past
    /// Int32 (a Telegram supergroup chat id looks like <c>-1001234567890</c>) and would otherwise be
    /// refused by their own catalogue entry. <see cref="Minimum"/>/<see cref="Maximum"/> stay
    /// <c>int?</c> — every ranged Int row in the catalogue has Int32 bounds — so the comparison
    /// widens the bound to long rather than the other way around.
    /// </summary>
    string? Validate_Int_OrNull(JsonNode value)
    {
        if (value is not JsonValue jsonValue || !Try_GetLong(jsonValue, out var number))
            return $"'{Path}' must be a whole number";

        if (Minimum != null && number < (long)Minimum.Value)
            return $"'{Path}' must be {Describe_Range()} — {number} is too low";

        if (Maximum != null && number > (long)Maximum.Value)
            return $"'{Path}' must be {Describe_Range()} — {number} is too high";

        return null;
    }

    /// <summary>
    /// <see cref="JsonValue.TryGetValue{TValue}"/> only converts to the exact CLR type a non-element
    /// backed <see cref="JsonValue"/> was created with — a <c>JsonValue.Create(30)</c> is backed by
    /// <see langword="int"/>, and asking it for <see langword="long"/> fails even though 30 fits. A
    /// JsonElement-backed value (the shape every value read off disk actually is) has no such
    /// restriction: <c>TryGetValue&lt;long&gt;</c> alone would already accept
    /// <c>-1001234567890</c> there. Trying <see langword="long"/> first and falling back to
    /// <see langword="int"/> covers both shapes without accepting anything a whole number is not.
    /// </summary>
    static bool Try_GetLong(JsonValue jsonValue, out long number)
    {
        if (jsonValue.TryGetValue(out number))
            return true;

        if (jsonValue.TryGetValue(out int intValue))
        {
            number = intValue;
            return true;
        }

        number = 0;
        return false;
    }

    /// <summary>Names only the bound(s) actually set — a one-sided range never claims the other side.</summary>
    string Describe_Range()
    {
        if (Minimum != null && Maximum != null)
            return $"between {Minimum.Value} and {Maximum.Value}";

        if (Minimum != null)
            return $"at least {Minimum.Value}";

        return $"at most {Maximum!.Value}";
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
