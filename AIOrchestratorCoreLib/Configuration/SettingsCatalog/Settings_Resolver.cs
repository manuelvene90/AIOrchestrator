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
/// EACH LAYER TRIES BOTH SHAPES, LITERAL KEY FIRST (2026-09-12): the shipped presets really are flat
/// — one top-level key per catalogue path, literally <c>"phone.receipts"</c>, never nested (see
/// <c>Presets_Loader</c>'s own class doc and <c>kit/presets/classic.json</c>) — but a preset loaded
/// from a file path is free text an owner wrote by hand, and config.json is nominally nested but
/// nothing stops someone hand-writing a flat-dotted key there, which <see cref="SettingsJson_Path"/>'s
/// own doc calls legal for a single-segment path. A layer that could only see ONE of the two shapes
/// silently read the other as absent, which is the exact failure this catalogue exists to prevent.
/// So both the preset layer and the config layer first try the literal whole-path key as a single
/// dictionary lookup, then fall back to the segment walk. For a single-segment path the two lookups
/// are the same operation, so nothing changes there. For a multi-segment path both can only hit when
/// someone has written the key twice — the literal whole-path key wins, since it is tried first.
/// </para>
/// <para>
/// THE OLD SPELLING IS READ IN THE SAME LAYER, after the new one: <see cref="ISettingDefinition.LegacyPath_OrNull"/>
/// is tried — literal key first, then the segment walk, same as the new path — only once the new
/// path's answer for that same layer has been ruled out, never across layers — a legacy key in
/// config.json must not be beaten by a new-spelling key in the preset, or a re-homed path would
/// silently reset an owner's config.json value to the shipped default the day the alias is read.
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

    /// <summary>
    /// 64-bit, not 32: three Kind=Int rows (<c>telegramSupergroupChatId</c>, <c>telegramOwnerUserId</c>,
    /// <c>orchestrationTokenBudget</c>) are nullable with no bounds and the catalogue's own validator
    /// reads them as <see cref="long"/> — a real Telegram supergroup id looks like
    /// <c>-1001234567890</c>, which does not fit <see cref="int"/>. An accessor that called
    /// <c>GetValue&lt;int&gt;()</c> would throw on exactly the values the catalogue was widened to
    /// accept.
    ///
    /// <para>
    /// AND IT READS ITS OWN SHIPPED DEFAULT (fixed 2026-09-17). Until this fix the accessor threw
    /// <see cref="InvalidOperationException"/> on the DEFAULT layer of every <c>Kind = Int</c> row
    /// carrying a non-null shipped default — that is, on an unconfigured machine, which is the
    /// ordinary case. <see cref="SettingDefinition_Factory.Create_Int"/> boxes the default with
    /// <c>JsonValue.Create(int)</c>, and a VALUE-backed <see cref="JsonValue"/> demands an exact type
    /// match from <c>GetValue&lt;T&gt;()</c>, unlike a node parsed from real JSON text, which is
    /// element-backed and converts freely between numeric types. So the layer the accessor exists to
    /// fall back to was the one layer it could not read. It went unseen because the three Kind=Int
    /// rows that existed were all NULLABLE with a null default, where <c>value?.</c> short-circuits
    /// before the cast — and because this accessor had no production caller at all until the
    /// <c>reviewing.*</c> dials, whose defaults are not null. Found by that block's round-trip test.
    /// </para>
    /// <para>
    /// BOTH SHAPES, AND STILL LOUD ON A WRONG ONE: an int and a long are both read, anything else
    /// still throws out of <c>GetValue&lt;long&gt;()</c> rather than becoming a quiet fallback — a
    /// setting whose value is a string is a fault to report, not a default to substitute.
    /// </para>
    /// </summary>
    public static long? Resolve_Long(
        ISettingDefinition definition,
        JsonObject? presetTree,
        JsonObject? configTree,
        IOrchestrationSession? session)
    {
        Require_Kind(definition, SettingKinds.Int);

        var (value, _) = Resolve(definition, presetTree, configTree, session);

        if (value == null)
            return null;

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var asInt))
            return asInt;

        return value.GetValue<long>();
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

    /// <summary>Literal whole-path key first, then the segment walk — see the class doc.</summary>
    static bool Try_ConfigLayer(JsonObject? tree, ISettingDefinition definition, out JsonNode? value)
    {
        if (Try_EitherShape(tree, definition.Path, definition, out value))
            return true;

        if (definition.LegacyPath_OrNull != null && Try_EitherShape(tree, definition.LegacyPath_OrNull, definition, out value))
            return true;

        value = null;
        return false;
    }

    /// <summary>Literal whole-path key first, then the segment walk — see the class doc.</summary>
    static bool Try_PresetLayer(JsonObject? tree, ISettingDefinition definition, out JsonNode? value)
    {
        if (Try_EitherShape(tree, definition.Path, definition, out value))
            return true;

        if (definition.LegacyPath_OrNull != null && Try_EitherShape(tree, definition.LegacyPath_OrNull, definition, out value))
            return true;

        value = null;
        return false;
    }

    /// <summary>
    /// The literal whole-path key wins when both shapes are present — see the class doc's stated
    /// order. For a single-segment path the two lookups are the same operation.
    /// </summary>
    static bool Try_EitherShape(JsonObject? tree, string path, ISettingDefinition definition, out JsonNode? value)
    {
        if (Try_FlatPath(tree, path, definition, out value))
            return true;

        if (Try_NestedPath(tree, path, definition, out value))
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
