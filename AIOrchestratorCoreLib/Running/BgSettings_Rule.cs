using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// <c>bg</c> IS NEVER RUN WITH REMOTE CONTROL ON. Measured on 2026-09-05: a default
/// <c>claude --bg</c> opens a websocket to <c>wss://bridge.claudeusercontent.com</c> and writes its
/// <c>bridgeSessionId</c> plus the account and organisation uuids into <c>~/.claude/jobs/</c> — the
/// owner's orchestration appears on claude.ai. The same measurement found that
/// <c>--settings '{"disableRemoteControl":true}'</c> removes every <c>bridge*</c> field with no
/// privilege, no managed settings and no organisation policy, and that <c>respawnFlags</c> keeps it
/// across a respawn.
///
/// <para>
/// So the rule is not a default, it is a CONDITION OF THE TRANSPORT: a role asking for <c>bg</c>
/// without those settings is not configured, it is misconfigured, and the loader refuses it rather
/// than starting a session that quietly registers the owner elsewhere. Silence in config.json is not
/// the misconfiguration — that gets the canonical settings; an explicit contradiction is.
/// </para>
/// </summary>
public static class BgSettings_Rule
{
    public const string DISABLE_REMOTE_CONTROL_KEY = "disableRemoteControl";

    /// <summary>What is passed as <c>--settings</c> when a role names <c>bg</c> and says nothing else.</summary>
    public const string CANONICAL_SETTINGS = "{\"disableRemoteControl\":true}";

    /// <summary>
    /// The settings a <c>bg</c> role must run with, or a refusal reason. Null settings mean "the
    /// owner said nothing", which is the common case and gets <see cref="CANONICAL_SETTINGS"/>.
    /// </summary>
    public static (string? Settings, string? RefusalReason) Resolve(string? configuredSettings)
    {
        if (string.IsNullOrWhiteSpace(configuredSettings))
            return (CANONICAL_SETTINGS, null);

        JsonObject? settings;

        try
        {
            settings = JsonNode.Parse(configuredSettings) as JsonObject;
        }
        catch
        {
            settings = null;
        }

        if (settings == null)
            return (null, $"its 'settings' is not a JSON object ({Trim(configuredSettings)}), so it cannot be shown to disable Remote Control");

        var disabled = settings[DISABLE_REMOTE_CONTROL_KEY];

        if (disabled == null)
            return (null, $"its 'settings' does not set {DISABLE_REMOTE_CONTROL_KEY} ({Trim(configuredSettings)})");

        try
        {
            if (!disabled.GetValue<bool>())
                return (null, $"its 'settings' sets {DISABLE_REMOTE_CONTROL_KEY} to false — a bg session would register with the Anthropic bridge");
        }
        catch
        {
            return (null, $"its 'settings' sets {DISABLE_REMOTE_CONTROL_KEY} to a non-boolean ({Trim(configuredSettings)})");
        }

        return (configuredSettings, null);
    }

    static string Trim(string text)
    {
        var single = text.Replace("\n", " ").Trim();

        return single.Length <= 80 ? single : single[..79] + "\u2026";
    }
}
