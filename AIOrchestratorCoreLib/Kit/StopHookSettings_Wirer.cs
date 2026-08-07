using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;

namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// Registers the ledger Stop hook in ~/.claude/settings.json, the same way the status line is
/// wired. This is what turns the ledger from prose into a rule: a supervisor that owes a PLAN.md
/// update cannot end its turn.
///
/// Merges rather than overwrites — the user's own hooks are preserved, ours is added once, and a
/// backup is taken the first time. A settings file that is not a JSON object is left ALONE.
/// </summary>
public static class StopHookSettings_Wirer
{
    const string BACKUP_SUFFIX = ".aiorch-backup";

    public static bool Ensure_Wired(string settingsFilePath, string hookScriptPath)
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

            if (hooks["Stop"] is not JsonArray stopEntries)
            {
                stopEntries = [];
                hooks["Stop"] = stopEntries;
            }

            if (Contains_OurHook(stopEntries, hookScriptPath))
                return false;

            stopEntries.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command,
                }),
            });

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

    static bool Contains_OurHook(JsonArray stopEntries, string hookScriptPath)
    {
        var scriptName = Path.GetFileName(hookScriptPath);

        foreach (var entry in stopEntries)
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
