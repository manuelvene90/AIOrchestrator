using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;

namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// ONE SETTING'S VALUE, ACROSS FOUR LAYERS, WITHOUT TOUCHING DISK (spec §6.2). Highest first: the
/// running session (Orchestration-scoped keys only, and only when a session is handed in), then the
/// config-file tree, then the preset tree, then the definition's own shipped default. Every layer is
/// a PARAMETER — a plain <see cref="JsonObject"/> or null — so precedence is provable without a
/// loader or a file on disk, the same reason <c>HostOptions_Factory.Create_FromArguments</c> takes
/// its environment reader as a function.
///
/// <para>
/// ABSENT IS ABSENT AND BLANK IS ABSENT (2026-09-10): an absent key, a key holding only whitespace,
/// and a value <see cref="ISettingDefinition.Validate_OrNull"/> refuses all mean "this layer says
/// nothing" and the resolver falls to the layer below — never up to the caller as a half-answer. A
/// JSON <c>null</c> is the one exception: for a NULLABLE setting it IS an answer (it means "emit no
/// flag", not "layer said nothing"), so it stops the fall-through and is returned as-is. Telling
/// "the key is missing" from "the key is present and holds JSON null" needs more than
/// <see cref="SettingsJson_Path.Read_OrNull"/> returns (both read back as a null <see cref="JsonNode"/>),
/// which is why every walk below checks <see cref="JsonObject.ContainsKey"/> at each segment before
/// trusting what it reads.
/// </para>
/// <para>
/// THE PRESET TREE IS FLAT, THE CONFIG TREE IS NESTED. <c>Presets_Loader</c>'s embedded presets are
/// deliberately flat — one top-level key per catalogue path, literally <c>"phone.receipts"</c>,
/// never nested (see its own class doc and <c>kit/presets/classic.json</c>) — so a preset layer is
/// read by a single direct lookup of the whole dotted path as one key. config.json is the opposite:
/// a real nested object tree (<c>{"phone":{"receipts":...}}</c>), which is exactly what
/// <see cref="SettingsJson_Path"/> walks. The two lookups are kept as separate helpers below so
/// neither tree is ever walked the way the other one is shaped.
/// </para>
/// <para>
/// THE OLD SPELLING IS READ IN THE SAME LAYER, after the new one: <see cref="ISettingDefinition.LegacyPath_OrNull"/>
/// is tried only once the new path's answer for that same layer has been ruled out, never across
/// layers — a legacy key in config.json must not be beaten by a new-spelling key in the preset, or a
/// re-homed path would silently reset an owner's config.json value to the shipped default the day
/// the alias is read.
/// </para>
/// </summary>
public static class Settings_Resolver
{
    public static (JsonNode? Value, SettingOrigins Origin) Resolve(
        ISettingDefinition definition,
        JsonObject? presetTree,
        JsonObject? configTree,
        IOrchestrationSession? session)
    {
        if (definition.Scope == SettingScopes.Orchestration && session != null && Try_Session(definition, session, out var sessionValue))
            return (sessionValue, SettingOrigins.Session);

        if (Try_ConfigLayer(configTree, definition, out var configValue))
            return (configValue, SettingOrigins.ConfigFile);

        if (Try_PresetLayer(presetTree, definition, out var presetValue))
            return (presetValue, SettingOrigins.Preset);

        return (definition.Default_OrNull, SettingOrigins.ShippedDefault);
    }

    /// <summary>
    /// For a String or Enum setting — both are string-shaped on the wire, and Task 7's effort dial
    /// (Kind = Enum, nullable) shares this accessor with Task 6's model dial (Kind = String) rather
    /// than each writing its own cast.
    /// </summary>
    public static string? Resolve_String_OrNull(
        ISettingDefinition definition,
        JsonObject? presetTree,
        JsonObject? configTree,
        IOrchestrationSession? session)
    {
        Require_Kind(definition, SettingKinds.String, SettingKinds.Enum);

        var (value, _) = Resolve(definition, presetTree, configTree, session);
        return value?.GetValue<string>();
    }

