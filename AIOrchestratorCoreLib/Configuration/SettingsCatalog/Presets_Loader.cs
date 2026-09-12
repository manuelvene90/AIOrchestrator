using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// THE TWO SHIPPED PRESETS — Manu's way and Nathan's way — as embedded data, on the same shape
/// <see cref="Channels.ChannelGrammar"/> already proved for the channel grammar: the truth is a JSON
/// file under <c>kit/</c>, embedded into this assembly as a resource so a missing preset is a broken
/// BUILD rather than an app that starts with nobody's settings, and the SAME file also ships beside
/// the kit for an installer to read on a fresh machine.
///
/// <para>
/// A PRESET IS A LIST OF CATALOGUE PATHS, so its keys are written FLAT
/// (<c>"phone.push"</c>), never nested — the flat form is what makes "does this preset name only
/// catalogue paths" a one-line test (<c>PresetsLoaderTests</c>). <see cref="SettingsJson_Path"/>
/// reads a dotted path regardless of whether the JSON tree it walks is flat or nested, which is why
/// the flat form is legal in <c>config.json</c> too — this class does not depend on that, but the
/// two readers agree because they are the same reader.
/// </para>
/// <para>
/// AN UNKNOWN PRESET NAME THROWS RATHER THAN DEFAULTING (spec §6.2) — the fork's own rule for
/// <c>planBackend.kind</c>, and for its reason: a typo that silently fell back to classic would give
/// the owner who typed "quite" Manu's phone with nothing anywhere saying why.
/// </para>
/// </summary>
public static class Presets_Loader
{
    /// <summary>The config.json key that names which preset an orchestration or the app resolves to.</summary>
    public const string PRESET_KEY = "preset";

    public const string CLASSIC = "classic";

    public const string QUIET = "quiet";

    /// <summary>
    /// The kit path, so a caller that wants the folder on disk (the installer, this class's own
    /// tests) names it once. Not used to LOAD a preset — that comes from the embedded resource,
    /// which cannot be missing at runtime.
    /// </summary>
    public const string KIT_RELATIVE_FOLDER = "kit/presets";

    static readonly IReadOnlyList<string> KNOWN_NAMES = [CLASSIC, QUIET];

    static readonly Dictionary<string, string> RAW_JSON_BY_NAME = new()
    {
        [CLASSIC] = Read_Resource(CLASSIC),
        [QUIET] = Read_Resource(QUIET),
    };

    static readonly Dictionary<string, JsonObject> PARSED_BY_NAME = RAW_JSON_BY_NAME.ToDictionary(
        pair => pair.Key,
        pair => Parse(pair.Value, pair.Key));

    /// <summary>The preset's tree, keyed by its flat catalogue paths. Throws for any other word.</summary>
    public static JsonObject Load_Embedded(string name)
    {
        if (!PARSED_BY_NAME.TryGetValue(name, out var preset))
            throw Unknown_Name(name);

        return preset;
    }

    /// <summary>The raw embedded text, for the test that compares it byte-for-byte with the kit copy.</summary>
    public static string Embedded_Json(string name)
    {
        if (!RAW_JSON_BY_NAME.TryGetValue(name, out var json))
            throw Unknown_Name(name);

        return json;
    }

    /// <summary>
    /// Resolves the preset a config tree names. Reads <c>preset</c> through a try/catch —
    /// <c>GetValue&lt;string?&gt;()</c> throws for a number, so a mistyped <c>preset</c> reads as
    /// ABSENT rather than crashing the whole resolve. Absent or blank means <see cref="CLASSIC"/>
    /// (spec §11.3: that is what master did before the merge, and the model ladder already treats a
    /// cleared field as the owner saying nothing). A value containing a directory separator or
    /// ending <c>.json</c> is a FILE PATH, read from disk rather than looked up among the embedded
    /// presets — a file that does not exist or does not parse THROWS naming the path, because the
    /// owner typed a path and silently ignoring it would be the "externa1" failure again.
    /// </summary>
    public static (JsonObject Tree, string Name) Resolve_ForConfig(JsonObject? configRoot)
    {
        var word = Read_PresetWord_OrNull(configRoot);

        if (string.IsNullOrWhiteSpace(word))
            return (Load_Embedded(CLASSIC), CLASSIC);

        if (Looks_LikeFilePath(word))
            return (Load_FromDisk(word), word);

        return (Load_Embedded(word), word);
    }

    static string? Read_PresetWord_OrNull(JsonObject? configRoot)
    {
        try
        {
            return SettingsJson_Path.Read_OrNull(configRoot, PRESET_KEY)?.GetValue<string?>();
        }
        catch (Exception)
        {
            // A non-string 'preset' (a number, a bool) reads as absent, which resolves to classic —
            // never a crash over a field the owner mistyped.
            return null;
        }
    }

    static bool Looks_LikeFilePath(string word)
    {
        return word.Contains('/') || word.Contains('\\') || word.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    static JsonObject Load_FromDisk(string path)
    {
        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"The preset file '{path}' named by '{PRESET_KEY}' could not be read: {ex.Message}", ex);
        }

        if (JsonNode.Parse(text) is not JsonObject parsed)
            throw new ArgumentException($"The preset file '{path}' named by '{PRESET_KEY}' is not a JSON object.");

        return parsed;
    }

    static JsonObject Parse(string json, string name)
    {
        return JsonNode.Parse(json) as JsonObject
            ?? throw new Exception($"The embedded preset '{name}' ({KIT_RELATIVE_FOLDER}/{name}.json) is not a JSON object.");
    }

    static ArgumentException Unknown_Name(string name)
    {
        return new ArgumentException(
            $"'{name}' is not a known preset — the known presets are: {string.Join(", ", KNOWN_NAMES)}.");
    }

    static string Read_Resource(string name)
    {
        var resourceName = $"AIOrchestratorCoreLib.kit.presets.{name}.json";

        using var stream = typeof(Presets_Loader).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new Exception(
                $"The preset resource '{resourceName}' is not embedded in "
                + $"{typeof(Presets_Loader).Assembly.GetName().Name}. Every preset comes from it, so there is "
                + $"nothing to fall back to: check the EmbeddedResource entry in AIOrchestratorCoreLib.csproj.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
