using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;

namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// Wires the orchestrator status line into ~/.claude/settings.json (statusLine → the installed
/// statusline.ps1). Careful read-modify-write: every other setting is preserved, and the previous
/// file is backed up beside it before the first change. Idempotent — no write when already wired.
/// </summary>
public static class StatusLineSettings_Wirer
{
    public const string BACKUP_SUFFIX = ".aiorch-backup";

    /// <summary>Returns true when the settings file was changed (false = already wired).</summary>
    public static bool Ensure_Wired(string settingsFilePath, string statuslineScriptPath)
    {
        var desiredCommand = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{statuslineScriptPath}\"";

        JsonObject root;

        if (File.Exists(settingsFilePath))
        {
            var text = File.ReadAllText(settingsFilePath);
            root = JsonNode.Parse(text) as JsonObject
                ?? throw new Exception($"'{settingsFilePath}' is not a JSON object — refusing to rewrite it");
        }
        else
        {
            root = [];
        }

        var currentCommand = root["statusLine"]?["command"]?.GetValue<string>();

        if (currentCommand == desiredCommand)
            return false;

        if (File.Exists(settingsFilePath))
            File.Copy(settingsFilePath, settingsFilePath + BACKUP_SUFFIX, overwrite: true);

        root["statusLine"] = new JsonObject
        {
            ["type"] = "command",
            ["command"] = desiredCommand,
        };

        var settingsFolder = Path.GetDirectoryName(settingsFilePath);
        if (settingsFolder != null)
            Directory.CreateDirectory(settingsFolder);

        File.WriteAllText(settingsFilePath, root.ToJsonString(JsonWriting.INDENTED));
        return true;
    }
}
