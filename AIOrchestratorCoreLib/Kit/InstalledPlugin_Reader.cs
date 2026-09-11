using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// Reads what a Claude home says about an installed plugin, from the two files that hold the answer:
///
///   plugins/installed_plugins.json   version + installPath + gitCommitSha, per scope
///   settings.json  -> enabledPlugins  whether it is switched on
///
/// FILES, NOT `claude plugin list --json`. Shelling out would make the host's startup depend on a
/// CLI being on PATH and on its output format, and would make this untestable without a real
/// installation; both files are stable on-disk state that a temp folder can reproduce exactly.
///
/// A file that is absent is an ANSWER (not installed / not enabled). A file that is present but
/// unreadable is NOT an answer, and is reported as such rather than being read as absence — that
/// distinction is the whole point: absence and illegibility must not collapse into the same verdict.
/// </summary>
public static class InstalledPlugin_Reader
{
    public const string INSTALLED_PLUGINS_FILE = "installed_plugins.json";
    public const string PLUGINS_FOLDER = "plugins";
    public const string SETTINGS_FILE = "settings.json";
    public const string ENABLED_PLUGINS_KEY = "enabledPlugins";

    /// <summary><paramref name="claudeHomeFolder"/> is the folder holding settings.json and plugins/.</summary>
    public static InstalledPluginReading Read(string claudeHomeFolder, string pluginId)
    {
        var installedFile = Path.Combine(claudeHomeFolder, PLUGINS_FOLDER, INSTALLED_PLUGINS_FILE);
        var settingsFile = Path.Combine(claudeHomeFolder, SETTINGS_FILE);

        string? version = null;
        string? installPath = null;
        string? commitSha = null;

        if (File.Exists(installedFile))
        {
            // THE WHOLE TRAVERSAL IS INSIDE THE TRY, not just the parse. GetValue<string>() on a
            // number, a bool or an object throws InvalidOperationException, and a record like
            // {"version": 2} is a shape a file on disk can genuinely hold — a hand-edit, a future
            // CLI, a half-written file. Outside the try that throw ESCAPES Read entirely, which is
            // worse than the absence/illegibility collapse this class exists to prevent: the caller
            // gets neither answer and the startup check dies with it.
            try
            {
                // Shape (measured on a real install): { "version": 2, "plugins": { "<id>": [ { scope,
                // installPath, version, ... } ] } } — an ARRAY per id, because one plugin can be
                // installed at more than one scope. First record wins; the refusal names the path, so
                // a human who has two can see which one answered.
                if (JsonNode.Parse(File.ReadAllText(installedFile)) is not JsonObject installedRoot)
                    return new InstalledPluginReading(null, null, false, $"{installedFile} is not a JSON object");

                var records = installedRoot["plugins"]?[pluginId] as JsonArray;
                var first = records?.FirstOrDefault() as JsonObject;

                version = Read_String_OrNull(first, "version");
                installPath = Read_String_OrNull(first, "installPath");

                // MEASURED 2026-09-07 on CLI 2.1.263: this field is present even for a plugin served
                // from a LOCAL DIRECTORY marketplace — it is the HEAD of the git repository the
                // marketplace sits in, at install time. `claude plugin list --json` does NOT expose
                // it, which is why this class reads the files rather than shelling out.
                commitSha = Read_String_OrNull(first, "gitCommitSha");
            }
            catch (Exception ex)
            {
                return new InstalledPluginReading(null, null, false, $"{installedFile} could not be read: {ex.Message}");
            }
        }

        var enabled = false;

        if (File.Exists(settingsFile))
        {
            try
            {
                var settings = JsonNode.Parse(File.ReadAllText(settingsFile)) as JsonObject;
                enabled = settings?[ENABLED_PLUGINS_KEY]?[pluginId]?.GetValue<bool>() ?? false;
            }
            catch (Exception ex)
            {
                return new InstalledPluginReading(version, installPath, false, $"{settingsFile} could not be read: {ex.Message}", commitSha);
            }
        }

        return new InstalledPluginReading(version, installPath, enabled, null, commitSha);
    }

    /// <summary>
    /// A field that is present but is not a string is NOT a value — it is an unreadable record, and
    /// it says so by throwing out of the guarded block above rather than by quietly reading as
    /// absent. Written as a helper so both fields answer the question the same way.
    /// </summary>
    static string? Read_String_OrNull(JsonObject? record, string field)
    {
        var node = record?[field];

        return node == null ? null : node.GetValue<string>();
    }
}
