using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;

namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// Registers the orchestrator's enforcement hooks in ~/.claude/settings.json, the same way the
/// status line is wired. This is what turns a rule from prose into a rule: a supervisor that owes a
/// PLAN.md update cannot end its turn (Stop), and a reviewer cannot mutate the repo through Bash
/// (PreToolUse).
///
/// Merges rather than overwrites — the user's own hooks are preserved, ours is added once per
/// script, and a backup is taken the first time. A settings file that is not a JSON object is left
/// ALONE.
/// </summary>
public static class AgentHookSettings_Wirer
{
    public const string STOP_EVENT = "Stop";
    public const string PRE_TOOL_USE_EVENT = "PreToolUse";

    const string BACKUP_SUFFIX = ".aiorch-backup";

    /// <summary>
    /// matcher is the tool-name pattern for PreToolUse (e.g. "Bash"); null for events that take
    /// no matcher, such as Stop.
    /// </summary>
    public static bool Ensure_Wired(string settingsFilePath, string hookScriptPath, string hookEvent, string? matcher)
    {
        try
        {
            var root = Read_SettingsRoot_OrNull(settingsFilePath);

            if (root == null)
                return false;

            var command = $"bash \"{hookScriptPath.Replace('\\', '/')}\"";

            if (root["hooks"] is not JsonObject hooks)
            {
                hooks = [];
                root["hooks"] = hooks;
            }

            if (hooks[hookEvent] is not JsonArray eventEntries)
            {
                eventEntries = [];
                hooks[hookEvent] = eventEntries;
            }

            if (Contains_OurHook(eventEntries, hookScriptPath))
                return false;

            var entry = new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command,
                }),
            };

            if (matcher != null)
                entry["matcher"] = matcher;

            eventEntries.Add(entry);

            Backup_Once(settingsFilePath);
            File.WriteAllText(settingsFilePath, root.ToJsonString(JsonWriting.INDENTED));
            return true;
        }
        catch
        {
            // Never let settings wiring break app startup — the app works, enforcement just is not on.
            return false;
        }
    }

    static bool Contains_OurHook(JsonArray eventEntries, string hookScriptPath)
    {
        var scriptName = Path.GetFileName(hookScriptPath);

        foreach (var entry in eventEntries)
        {
            if (entry is not JsonObject entryObject || entryObject["hooks"] is not JsonArray innerHooks)
                continue;

            foreach (var innerHook in innerHooks)
            {
                var command = (innerHook as JsonObject)?["command"]?.GetValue<string>();

                if (command != null && command.Contains(scriptName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    static JsonObject? Read_SettingsRoot_OrNull(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath))
            return [];

        var text = File.ReadAllText(settingsFilePath);

        if (string.IsNullOrWhiteSpace(text))
            return [];

        // A settings file we cannot parse is the user's to fix — never rewrite it blindly.
        return JsonNode.Parse(text) as JsonObject;
    }

    static void Backup_Once(string settingsFilePath)
    {
        var backupFile = $"{settingsFilePath}{BACKUP_SUFFIX}";

        if (File.Exists(settingsFilePath) && !File.Exists(backupFile))
            File.Copy(settingsFilePath, backupFile);
    }
}
