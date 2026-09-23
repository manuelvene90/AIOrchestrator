using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation;

/// <summary>
/// EVERY SETTING, READ ONCE, THE SAME WAY FOR ALL THREE RENDERERS (plan 04 Task 1). The Telegram menu, the
/// web GET and the WPF window each ask this for a snapshot and draw it; none of them resolves a value,
/// labels an origin or decides what to offer, because each of those would otherwise be written three times
/// (CLAUDE.md decision 12).
///
/// <para>
/// THE PURE OVERLOADS TAKE THE LAYERS AS PARAMETERS, for <see cref="Settings_Resolver"/>'s reason: a
/// snapshot is provable without a file. The <c>_FromDisk</c> overloads read config.json through the
/// loader's own tolerant read and resolve the preset through the loader's own fallback — CALLED, never
/// copied (ruling P16): a second "a mistyped preset word means classic" would be the drift this reader
/// exists to end, and a second "what an unreadable config.json is" would be another.
/// </para>
/// <para>
/// IT NEVER THROWS OVER THE OWNER'S FILE. It is called by an HTTP GET that must answer and by a Telegram tap
/// that must answer, and a hand-edit with a trailing comma is the ordinary way a config.json stops parsing:
/// that file reads as empty, with one warning line naming it (decision 21's corollary; not Telegram,
/// decision 15). The loader's own startup path is not changed by this — see
/// <c>OrchestratorConfig_Loader.Read_JsonObject_ForEditing</c>.
/// </para>
/// </summary>
public static class SettingsSnapshot_Reader
{
    /// <summary>
    /// THE ONE SECRET IN THE CATALOGUE, MASKED HERE (ruling P2, 2026-09-23). The web page's GET is open on
    /// loopback (owner, D4), so a reading that carried the token's value would hand it to anything on the
    /// machine that can open a socket, and "a set token is still enforced" would enforce nothing. Masking in
    /// the reading rather than in each renderer is what makes it hold for all three at once. The ORIGIN is
    /// kept — who set the token is not a secret — and the host that must compare the token reads the raw
    /// value through <see cref="Read_Trees_FromDisk"/> and the resolver, never through a reading.
    /// A second secret would need a second line here; that is the day to promote it to a catalogue flag.
    /// </summary>
    public const string MASKED_SECRET_PATH = "web.token";

    /// <summary>What a masked secret reads as when it holds a value. Absent or blank reads <see cref="SettingValue_Formatter.NOT_SET"/>.</summary>
    public const string SECRET_SET = "set";

    /// <summary>
    /// WHETHER A SECRET'S TEXT COUNTS AS SET — anything but absent, empty or whitespace. ONE predicate for the
    /// mask below and for the web handler's gate (<c>Web.SettingsRequest_Handler</c>, plan 04 Task 6), because
    /// the page shows this row's "set" / "not set" and the handler decides whether editing needs the token: two
    /// copies of the rule could leave the page saying "not set" about a token the handler then demands.
    /// </summary>
    public static bool Is_SecretSet(string? text)
    {
        return !string.IsNullOrWhiteSpace(text);
    }

    public static IReadOnlyList<ISettingReading> Read_All(
        JsonObject? configTree,
        JsonObject? presetTree,
        string presetName,
        IOrchestrationSession? session)
    {
        List<ISettingReading> readings = [];

        foreach (var definition in Catalog.ALL)
            readings.Add(Read(definition, configTree, presetTree, presetName, session));

        return readings;
    }

    /// <summary>
    /// The whole snapshot straight off disk, and the preset name it was resolved under — which the renderers
    /// SHOW in their header and never offer as a row (D13). <paramref name="log"/> receives the one line for
    /// an unreadable config.json or a preset word that does not resolve.
    /// </summary>
    public static (IReadOnlyList<ISettingReading> Readings, string PresetName) Read_All_FromDisk(
        ISupervisionPaths paths,
        IOrchestrationSession? session,
        IOrchestrationLog? log)
    {
        var (configTree, presetTree, presetName) = Read_Trees_FromDisk(paths, log);

        return (Read_All(configTree, presetTree, presetName, session), presetName);
    }

    /// <summary>
    /// One row, by its catalogue path or its legacy spelling (<c>SettingsCatalog.Find_OrNull</c>'s own
    /// matching), or null when no row answers to it — the <c>preset</c> key included, which is not a row.
    /// </summary>
    public static ISettingReading? Read_One_OrNull(
        string path,
        JsonObject? configTree,
        JsonObject? presetTree,
        string presetName,
        IOrchestrationSession? session)
    {
        var definition = Catalog.Find_OrNull(path);

        return definition == null ? null : Read(definition, configTree, presetTree, presetName, session);
    }

    /// <summary>One row straight off disk — the web host's door for <c>web.listen</c> (ruling P10).</summary>
    public static ISettingReading? Read_One_FromDisk_OrNull(
        string path,
        ISupervisionPaths paths,
        IOrchestrationSession? session,
        IOrchestrationLog? log)
    {
        var definition = Catalog.Find_OrNull(path);

        if (definition == null)
            return null;

        var (configTree, presetTree, presetName) = Read_Trees_FromDisk(paths, log);

        return Read(definition, configTree, presetTree, presetName, session);
    }