    public static bool Resolve_Bool(
        ISettingDefinition definition,
        JsonObject? presetTree,
        JsonObject? configTree,
        IOrchestrationSession? session)
    {
        Require_Kind(definition, SettingKinds.Bool);

        var (value, _) = Resolve(definition, presetTree, configTree, session);
        return value!.GetValue<bool>();
    }

    public static int? Resolve_Int(
        ISettingDefinition definition,
        JsonObject? presetTree,
        JsonObject? configTree,
        IOrchestrationSession? session)
    {
        Require_Kind(definition, SettingKinds.Int);

        var (value, _) = Resolve(definition, presetTree, configTree, session);
        return value?.GetValue<int>();
    }

    static void Require_Kind(ISettingDefinition definition, params SettingKinds[] accepted)
    {
        if (!accepted.Contains(definition.Kind))
        {
            throw new InvalidOperationException(
                $"'{definition.Path}' is Kind={definition.Kind}, which does not match this accessor "
                + $"(expected {string.Join(" or ", accepted)}) — fix the call site, not the value.");
        }
    }

    static bool Try_Session(ISettingDefinition definition, IOrchestrationSession session, out JsonNode? value)
    {
        var node = SessionScoped_Reader.Read_OrNull(definition, session);

        if (node == null || definition.Validate_OrNull(node) != null)
        {
            value = null;
            return false;
        }

        value = node;
        return true;
    }

    static bool Try_ConfigLayer(JsonObject? tree, ISettingDefinition definition, out JsonNode? value)
    {
        if (Try_NestedPath(tree, definition.Path, definition, out value))
            return true;

        if (definition.LegacyPath_OrNull != null && Try_NestedPath(tree, definition.LegacyPath_OrNull, definition, out value))
            return true;

        value = null;
        return false;
    }

    static bool Try_PresetLayer(JsonObject? tree, ISettingDefinition definition, out JsonNode? value)
    {
        if (Try_FlatPath(tree, definition.Path, definition, out value))
            return true;

        if (definition.LegacyPath_OrNull != null && Try_FlatPath(tree, definition.LegacyPath_OrNull, definition, out value))
            return true;

        value = null;
        return false;
    }

    /// <summary>Walks <paramref name="tree"/> segment by segment, the same shape <see cref="SettingsJson_Path"/> reads.</summary>
    static bool Try_NestedPath(JsonObject? tree, string path, ISettingDefinition definition, out JsonNode? value)
    {
        value = null;

        if (tree == null || !NestedPath_Exists(tree, path))
            return false;

        return Try_Accept(definition, SettingsJson_Path.Read_OrNull(tree, path), out value);
    }

    /// <summary>One direct lookup of the whole dotted path as a single literal key — the preset shape.</summary>
    static bool Try_FlatPath(JsonObject? tree, string path, ISettingDefinition definition, out JsonNode? value)
    {
        value = null;

        if (tree == null || !tree.ContainsKey(path))
            return false;

        return Try_Accept(definition, tree[path], out value);
    }

    /// <summary>
    /// True when every segment of <paramref name="path"/> is an actual key in the tree, even when the
    /// final segment's value is JSON null — <see cref="JsonObject"/> indexing alone cannot tell
    /// "absent" from "present and null" apart, only <see cref="JsonObject.ContainsKey"/> can.
    /// </summary>
    static bool NestedPath_Exists(JsonObject root, string path)
    {
        JsonNode? current = root;

        foreach (var segment in path.Split('.'))
        {
            if (current is not JsonObject currentObject || !currentObject.ContainsKey(segment))
                return false;

            current = currentObject[segment];
        }

        return true;
    }

    /// <summary>
    /// A blank string is absent regardless of kind; anything else answers only when the definition
    /// accepts it — <see cref="ISettingDefinition.Validate_OrNull"/> itself already returns null for
    /// a JSON null on a nullable definition, which is how an explicit null becomes a real answer here.
    /// </summary>
    static bool Try_Accept(ISettingDefinition definition, JsonNode? node, out JsonNode? value)
    {
        if (Is_Blank(node) || definition.Validate_OrNull(node) != null)
        {
            value = null;
            return false;
        }

        value = node;
        return true;
    }

    static bool Is_Blank(JsonNode? node)
    {
        return node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text);
    }
}
