namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// WHICH LAYER a resolved value actually came from, in the resolver's precedence order from lowest
/// to highest: <c>ShippedDefault</c> — the definition's own <c>Default_OrNull</c>, nobody has chosen
/// anything; <c>Preset</c> — the machine's chosen preset (<c>classic</c> or <c>quiet</c>) named a
/// value for this path; <c>ConfigFile</c> — config.json (machine scope) or session.json
/// (orchestration scope) names it explicitly, overriding the preset; <c>Session</c> — set for this
/// running session only, through a request or a dial, and never persisted. A renderer shows this
/// alongside the value so the owner can tell "this is what I set" from "this is what shipped."
/// </summary>
public enum SettingOrigins
{
    ShippedDefault,
    Preset,
    ConfigFile,
    Session,
}