    /// <summary>
    /// The two layers a snapshot resolves over, as the loader reads them: config.json through its tolerant
    /// read (an unreadable file is an empty tree and one warning), the preset through its classic fallback.
    /// INTERNAL so the web host (plan 04 Task 7) can resolve <see cref="MASKED_SECRET_PATH"/>'s raw value
    /// through <see cref="Settings_Resolver"/> over the SAME two trees a reading would have used, rather than
    /// through a reading that masks it or a read of its own.
    /// </summary>
    internal static (JsonObject ConfigTree, JsonObject PresetTree, string PresetName) Read_Trees_FromDisk(
        ISupervisionPaths paths,
        IOrchestrationLog? log)
    {
        var configTree = OrchestratorConfig_Loader.Read_JsonObject_ForEditing(paths.ConfigFile, log);
        var (presetTree, presetName) = OrchestratorConfig_Loader.Resolve_Preset_OrClassic(configTree, log);

        return (configTree, presetTree, presetName);
    }

    static ISettingReading Read(
        ISettingDefinition definition,
        JsonObject? configTree,
        JsonObject? presetTree,
        string presetName,
        IOrchestrationSession? session)
    {
        var (value, origin) = Settings_Resolver.Resolve(definition, presetTree, configTree, session);

        var isMasked = string.Equals(definition.Path, MASKED_SECRET_PATH, StringComparison.Ordinal);

        return SettingReading_Factory.Create(
            definition,
            isMasked ? null : value,
            origin,
            isMasked ? Describe_Masked(value) : SettingValue_Formatter.Describe(definition, value),
            SettingOrigin_Labels.Describe(origin, presetName),
            Describe_SessionNote_OrNull(definition, origin, configTree, presetTree, presetName),
            Offer_Values(definition));
    }

    static string Describe_Masked(JsonNode? value)
    {
        var isSet = value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) && Is_SecretSet(text);

        return isSet ? SECRET_SET : SettingValue_Formatter.NOT_SET;
    }

    /// <summary>
    /// WHAT THE SESSION SHADOWS, resolved the same way with no session: the value this row would read on the
    /// machine, and where THAT came from. Without it, editing a session-overridden row writes config.json and
    /// the screen does not move, which reads exactly like a write that failed.
    /// </summary>
    static string? Describe_SessionNote_OrNull(
        ISettingDefinition definition,
        SettingOrigins origin,
        JsonObject? configTree,
        JsonObject? presetTree,
        string presetName)
    {
        if (origin != SettingOrigins.Session)
            return null;

        var (machineValue, machineOrigin) = Settings_Resolver.Resolve(definition, presetTree, configTree, session: null);

        return "This orchestration states it in its session.json, which wins over the machine's value: "
            + $"{SettingValue_Formatter.Describe(definition, machineValue)} ({SettingOrigin_Labels.Describe(machineOrigin, presetName)}).";
    }

    /// <summary>
    /// THE CHOICES A RENDERER MAY DRAW. One rule per renderer kind, and for the picker lists a SOURCE, read
    /// from the code that owns the words and never copied from it:
    /// <list type="bullet">
    /// <item><c>pulse.fields</c> offers <see cref="PulseField_Names.ALL"/> — the list its validator reads, so
    /// a new field is offered the day it is registered.</item>
    /// <item><c>pulse.buttons</c> and <c>general.buttons</c> offer the verbs a bar can actually DRAW (ruling
    /// P5): every configurable verb — the "/" menu's and the shipped bars', which is where "tail sup" comes
    /// from — kept only when <see cref="TopicCommandButtons.Has_TapRoute"/> says a tap can run it. Plan 03
    /// Task 5 left twenty menu verbs with no tap route and the builders refuse to draw them, so offering
    /// <see cref="BotCommandMenu.ALL"/> whole would offer twenty buttons that never appear. The predicate is
    /// read, not its set, so a verb wired later is offered without a change here.</item>
    /// <item>Every other list — <c>highRiskPatterns</c> today — offers NOTHING: it is free text, and an
    /// empty offer is what tells a renderer to draw add / remove / reorder over typed words.</item>
    /// </list>
    /// </summary>
    static IReadOnlyList<string> Offer_Values(ISettingDefinition definition)
    {
        return definition.Renderer switch
        {
            SettingRenderers.Toggle => [SettingValue_Formatter.ON, SettingValue_Formatter.OFF],
            SettingRenderers.Choice => Offer_Choices(definition),
            SettingRenderers.OrderedList => Offer_ListWords(definition),
            SettingRenderers.Number or SettingRenderers.Text or SettingRenderers.ReadOnly => [],
            _ => throw new InvalidOperationException($"Unhandled SettingRenderers: {definition.Renderer}"),
        };
    }

    /// <summary>The enum words, plus the null meaning when the row accepts null (ruling P19: read off the definition itself).</summary>
    static IReadOnlyList<string> Offer_Choices(ISettingDefinition definition)
    {
        if (definition.Validate_OrNull(null) != null)
            return definition.EnumValues;

        return [.. definition.EnumValues, SettingValue_Formatter.NOT_SET];
    }

    static IReadOnlyList<string> Offer_ListWords(ISettingDefinition definition)
    {
        return definition.Path switch
        {
            PULSE_FIELDS_PATH => PulseField_Names.ALL,
            PULSE_BUTTONS_PATH or GENERAL_BUTTONS_PATH => Drawable_Verbs(),
            _ => [],
        };
    }

    static IReadOnlyList<string> Drawable_Verbs()
    {
        return BotCommandMenu.ALL.Select(command => command.Command)
            .Concat(TopicCommandButtons.Commands)
            .Concat(TopicCommandButtons.GeneralCommands)
            .Distinct(StringComparer.Ordinal)
            .Where(TopicCommandButtons.Has_TapRoute)
            .ToArray();
    }

    const string PULSE_FIELDS_PATH = "pulse.fields";
    const string PULSE_BUTTONS_PATH = "pulse.buttons";
    const string GENERAL_BUTTONS_PATH = "general.buttons";
}
