namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// The CONTROL a renderer draws for a setting, one per <see cref="SettingKinds"/> value (the
/// factory sets both together — see <c>SettingDefinition_Factory</c>): <c>Toggle</c> for Bool,
/// <c>Choice</c> for Enum, <c>Number</c> for Int, <c>Text</c> for String, <c>OrderedList</c> for
/// StringList, <c>ReadOnly</c> for Composite — a value this catalogue only describes, never edits.
/// </summary>
public enum SettingRenderers
{
    Toggle,
    Choice,
    Number,
    Text,
    OrderedList,
    ReadOnly,
}
