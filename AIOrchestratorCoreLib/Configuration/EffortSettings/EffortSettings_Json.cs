using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Running;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration.EffortSettings;

/// <summary>
/// The <c>effort</c> block, read:
///
/// <code>
/// "effort": { "supervisor": "xhigh", "implementer": null }
/// </code>
///
/// <para>
/// READ AND NEVER WRITTEN — there is deliberately no <c>Write</c> on this class, the same contract
/// the <c>defaults</c> and guardrail blocks keep. No window has a field for these keys, so the only
/// thing a save could do is materialise THIS BUILD's answer into the owner's file as if they had
/// chosen it, freezing a default that is meant to move. That is not a hypothetical here: the owner's
/// own config.json was found on 2026-09-12 pinned to a stale MODEL for exactly this reason, because
/// <see cref="OrchestratorConfig_Loader.Save"/> writes the model keys back. Effort must not repeat
/// that shape — and it is safe either way, because Save merges, so a hand-edited block survives
/// untouched.
/// </para>
/// <para>
/// IT RESOLVES RATHER THAN PARSES: every role's answer comes from
/// <see cref="Settings_Resolver.Resolve_String_OrNull"/> over this file and the preset beneath it, so
/// the four-layer precedence has one implementation and this block has no private idea of what
/// "absent" means. <c>session: null</c> on purpose — the per-orchestration <c>/effort</c> override is
/// the LAUNCHER's layer, applied where the session is in reach (CLAUDE.md decision 24).
/// </para>
/// <para>
/// AN EXPLICIT JSON NULL IS AN ANSWER, unlike everywhere else in this loader. The catalogue's effort
/// rows are <c>nullable</c>, so <c>ISettingDefinition.Validate_OrNull</c> accepts a null value and the
/// resolver stops falling through at that layer — which is the only way an owner on <c>classic</c>
/// can say "no flag for the supervisor" rather than merely failing to mention it. A key that is
/// ABSENT still falls to the preset, and the two must not be collapsed.
/// </para>
/// <para>
/// TOLERANT, because a config the app refuses to load is a bridge that does not start. A misspelled
/// level ("enormous"), a key holding a number, an object or an array: the definition's own validator
/// refuses each of them and the resolver falls to the layer below, so a typo costs that one key its
/// default and never the load.
/// </para>
/// </summary>
public static class EffortSettings_Json
{
    /// <summary>
    /// The block's own key in config.json, for the callers that need to NAME it — a renderer listing
    /// where a value came from, a report saying which block was hand-edited. DERIVED from the
    /// catalogue's path prefix rather than retyped: <c>effort.supervisor</c> and this key are the same
    /// word, and two copies of it are how one gets read under a spelling the other never writes
    /// (CLAUDE.md decision 12). Nothing inside this class uses it — every lookup here goes through the
    /// catalogue PATH, which is the spelling the resolver walks.
    /// </summary>
    public static readonly string EFFORT_KEY = Catalog.EFFORT_PATH_PREFIX.TrimEnd('.');

    public static IEffortSettings Parse(JsonObject? configRoot, JsonObject? presetTree)
    {
        Dictionary<SessionRoles, string?> levels = [];

        foreach (var role in SessionRole_Names.ALL)
        {
            var definition = Catalog.Find_OrNull(Catalog.Get_EffortPath(role))
                ?? throw new Exception($"No catalogue entry for {Catalog.Get_EffortPath(role)} — a role without a registered effort default cannot spawn");

            levels[role] = Settings_Resolver.Resolve_String_OrNull(definition, presetTree, configRoot, session: null);
        }

        return EffortSettings_Factory.Create(levels);
    }
}
