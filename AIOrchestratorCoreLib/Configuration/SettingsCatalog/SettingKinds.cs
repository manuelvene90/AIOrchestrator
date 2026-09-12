namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// The shape a setting's VALUE takes — what a resolved value can be cast to and what a renderer
/// draws as its control: <c>Bool</c> a toggle, <c>Enum</c> one of a fixed word list, <c>Int</c> a
/// number within a range, <c>String</c> free text (possibly still restricted by a named validator),
/// <c>StringList</c> an ordered list of words, <c>Composite</c> a value this catalogue only
/// describes — it is parsed elsewhere and shown read-only here.
/// </summary>
public enum SettingKinds
{
    Bool,
    Enum,
    Int,
    String,
    StringList,
    Composite,
}
