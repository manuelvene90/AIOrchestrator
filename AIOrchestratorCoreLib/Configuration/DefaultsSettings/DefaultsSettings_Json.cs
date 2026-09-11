using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Sessions;

namespace AIOrchestratorCoreLib.Configuration.DefaultsSettings;

/// <summary>
/// The <c>defaults</c> block, read:
///
/// <code>
/// "defaults": { "orchestrationMode": "basic" }
/// </code>
///
/// <para>
/// READ AND NEVER WRITTEN, for the reason the loader already gives about the guardrail keys: no
/// window has a field for this, so the only thing a save could do is materialise THIS BUILD's default
/// into the owner's file as if they had chosen it — freezing a default that is meant to move when the
/// app is updated. It is hand-edited, and the app owns nothing about it but its meaning.
/// </para>
/// <para>
/// TOLERANT, because a config the app refuses to load is a bridge that does not start. An absent
/// block, an absent key, a key holding a number or an object, and a word neither
/// <see cref="OrchestrationModes"/> knows all read as the shipped default.
/// </para>
/// <para>
/// AND NOT SILENT ABOUT IT — but not from here. This layer has no log to write to (the provider that
/// calls it has none either), so rather than invent one the fallback is made visible where the owner
/// actually meets it: <c>BridgeEngineModel.Process_StartRequests</c> says, in the entry that confirms
/// every start, which shape it used and whether the request or the configured default chose it. A
/// mistyped word is then one orchestration away from being obvious, instead of a key that reads
/// right and never takes effect.
/// </para>
/// </summary>
public static class DefaultsSettings_Json
{
    public const string DEFAULTS_KEY = "defaults";
    public const string ORCHESTRATION_MODE_KEY = "orchestrationMode";

    public static IDefaultsSettings Parse(JsonObject? configRoot)
    {
        return DefaultsSettings_Factory.Create(OrchestrationModes.Is_Basic_OrNull(Read_Word_OrNull(configRoot)));
    }

    /// <summary>
    /// Null for every way the key can fail to be a word: no block, no key, or a key holding a number,
    /// an object or an array. <c>GetValue&lt;string&gt;</c> throws on the last of those, and a config
    /// file that throws while being read is the bridge not starting.
    /// </summary>
    static string? Read_Word_OrNull(JsonObject? configRoot)
    {
        if (configRoot?[DEFAULTS_KEY] is not JsonObject block)
            return null;

        if (block[ORCHESTRATION_MODE_KEY] is not JsonValue value)
            return null;

        return value.TryGetValue<string>(out var word) ? word : null;
    }
}
