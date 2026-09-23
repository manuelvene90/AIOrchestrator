using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration.SettingsWriting;

/// <summary>
/// THE ONLY WRITER OF A CATALOGUE PATH ON BEHALF OF AN EDIT (plan 04 Task 2, 2026-09-23). The Telegram
/// <c>/settings</c> menu, the web page's PUT and the WPF window all hand their edit HERE. Three renderers each
/// carrying their own "is 240 a legal interval" would be three answers to one question, and the day one of
/// them drifted the phone would accept what the desktop refuses.
///
/// <para>
/// WHAT ELSE STILL WRITES A CATALOGUE KEY, stated rather than implied away: <see cref="OrchestratorConfig_Loader.Save"/>
/// rewrites <c>runners.*</c>, <c>limits.*</c>, the repo list and five Kernel scalars at their RESOLVED values for its two
/// remaining callers — the old Settings window until Task 9 replaces it, and the engine's /screenshots toggle
/// (PARKED, ruling P11) — and <c>ConfigRepos_Reorderer</c> / <c>ConfigRepoColor_Writer</c> edit <c>repos</c>,
/// which is a ReadOnly row here precisely because they own it. What Save no longer writes is any MODEL key
/// (D1): those six are written here, when their row is edited, and nowhere else.
/// </para>
///
/// <para>
/// THE DEFINITION DECIDES, THIS CLASS ONLY WRITES (CLAUDE.md decision 21: hooks advise, the app enforces at
/// the point of effect — and the point of effect for a setting is the write). Every "is this acceptable" is
/// <see cref="ISettingDefinition.Validate_OrNull"/>, asked once per edit, and its message is returned to the
/// owner verbatim: a renderer may CONSTRAIN what it offers (a picker over the enum words), but it never
/// judges a value and never invents a refusal. The two refusals this class owns are not judgements of a
/// value — a path no catalogue row answers to, and a row whose renderer is ReadOnly — and both are read off
/// the catalogue, never kept as a list of paths here: a second list is a place for the next ReadOnly row to
/// be forgotten.
/// </para>
/// <para>
/// THE WRITE LANDS UNDER THE NEW, NESTED PATH, AND EVERY OTHER SPELLING IS REMOVED FIRST (ruling P12). The
/// resolver reads, within one layer, the literal whole-path key, then the nested walk, then the legacy
/// spelling (<see cref="Settings_Resolver"/>). A flat <c>"phone.push"</c> copied from a preset would shadow
/// every nested write beneath it — applied on screen, invisible in effect — and a legacy
/// <c>supervisorModel</c> left behind would be a second answer the day the alias is retired. Writing under
/// the legacy spelling instead would re-create the alias for ever.
/// </para>
/// <para>
/// A JSON null IS A VALUE, AND RESET IS NOT A WRITE OF ONE (ruling P3). <see cref="Apply"/> with null asks
/// the definition like any other value: a nullable row accepts it as a stated nothing (an
/// <c>effort.supervisor</c> of null beats classic's xhigh — no <c>--effort</c> flag), every other row
/// refuses it. <see cref="Reset"/> DELETES the key and writes nothing in its place (spec §6.2): a
/// materialised default is a default that can never move again, frozen on the first button press — the rule
/// the loader has always kept for <c>reviewerModel</c>, enforced for every row.
/// </para>
/// <para>
/// THE READ IS THE LOADER'S OWN TOLERANT ONE (<see cref="OrchestratorConfig_Loader.Read_JsonObject_ForEditing"/>,
/// ruling P16), never a second copy: unknown keys survive every write (D6 — agents edit config.json at runtime,
/// and a write that dropped <c>planBackend</c> would be the defect <see cref="OrchestratorConfig_Loader.Save"/>'s
/// own docstring records being fixed twice), and a config.json that will not parse is REPLACED rather than
/// refused — refusing would strand the owner with a corrupt file and no way to fix it from the app — with one
/// warning line naming the file, because the owner is about to lose hand-edited keys they cannot see.
/// </para>
/// <para>
/// ATOMIC, and under <see cref="CONFIG_WRITE_LOCK"/>: see the lock's own doc for what it does and, more
/// importantly, what it does not restrain. A write that FAILS throws out of here rather than returning an
/// outcome — a write that did not happen must never be reported as one (<see cref="Atomic_FileWriter"/>).
/// </para>
/// </summary>
public static class Settings_Writer
{
    /// <summary>
    /// ONE READ-EDIT-WRITE OF config.json AT A TIME, IN THIS PROCESS (D10). Taken by every method here and by
    /// <see cref="OrchestratorConfig_Loader.Save"/> and <see cref="OrchestratorConfig_Loader.Save_BotToken"/>:
    /// a phone tap and a web PUT landing in the same second would otherwise each read the file, each apply
    /// their own edit, and the second rename would silently erase the first.
    ///
    /// <para>
    /// IN-PROCESS ONLY, stated plainly because the name invites the wrong belief: the WPF app and the daemon
    /// are two processes, an agent editing config.json by hand is a third, and a <see cref="Lock"/> in one
    /// restrains nothing in the others. <c>ConfigRepos_Reorderer</c> and <c>ConfigRepoColor_Writer</c> do not
    /// take it either (PARKED — see <see cref="OrchestratorConfig_Loader.Save"/>).
    /// </para>
    /// </summary>
    public static readonly Lock CONFIG_WRITE_LOCK = new();

