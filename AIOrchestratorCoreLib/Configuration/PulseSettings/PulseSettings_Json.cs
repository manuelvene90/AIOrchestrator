using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration.PulseSettings;

/// <summary>
/// The <c>pulse</c> rows and <c>general.buttons</c>, read:
///
/// <code>
/// "pulse": { "fields": ["supervisor", "merged", "updated"], "stepMinutes": 5, "holdToggle": false },
/// "general": { "buttons": [] }
/// </code>
///
/// <para>
/// READ AND NEVER WRITTEN, for the reason <c>PhoneSettings_Json</c> argues: with no window field for
/// any of these keys, a save could only materialise this build's resolution into the owner's file as if
/// they had chosen it — and a written-back field list or button bar would outrank the preset from then
/// on, silently undoing the next change either preset ships. No <c>Write</c> exists, so it cannot
/// happen by accident.
/// </para>
/// <para>
/// IT RESOLVES RATHER THAN PARSES, and a list is VALIDATED WHOLE: <c>pulse.fields</c> names only
/// <c>PulseField_Names</c> words with no repeat, the two button rows name only <c>BotCommandMenu</c>
/// verbs with no repeated verb (<c>SettingValidators</c>). One misspelled element refuses the list at
/// that layer and the resolver falls to the layer below — the owner gets the preset's whole bar rather
/// than a bar with a hole where the typo was, which a half-honoured list would look like.
/// </para>
/// </summary>
public static class PulseSettings_Json
{
    const string FIELDS_PATH = "pulse.fields";
    const string STEP_MINUTES_PATH = "pulse.stepMinutes";
    const string BUTTONS_PATH = "pulse.buttons";
    const string GENERAL_BUTTONS_PATH = "general.buttons";
    const string HOLD_TOGGLE_PATH = "pulse.holdToggle";

    /// <summary>Both trees null is the catalogue's own shipped defaults — see <c>PhoneSettings_Json.Parse</c>.</summary>
    public static IPulseSettings Parse(JsonObject? configRoot, JsonObject? presetTree)
    {
        return PulseSettings_Factory.Create(
            Read_StringList(FIELDS_PATH, configRoot, presetTree),
            (int)Settings_Resolver.Resolve_Long(Definition(STEP_MINUTES_PATH), presetTree, configRoot, session: null)!.Value,
            Read_StringList(BUTTONS_PATH, configRoot, presetTree),
            Read_StringList(GENERAL_BUTTONS_PATH, configRoot, presetTree),
            Settings_Resolver.Resolve_Bool(Definition(HOLD_TOGGLE_PATH), presetTree, configRoot, session: null));
    }

    /// <summary>
    /// THROUGH <see cref="Settings_Resolver.Resolve"/> BECAUSE THERE IS NO LIST ACCESSOR, and the cast
    /// is safe for a reason worth naming: every StringList row is non-nullable, and whatever layer
    /// answers has passed the definition's own check that it is an array of strings — the shipped
    /// default included (<c>SettingsCatalogTests</c>). The elements are COPIED out rather than the node
    /// kept: a preset tree is shared by every load in the process, so nothing downstream may hold it.
    /// </summary>
    static IReadOnlyList<string> Read_StringList(string path, JsonObject? configRoot, JsonObject? presetTree)
    {
        var (value, _) = Settings_Resolver.Resolve(Definition(path), presetTree, configRoot, session: null);

        return value!.AsArray().Select(element => element!.GetValue<string>()).ToArray();
    }

    /// <summary>A path this class names and the catalogue does not is a broken build, and it says which path.</summary>
    static ISettingDefinition Definition(string path)
    {
        return Catalog.Find_OrNull(path)
            ?? throw new Exception($"No catalogue entry for {path} — {nameof(PulseSettings_Json)} names a row the settings catalogue does not register");
    }
}
