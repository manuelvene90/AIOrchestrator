using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;

namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// The orchestrator's enforcement hooks used to be registered here, in ~/.claude/settings.json, for
/// every session on the machine. They now travel with the aiorch plugin and are declared in the
/// frontmatter of the role that owns each one, so they register for that role and nothing else.
///
/// WHAT SURVIVES IS THE REMOVAL. A machine that upgrades still has the old entries pointing at
/// ~/.claude/hooks/*.sh, and leaving them would mean the ledger hook fires TWICE for a supervisor
/// (once from settings, once from the frontmatter) and once for every unrelated session on the
/// machine — which is what this stage set out to stop. Ensure_Wired is kept because it is what the
/// removal is defined against, and because a rollback needs it to still exist.
///
/// Both directions merge rather than overwrite — the user's own hooks are preserved, and a settings
/// file that is not a JSON object is left ALONE.
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

    /// <summary>
    /// Removes every entry whose command names <paramref name="hookScriptPath"/>'s file, from every
    /// event. Returns true when the file was changed. Matching is by FILENAME, exactly as
    /// Ensure_Wired's own idempotence check matches, so this removes precisely what that added — on
    /// a machine whose settings.json was written by an older build with a different absolute path.
    /// </summary>
    public static UnwireOutcomes Ensure_Unwired(string settingsFilePath, string hookScriptPath)
    {
        try
        {
            if (!File.Exists(settingsFilePath))
                return UnwireOutcomes.NothingToDo;

            // A settings file we cannot parse is the user's to fix — but it is NOT nothing to do.
            // Reported, because on that machine the legacy entries survive and the ledger hook fires
            // twice for a supervisor and once for every unrelated session, which is the exact failure
            // this stage exists to end. Silence here would be a predicate that could not be evaluated
            // pretending it was evaluated (decision 21's corollary).
            if (Read_SettingsRoot_OrNull(settingsFilePath) is not JsonObject root)
                return UnwireOutcomes.Unreadable;

            if (root["hooks"] is not JsonObject hooks)
                return UnwireOutcomes.NothingToDo;

            var scriptName = Path.GetFileName(hookScriptPath);
            var changed = false;

            foreach (var eventName in hooks.Select(pair => pair.Key).ToList())
            {
                if (hooks[eventName] is not JsonArray eventEntries)
                    continue;

                var removedHere = false;

                for (var index = eventEntries.Count - 1; index >= 0; index--)
                {
                    if (!Names_Script(eventEntries[index], scriptName))
                        continue;

                    eventEntries.RemoveAt(index);
                    removedHere = true;
                }

                // ONLY an event WE emptied. The previous version cleaned every empty array in the
                // file, so a user who keeps `"SessionStart": []` had that key deleted and the file
                // rewritten on a run that removed nothing of ours — and the caller then logged, seven
                // times over, that it had un-wired hooks the file never contained.
                if (removedHere && eventEntries.Count == 0)
                    hooks.Remove(eventName);

                changed |= removedHere;
            }

            if (!changed)
                return UnwireOutcomes.NothingToDo;

            Backup_Once(settingsFilePath);
            File.WriteAllText(settingsFilePath, root.ToJsonString(JsonWriting.INDENTED));
            return UnwireOutcomes.Unwired;
        }
        catch
        {
            // Same contract as Ensure_Wired: never let settings surgery break app startup. Reported
            // rather than swallowed, so the caller can say which predicate it could not evaluate.
            return UnwireOutcomes.Unreadable;
        }
    }

    static bool Names_Script(JsonNode? entry, string scriptName)
    {
        if (entry is not JsonObject entryObject || entryObject["hooks"] is not JsonArray innerHooks)
            return false;

        return innerHooks.Any(innerHook =>
            (innerHook as JsonObject)?["command"]?.GetValue<string>() is string command
            && command.Contains(scriptName, StringComparison.OrdinalIgnoreCase));
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
