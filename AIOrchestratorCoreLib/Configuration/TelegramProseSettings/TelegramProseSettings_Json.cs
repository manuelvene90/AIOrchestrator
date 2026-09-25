using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration.TelegramProseSettings;

/// <summary>
/// The two prose-shaping keys, read under either spelling:
///
/// <code>
/// "phone":    { "foldLongEntriesAbove": 900, "attachEntriesAbove": 3 }   // the catalogue's path
/// "telegram": { "foldLongEntriesAbove": 900, "attachEntriesAbove": 3 }   // the old one, still read
/// </code>
///
/// <para>
/// READ AND NEVER WRITTEN, for the reason <see cref="DefaultsSettings.DefaultsSettings_Json"/> and the
/// guardrail keys already give: no window has a field for either setting, so the only thing a save
/// could do is materialise THIS BUILD's defaults into the owner's file as if they had chosen them,
/// freezing numbers that are meant to move when the app is updated.
/// </para>
/// <para>
/// IT RESOLVES RATHER THAN PARSES (2026-09-14, plan 03 task 1). Until then this class read
/// <c>telegram.*</c> directly, so the catalogue's re-homed <c>phone.*</c> spelling was registered and
/// read by nothing. Both keys now come from <see cref="Settings_Resolver"/>, whose definitions carry
/// <c>telegram.*</c> as the legacy path: the new spelling wins inside a layer, the old one is still
/// honoured there, and the preset rung applies to these two keys like any other.
/// </para>
/// <para>
/// TOLERANT, because a config the app refuses to load is a bridge that does not start. An absent
/// block, an absent key, a key holding a string, an object, an array or null all read as the layer
/// below — ultimately the shipped default — for that ONE setting. 0 is the owner's OFF SWITCH for
/// either key and the catalogue's floor is 0 for exactly that reason. A NEGATIVE number, which this
/// class used to keep as a second spelling of "off", is now refused by the definition and costs the
/// key its default instead: the resolver has one rule for an out-of-range value, and a private
/// exception here would be the second idea of "valid" the catalogue exists to remove.
/// </para>
/// </summary>
public static class TelegramProseSettings_Json
{
    /// <summary>The OLD spelling's parts — the catalogue builds each row's legacy path from them.</summary>
    public const string TELEGRAM_KEY = "telegram";
    public const string FOLD_LONG_ENTRIES_ABOVE_KEY = "foldLongEntriesAbove";
    public const string ATTACH_ENTRIES_ABOVE_KEY = "attachEntriesAbove";

    /// <summary>The catalogue's paths, the spelling the resolver walks first.</summary>
    const string FOLD_LONG_ENTRIES_ABOVE_PATH = "phone." + FOLD_LONG_ENTRIES_ABOVE_KEY;
    const string ATTACH_ENTRIES_ABOVE_PATH = "phone." + ATTACH_ENTRIES_ABOVE_KEY;

    public static ITelegramProseSettings Parse(JsonObject? configRoot, JsonObject? presetTree)
    {
        return TelegramProseSettings_Factory.Create(
            Read_Int(FOLD_LONG_ENTRIES_ABOVE_PATH, configRoot, presetTree),
            Read_Int(ATTACH_ENTRIES_ABOVE_PATH, configRoot, presetTree));
    }

    /// <summary>
    /// Narrowed from the resolver's <see cref="long"/> without a check: both rows are non-nullable with
    /// <see cref="int"/> bounds, so whatever layer answers is a whole number the definition accepted.
    /// </summary>
    static int Read_Int(string path, JsonObject? configRoot, JsonObject? presetTree)
    {
        var definition = Catalog.Find_OrNull(path)
            ?? throw new Exception($"No catalogue entry for {path} — {nameof(TelegramProseSettings_Json)} names a row the settings catalogue does not register");

        return (int)Settings_Resolver.Resolve_Long(definition, presetTree, configRoot, session: null)!.Value;
    }
}
