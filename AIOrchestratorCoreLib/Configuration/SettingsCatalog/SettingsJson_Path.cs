using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// ONE WALK OVER THE RAW TREE for every catalogue key, dotted-path style: flat like
/// <c>telegramInbound</c>, nested like <c>phone.status.periodic</c>, three deep like
/// <c>runners.supervisor.runner</c> are all the same operation on the same
/// <see cref="JsonObject"/>. This is deliberately independent of the setting-definition vocabulary —
/// the resolver's four layers (shipped default, preset, config file, session) each hand this class a
/// tree and a path; nothing here knows what a "setting" is.
///
/// <para>
/// A SEGMENT THAT IS NOT AN OBJECT STOPS THE WALK RATHER THAN THROWING, the same tolerance
/// <see cref="OrchestratorConfig_Loader"/>'s own readers were given on 2026-09-10: a hand-edited
/// <c>{"phone": 3}</c> costs that ONE setting its default, never the app's startup.
/// </para>
///
/// <para>
/// <see cref="Write"/> CLONES THE VALUE BEFORE ASSIGNING IT (2026-09-12): a <see cref="JsonNode"/>
/// already attached to another tree throws <see cref="InvalidOperationException"/> when re-parented,
/// and the resolver hands values between trees — a shipped default living in one static tree, a
/// preset value living in another, a session override living in a third. Without the clone, reading
/// the same default twice into two different config trees would throw on the second call.
/// </para>
/// </summary>
public static class SettingsJson_Path
{
    /// <summary>
    /// The value at <paramref name="path"/>, or null when <paramref name="root"/> is null, any
    /// segment is absent, or any non-final segment is not a <see cref="JsonObject"/>.
    /// </summary>
    public static JsonNode? Read_OrNull(JsonObject? root, string path)
    {
        JsonNode? current = root;

        foreach (var segment in path.Split('.'))
        {
            if (current is not JsonObject currentObject)
                return null;

            current = currentObject[segment];
        }

        return current;
    }

    /// <summary>
    /// Writes <paramref name="value"/> at <paramref name="path"/> in <paramref name="root"/>,
    /// creating a new <see cref="JsonObject"/> for every missing intermediate segment. A segment that
    /// already exists but is not a <see cref="JsonObject"/> is REPLACED with one — this is a write,
    /// unlike <see cref="Read_OrNull"/>, so there is a caller asking for this exact path to hold this
    /// exact value, not a reader tolerating whatever it finds.
    /// </summary>
    public static void Write(JsonObject root, string path, JsonNode? value)
    {
        var segments = path.Split('.');
        var current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                child = new JsonObject();
                current[segments[i]] = child;
            }

            current = child;
        }

        current[segments[^1]] = value?.DeepClone();
    }

    /// <summary>
    /// Deletes the key at <paramref name="path"/>. Returns whether there was one to delete — never
    /// writes anything in its place. RESET DELETES THE KEY, IT DOES NOT WRITE THE DEFAULT (spec
    /// §6.2, the same rule the loader already keeps for <c>reviewerModel</c>): a materialised default
    /// is a default that can never move again, frozen on the first button press.
    /// </summary>
    public static bool Remove(JsonObject root, string path)
    {
        var segments = path.Split('.');
        JsonNode? current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current is not JsonObject currentObject)
                return false;

            current = currentObject[segments[i]];
        }

        if (current is not JsonObject parent)
            return false;

        return parent.Remove(segments[^1]);
    }
}
