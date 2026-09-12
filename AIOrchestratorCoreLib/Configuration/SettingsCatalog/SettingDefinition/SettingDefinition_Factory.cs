using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;

/// <summary>
/// The only way to build an <see cref="ISettingDefinition"/>. Each <c>Create_*</c> method owns one
/// <see cref="SettingKinds"/> and sets its paired <see cref="SettingRenderers"/> in the same call —
/// a definition is never built with a kind and renderer that disagree — and refuses a blank path,
/// label or description at construction, naming the path in the exception, because those three are
/// read by every renderer this catalogue will ever grow.
/// </summary>
public static class SettingDefinition_Factory
{
    public static ISettingDefinition Create_Bool(
        string path,
        bool shippedDefault,
        SettingScopes scope,
        SettingCategories category,
        string label,
        string description,
        RestartKinds restart,
        string? legacyPath = null,
        string validator = SettingValidators.NONE)
    {
        Validate_Common(path, label, description);

        return new SettingDefinitionModel(
            path: path,
            legacyPath_OrNull: legacyPath,
            kind: SettingKinds.Bool,
            default_OrNull: JsonValue.Create(shippedDefault),
            enumValues: [],
            minimum: null,
            maximum: null,
            scope: scope,
            category: category,
            label: label,
            description: description,
            restart: restart,
            renderer: SettingRenderers.Toggle,
            compositeParser_OrNull: null,
            validatorName: validator,
            nullable: false);
    }

    public static ISettingDefinition Create_Enum(
        string path,
        IReadOnlyList<string> values,
        string? shippedDefault,
        SettingScopes scope,
        SettingCategories category,
        string label,
        string description,
        RestartKinds restart,
        bool nullable = false,
        string? legacyPath = null,
        string validator = SettingValidators.NONE)
    {
        Validate_Common(path, label, description);

        return new SettingDefinitionModel(
            path: path,
            legacyPath_OrNull: legacyPath,
            kind: SettingKinds.Enum,
            default_OrNull: shippedDefault == null ? null : JsonValue.Create(shippedDefault),
            enumValues: values,
            minimum: null,
            maximum: null,
            scope: scope,
            category: category,
            label: label,
            description: description,
            restart: restart,
            renderer: SettingRenderers.Choice,
            compositeParser_OrNull: null,
            validatorName: validator,
            nullable: nullable);
    }

    public static ISettingDefinition Create_Int(
        string path,
        int shippedDefault,
        int? minimum,
        int? maximum,
        SettingScopes scope,
        SettingCategories category,
        string label,
        string description,
        RestartKinds restart,
        string? legacyPath = null,
        string validator = SettingValidators.NONE)
    {
        Validate_Common(path, label, description);

        return new SettingDefinitionModel(
            path: path,
            legacyPath_OrNull: legacyPath,
            kind: SettingKinds.Int,
            default_OrNull: JsonValue.Create(shippedDefault),
            enumValues: [],
            minimum: minimum,
            maximum: maximum,
            scope: scope,
            category: category,
            label: label,
            description: description,
            restart: restart,
            renderer: SettingRenderers.Number,
            compositeParser_OrNull: null,
            validatorName: validator,
            nullable: false);
    }

    public static ISettingDefinition Create_String(
        string path,
        string shippedDefault,
        SettingScopes scope,
        SettingCategories category,
        string label,
        string description,
        RestartKinds restart,
        string? legacyPath = null,
        string validator = SettingValidators.NONE)
    {
        Validate_Common(path, label, description);

        return new SettingDefinitionModel(
            path: path,
            legacyPath_OrNull: legacyPath,
            kind: SettingKinds.String,
            default_OrNull: JsonValue.Create(shippedDefault),
            enumValues: [],
            minimum: null,
            maximum: null,
            scope: scope,
            category: category,
            label: label,
            description: description,
            restart: restart,
            renderer: SettingRenderers.Text,
            compositeParser_OrNull: null,
            validatorName: validator,
            nullable: false);
    }

    public static ISettingDefinition Create_StringList(
        string path,
        IReadOnlyList<string> shippedDefault,
        SettingScopes scope,
        SettingCategories category,
        string label,
        string description,
        RestartKinds restart,
        string? legacyPath = null,
        string validator = SettingValidators.NONE)
    {
        Validate_Common(path, label, description);

        var defaultArray = new JsonArray();
        foreach (var element in shippedDefault)
            defaultArray.Add(JsonValue.Create(element));

        return new SettingDefinitionModel(
            path: path,
            legacyPath_OrNull: legacyPath,
            kind: SettingKinds.StringList,
            default_OrNull: defaultArray,
            enumValues: [],
            minimum: null,
            maximum: null,
            scope: scope,
            category: category,
            label: label,
            description: description,
            restart: restart,
            renderer: SettingRenderers.OrderedList,
            compositeParser_OrNull: null,
            validatorName: validator,
            nullable: false);
    }

    public static ISettingDefinition Create_Composite(
        string path,
        string parserTypeName,
        SettingScopes scope,
        SettingCategories category,
        string label,
        string description,
        RestartKinds restart,
        string? legacyPath = null,
        string validator = SettingValidators.NONE)
    {
        Validate_Common(path, label, description);

        return new SettingDefinitionModel(
            path: path,
            legacyPath_OrNull: legacyPath,
            kind: SettingKinds.Composite,
            default_OrNull: null,
            enumValues: [],
            minimum: null,
            maximum: null,
            scope: scope,
            category: category,
            label: label,
            description: description,
            restart: restart,
            renderer: SettingRenderers.ReadOnly,
            compositeParser_OrNull: parserTypeName,
            validatorName: validator,
            nullable: true);
    }

    static void Validate_Common(string path, string label, string description)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Setting path must be non-empty");

        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException($"Setting '{path}' must have a non-blank label");

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException($"Setting '{path}' must have a non-blank description");
    }
}
