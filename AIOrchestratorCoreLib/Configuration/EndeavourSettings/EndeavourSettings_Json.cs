using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration.EndeavourSettings;

/// <summary>
/// The <c>endeavour</c> block, read:
///
/// <code>
/// "endeavour": { "maxOpenSiblings": 3 }
/// </code>
///
/// <para>
/// READ AND NEVER WRITTEN — there is deliberately no <c>Write</c> here, for the reason
/// <c>EffortSettings_Json</c> gives: a block written back by <c>OrchestratorConfig_Loader.Save</c> becomes
/// a STATED value, freezing this build's default into the owner's file as if they had chosen it (the
/// owner's config.json was found pinned to a stale model that way on 2026-09-12). The owner states the
/// key deliberately through <c>Settings_Writer</c> — one row, on their own gesture — and that is the
/// only writer it has.
/// </para>
/// <para>
/// IT RESOLVES RATHER THAN PARSES: <see cref="Settings_Resolver.Resolve_Long"/> over this file and the
/// preset beneath it, so the precedence has one implementation. The row's own range (1–10) is applied
/// by the resolver PER LAYER (<c>ISettingDefinition.Validate_OrNull</c>), so a hand-typed 0, 11 or
/// <c>"three"</c> costs this one key its default and never the load — which matters because the loader
/// runs every tick with no try/catch above it. No clamp is restated here.
/// </para>
/// <para>
/// Neither shipped preset states the key (spec §9 O2): the catalogue's default IS the answer.
/// </para>
/// </summary>
public static class EndeavourSettings_Json
{
    public static IEndeavourSettings Parse(JsonObject? configRoot, JsonObject? presetTree)
    {
        var definition = Catalog.Find_OrNull(Catalog.ENDEAVOUR_MAX_OPEN_SIBLINGS_PATH)
            ?? throw new Exception($"No catalogue entry for {Catalog.ENDEAVOUR_MAX_OPEN_SIBLINGS_PATH} — the sibling cap has no registered default");

        var cap = Settings_Resolver.Resolve_Long(definition, presetTree, configRoot, session: null)
            ?? EndeavourSettings_Factory.DEFAULT_MAX_OPEN_SIBLINGS;

        return EndeavourSettings_Factory.Create((int)cap);
    }
}