    /// <summary>One edit — the same rules and the same single write as <c>Apply_Many</c>, because it IS a one-edit <c>Apply_Many</c>.</summary>
    public static (SettingsWriteOutcomes Outcome, string? Message_OrNull) Apply(ISupervisionPaths paths, string path, JsonNode? value, IOrchestrationLog? log)
    {
        var (_, outcome, message) = Apply_Many(paths, [(path, value)], log)[0];

        return (outcome, message);
    }

    /// <summary>
    /// A whole PUT body: every edit judged on its own, every accepted one applied to ONE read of the file, and
    /// ONE atomic write — a body of twelve settings as twelve read-modify-write cycles would be eleven of them
    /// racing the other ten. One refused edit does not refuse the rest; each result carries the path AS THE
    /// CALLER SPELLED IT (a legacy word included), so a handler can answer its own body key by key. When
    /// nothing is accepted the file is not touched at all. Edits to the same row apply in order, last wins.
    /// </summary>
    public static IReadOnlyList<(string Path, SettingsWriteOutcomes Outcome, string? Message_OrNull)> Apply_Many(
        ISupervisionPaths paths,
        IReadOnlyList<(string Path, JsonNode? Value)> edits,
        IOrchestrationLog? log)
    {
        return Apply_Many(paths, edits, log, Atomic_FileWriter.Write_AllText);
    }

    /// <summary>
    /// THE FILE WRITER IS A PARAMETER, for the reason <c>HostOptions_Factory.Create_FromArguments</c> takes its
    /// environment reader as one: "one write per PUT" is a property nobody can observe from the finished file,
    /// only by counting. Production passes <see cref="Atomic_FileWriter.Write_AllText"/> through the overload
    /// above and nothing else.
    /// </summary>
    public static IReadOnlyList<(string Path, SettingsWriteOutcomes Outcome, string? Message_OrNull)> Apply_Many(
        ISupervisionPaths paths,
        IReadOnlyList<(string Path, JsonNode? Value)> edits,
        IOrchestrationLog? log,
        Action<string, string> writeAllText)
    {
        List<(string Path, SettingsWriteOutcomes Outcome, string? Message_OrNull)> results = [];
        List<(ISettingDefinition Definition, JsonNode? Value)> accepted = [];

        foreach (var (path, value) in edits)
        {
            var definition = Catalog.Find_OrNull(path);
            var refusal = Refuse_Path_OrNull(definition, path) ?? Refuse_Value_OrNull(definition!, value);

            if (refusal != null)
            {
                results.Add((path, refusal.Value.Outcome, refusal.Value.Message));
                continue;
            }

            accepted.Add((definition!, value));
            results.Add((path, SettingsWriteOutcomes.Applied, null));
        }

        if (accepted.Count == 0)
            return results;

        lock (CONFIG_WRITE_LOCK)
        {
            var root = OrchestratorConfig_Loader.Read_JsonObject_ForEditing(paths.ConfigFile, log);

            foreach (var (definition, value) in accepted)
            {
                Remove_EverySpelling(root, definition);
                SettingsJson_Path.Write(root, definition.Path, value);
            }

            // Built before the writer opens anything: a serialisation that throws leaves the file as it was.
            var text = root.ToJsonString(JsonWriting.INDENTED);
            writeAllText(paths.ConfigFile, text);
        }

        return results;
    }

