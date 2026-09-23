using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Telegram;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration.PhoneSettings;

/// <summary>
/// The <c>phone</c> and <c>topic</c> rows, read:
///
/// <code>
/// "phone": { "push": "everything", "receipts": "reactions", "status": { "periodic": false } },
/// "topic": { "onClose": "delete" }
/// </code>
///
/// <para>
/// READ AND NEVER WRITTEN — there is no <c>Write</c> on this class, and <c>OrchestratorConfig_Loader.Save</c>
/// does not serialise the block. No window has a field for any of these keys, so the only thing a save
/// could ever put into config.json is the answer THIS BUILD resolved — and once written, that answer
/// sits in the owner's own file at the highest file layer, indistinguishable from a choice they made,
/// outranking the preset they picked and every default a later build ships. That is how the owner's
/// config.json came to pin a stale model on 2026-09-12. These rows are exactly the ones two presets
/// disagree about, so a written-back value would freeze a machine to whichever preset was in force the
/// day someone pressed a button. A hand-edited block is safe either way: Save merges.
/// </para>
/// <para>
/// IT RESOLVES RATHER THAN PARSES: each row comes from <see cref="Settings_Resolver"/> over this file
/// and the preset beneath it, so the four-layer precedence has one implementation and this block has
/// no private idea of what "absent" means. <c>session: null</c> — every row here is machine-scoped.
/// </para>
/// <para>
/// TOLERANT, because a config the app refuses to load is a bridge that does not start. A misspelled
/// word, a string where a number belongs, a number past its range: the row's own definition refuses it
/// and the resolver falls to the layer below, so a typo costs that one key its preset or shipped value
/// and never the load.
/// </para>
/// </summary>
public static class PhoneSettings_Json
{
    const string PUSH_PATH = "phone.push";
    const string PERIODIC_STATUS_PATH = "phone.status.periodic";
    const string PERIODIC_STATUS_INTERVAL_MINUTES_PATH = "phone.status.intervalMinutes";
    const string APP_MESSAGES_RING_PATH = "phone.appMessagesRing";
    const string REPLY_KEYBOARD_PATH = "phone.replyKeyboard";

    /// <summary>Public so a line that names this key — the hold toggle's D10 fallback warning — spells it from here, not from a second copy.</summary>
    public const string RECEIPTS_PATH = "phone.receipts";
    const string TOPIC_ON_CLOSE_PATH = "topic.onClose";
    const string TOPIC_MODE_GLYPHS_PATH = "topic.modeGlyphs";

    /// <summary>
    /// A null <paramref name="configRoot"/> and a null <paramref name="presetTree"/> together are the
    /// catalogue's own shipped defaults — which is how <c>OrchestratorConfig_Factory</c> fills the block
    /// for a config assembled in memory. That is NOT classic: the preset rung is the loader's, the only
    /// reader that has a <c>preset</c> key to consult.
    /// </summary>
    public static IPhoneSettings Parse(JsonObject? configRoot, JsonObject? presetTree)
    {
        return PhoneSettings_Factory.Create(
            PhonePush_Modes.Parse_OrFiltered(Read_Word_OrNull(PUSH_PATH, configRoot, presetTree)),
            Settings_Resolver.Resolve_Bool(Definition(PERIODIC_STATUS_PATH), presetTree, configRoot, session: null),
            Read_Int(PERIODIC_STATUS_INTERVAL_MINUTES_PATH, configRoot, presetTree),
            Settings_Resolver.Resolve_Bool(Definition(APP_MESSAGES_RING_PATH), presetTree, configRoot, session: null),
            ReplyKeyboard_Modes.Parse_OrOff(Read_Word_OrNull(REPLY_KEYBOARD_PATH, configRoot, presetTree)),
            Receipt_Styles.Parse_OrTicks(Read_Word_OrNull(RECEIPTS_PATH, configRoot, presetTree)),
            TopicClose_Actions.Parse_OrDefault(Read_Word_OrNull(TOPIC_ON_CLOSE_PATH, configRoot, presetTree)),
            ModeGlyph_Placements.Parse_OrPulseHeader(Read_Word_OrNull(TOPIC_MODE_GLYPHS_PATH, configRoot, presetTree)));
    }

    static string? Read_Word_OrNull(string path, JsonObject? configRoot, JsonObject? presetTree)
    {
        return Settings_Resolver.Resolve_String_OrNull(Definition(path), presetTree, configRoot, session: null);
    }

    /// <summary>
    /// Narrowed from the resolver's <see cref="long"/> without a check: the row is non-nullable with
    /// <see cref="int"/> bounds, and a value outside them never gets past its own definition.
    /// </summary>
    static int Read_Int(string path, JsonObject? configRoot, JsonObject? presetTree)
    {
        return (int)Settings_Resolver.Resolve_Long(Definition(path), presetTree, configRoot, session: null)!.Value;
    }

    /// <summary>A path this class names and the catalogue does not is a broken build, and it says which path.</summary>
    static ISettingDefinition Definition(string path)
    {
        return Catalog.Find_OrNull(path)
            ?? throw new Exception($"No catalogue entry for {path} — {nameof(PhoneSettings_Json)} names a row the settings catalogue does not register");
    }
}
