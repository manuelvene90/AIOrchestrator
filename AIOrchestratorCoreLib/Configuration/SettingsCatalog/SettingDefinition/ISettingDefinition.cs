using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;

/// <summary>
/// One setting's whole DEFINITION, as DATA: everything a resolver, a renderer or a validator needs
/// to know about the path without asking anyone else. The catalogue (a later task) is a list of
/// these; nothing here is a resolved value — that is the resolver's job, reading a definition's
/// <see cref="Default_OrNull"/> as its lowest layer.
/// </summary>
public interface ISettingDefinition
{
    /// <summary>The key a resolved value is looked up and written under, e.g. "phone.appMessagesRing".</summary>
    string Path { get; }

    /// <summary>
    /// The flat config.json key this setting used to be, before the catalogue existed — e.g.
    /// "supervisorModel" for "models.supervisor" — or null when the setting is new. The loader's
    /// compatibility ladder reads an old key under its old name until the owner's config.json is
    /// rewritten under the new one.
    /// </summary>
    string? LegacyPath_OrNull { get; }

    SettingKinds Kind { get; }

    /// <summary>
    /// The value this setting carries when nobody — no preset, no config file, no session — has
    /// said otherwise. Null is legal only where null is itself a meaning (see the nullable Enum
    /// factory overload), never as "not yet decided."
    /// </summary>
    JsonNode? Default_OrNull { get; }

    /// <summary>The words an Enum setting accepts. Empty for every other kind.</summary>
    IReadOnlyList<string> EnumValues { get; }

    /// <summary>The lowest value an Int setting accepts, or null when unbounded below.</summary>
    int? Minimum { get; }

    /// <summary>The highest value an Int setting accepts, or null when unbounded above.</summary>
    int? Maximum { get; }

    SettingScopes Scope { get; }

    SettingCategories Category { get; }

    /// <summary>The short word or phrase a renderer shows beside the control.</summary>
    string Label { get; }

    /// <summary>The sentence a renderer shows under the label, explaining what the setting does.</summary>
    string Description { get; }

    RestartKinds Restart { get; }

    SettingRenderers Renderer { get; }

    /// <summary>
    /// The fully-qualified type name of the component that parses a Composite setting's value, or
    /// null for every other kind. Named rather than referenced so the catalogue stays serialisable.
    /// </summary>
    string? CompositeParser_OrNull { get; }

    /// <summary>
    /// The reason <paramref name="value"/> is not acceptable for this setting, or null when it is.
    /// Checks the value's shape against <see cref="Kind"/> first, then the range or enum list; a
    /// named validator this definition points at runs only for the <see cref="SettingKinds.String"/>
    /// and <see cref="SettingKinds.StringList"/> kinds — every validator the catalogue needs is
    /// string-shaped (a listen address, a list of known field or command words), so Bool, Enum, Int
    /// and Composite never consult one.
    /// </summary>
    string? Validate_OrNull(JsonNode? value);
}
