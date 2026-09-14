using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;

/// <summary>
/// The only way to build an <see cref="ISettingDefinition"/>. Each <c>Create_*</c> method owns one
/// <see cref="SettingKinds"/> and sets its paired <see cref="SettingRenderers"/> in the same call —
/// a definition is never built with a kind and renderer that disagree — and refuses a blank path,
/// label or description at construction, naming the path in the exception, because those three are
/// read by every renderer this catalogue will ever grow.
///
/// <para>
/// Only <see cref="Create_String"/> and <see cref="Create_StringList"/> take a named
/// <c>validator</c>: every validator this catalogue needs is string-shaped (a listen address, a
/// list of known field or command words), so <see cref="Create_Bool"/>, <see cref="Create_Enum"/>,
/// <see cref="Create_Int"/> and <see cref="Create_Composite"/> do not carry the parameter at all —
/// a definition of one of those kinds simply has no named validator to point at, rather than one
/// that compiles and is silently never read.
/// </para>
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
        bool readOnly = false)
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
            renderer: readOnly ? SettingRenderers.ReadOnly : SettingRenderers.Toggle,
            compositeParser_OrNull: null,
            validatorName: SettingValidators.NONE,
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
        bool readOnly = false)
    {
        Validate_Common(path, label, description);

        if (shippedDefault == null && !nullable)
            throw new ArgumentException($"Setting '{path}' has no shipped default but is not nullable");

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
            renderer: readOnly ? SettingRenderers.ReadOnly : SettingRenderers.Choice,
            compositeParser_OrNull: null,
            validatorName: SettingValidators.NONE,
            nullable: nullable);
    }

    /// <summary>
    /// <paramref name="nullable"/> exists for the same reason <see cref="Create_Enum"/> has it: three
    /// catalogue keys — the two Telegram ids and the orchestration token budget — ship ABSENT rather
    /// than with a number, and "the owner has never set this" is not expressible as an int. A
    /// nullable Int's shipped default is <c>null</c>, and a null VALUE is then accepted rather than
    /// refused as empty; the range still governs every value that is present.
    /// </summary>
    public static ISettingDefinition Create_Int(
        string path,
        int? shippedDefault,
        int? minimum,
        int? maximum,
        SettingScopes scope,
        SettingCategories category,
        string label,
        string description,
        RestartKinds restart,
        bool nullable = false,
        string? legacyPath = null)
    {
        Validate_Common(path, label, description);

        if (shippedDefault == null && !nullable)
            throw new ArgumentException($"Setting '{path}' has no shipped default but is not nullable");

        return new SettingDefinitionModel(
            path: path,
            legacyPath_OrNull: legacyPath,
            kind: SettingKinds.Int,
            default_OrNull: shippedDefault == null ? null : JsonValue.Create(shippedDefault.Value),
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
            validatorName: SettingValidators.NONE,
            nullable: nullable);
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
        string? legacyPath = null)
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
            validatorName: SettingValidators.NONE,
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