    /// <summary>
    /// Deletes every spelling of the row — nested, flat and legacy — so the value falls to the preset or the
    /// shipped default, and writes nothing in its place. A row that was never set is harmless: the file is not
    /// rewritten, and the message says there was nothing to reset rather than claiming an effect. A ReadOnly row
    /// is refused by the same rule as a write — a Reset of <c>repos</c> would delete the repository list.
    /// </summary>
    public static (SettingsWriteOutcomes Outcome, string? Message_OrNull) Reset(ISupervisionPaths paths, string path, IOrchestrationLog? log)
    {
        var definition = Catalog.Find_OrNull(path);
        var refusal = Refuse_Path_OrNull(definition, path);

        if (refusal != null)
            return refusal.Value;

        lock (CONFIG_WRITE_LOCK)
        {
            var root = OrchestratorConfig_Loader.Read_JsonObject_ForEditing(paths.ConfigFile, log);

            if (!Remove_EverySpelling(root, definition!))
                return (SettingsWriteOutcomes.Reset, Describe_NothingToReset(definition!));

            var text = root.ToJsonString(JsonWriting.INDENTED);
            Atomic_FileWriter.Write_AllText(paths.ConfigFile, text);
        }

        return (SettingsWriteOutcomes.Reset, null);
    }

    /// <summary>The two refusals that are about the PATH, read off the catalogue — null when the row may be written.</summary>
    static (SettingsWriteOutcomes Outcome, string Message)? Refuse_Path_OrNull(ISettingDefinition? definition, string path)
    {
        if (definition == null)
            return (SettingsWriteOutcomes.RefusedUnknownPath, Describe_UnknownPath(path));

        if (definition.Renderer == SettingRenderers.ReadOnly)
            return (SettingsWriteOutcomes.RefusedReadOnly, Describe_ReadOnly(definition));

        return null;
    }

    /// <summary>The definition's own verdict on the value, and its own words — never a message of this class's.</summary>
    static (SettingsWriteOutcomes Outcome, string Message)? Refuse_Value_OrNull(ISettingDefinition definition, JsonNode? value)
    {
        var problem = definition.Validate_OrNull(value);

        return problem == null ? null : (SettingsWriteOutcomes.RefusedInvalid, problem);
    }

    /// <summary>
    /// Every spelling the resolver would read for this row, in both shapes: the new path flat and nested, and
    /// the legacy path flat and nested. True when anything was there. The legacy spelling can never be another
    /// row's path — <c>SettingsCatalogTests.EveryPath_IsUnique_AndSoIsEveryLegacyPath</c> pins it — so this
    /// removes nothing that belongs to a neighbour.
    /// </summary>
    static bool Remove_EverySpelling(JsonObject root, ISettingDefinition definition)
    {
        var removed = Remove_BothShapes(root, definition.Path);

        if (definition.LegacyPath_OrNull != null)
            removed |= Remove_BothShapes(root, definition.LegacyPath_OrNull);

        return removed;
    }

    /// <summary>Non-short-circuit on purpose: whichever shape held the value, both must go.</summary>
    static bool Remove_BothShapes(JsonObject root, string path)
    {
        var flat = root.Remove(path);
        var nested = SettingsJson_Path.Remove(root, path);

        return flat | nested;
    }

    static string Describe_UnknownPath(string path)
    {
        return $"'{path}' is not a setting — no catalogue row answers to that path or to an old spelling of one.";
    }

    /// <summary>The row's own Description says where it IS changed (a window, config.json by hand, the command that toggles it).</summary>
    static string Describe_ReadOnly(ISettingDefinition definition)
    {
        return $"'{definition.Path}' is read-only here. {definition.Description}";
    }

    static string Describe_NothingToReset(ISettingDefinition definition)
    {
        return $"'{definition.Path}' is not set in config.json, so there was nothing to reset — it already reads its preset or shipped default.";
    }
}
