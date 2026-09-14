using AIOrchestratorCoreLib.Running;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration.EffortSettings;

public static class EffortSettings_Factory
{
    /// <summary>
    /// EVERY ROLE MUST BE STATED, null included. A partial map would make "this role was never
    /// resolved" and "this role resolves to no flag" the same value, and the second is a real answer
    /// the owner can write (see <see cref="IEffortSettings"/>). So the absence is refused here, at
    /// construction, naming the role — an invariant violation, which this codebase throws for
    /// (`.claude/rules/code-conventions.md`).
    /// </summary>
    public static IEffortSettings Create(IReadOnlyDictionary<SessionRoles, string?> levelsByRole)
    {
        Dictionary<SessionRoles, string?> levels = [];

        foreach (var role in SessionRole_Names.ALL)
        {
            if (!levelsByRole.TryGetValue(role, out var level))
                throw new ArgumentException($"No effort level was resolved for role {role} — every role in SessionRole_Names.ALL must be present, null included");

            levels[role] = level;
        }

        return new EffortSettingsModel(levels);
    }

    /// <summary>
    /// What an absent <c>effort</c> block means WITH NO PRESET UNDERNEATH IT: the catalogue's own
    /// shipped default for each role, which today is null for all six — no <c>--effort</c> flag, the
    /// CLI's own default.
    ///
    /// <para>
    /// IT IS NOT CLASSIC'S ANSWER, and the distinction is deliberate rather than an oversight: the
    /// PRESET rung belongs to <see cref="OrchestratorConfig_Loader"/>, exactly as it does for the six
    /// model keys, because only the loader has a config.json to read the <c>preset</c> key from. This
    /// is the fallback for a config ASSEMBLED IN MEMORY (the Settings window rebuilding one to save
    /// it, <c>Create_Empty</c> in tests) — and no such config is ever spawned from, because every
    /// spawn asks <c>IOrchestratorConfigProvider.Get_Current()</c>, which loads from disk.
    /// </para>
    /// <para>
    /// READ FROM THE CATALOGUE rather than writing six nulls here, so the shipped default has ONE
    /// home (CLAUDE.md decision 12). Unlike the six <c>DEFAULT_*_MODEL</c> fields of
    /// <see cref="OrchestratorConfig.OrchestratorConfig_Factory"/>, this direction carries no
    /// type-initializer cycle risk: nothing in <c>SettingsCatalog</c> reads this class back.
    /// </para>
    /// </summary>
    public static IEffortSettings Create_Default()
    {
        Dictionary<SessionRoles, string?> levels = [];

        foreach (var role in SessionRole_Names.ALL)
            levels[role] = Read_ShippedEffort_OrNull(role);

        return Create(levels);
    }

    static string? Read_ShippedEffort_OrNull(SessionRoles role)
    {
        var definition = Catalog.Find_OrNull(Catalog.Get_EffortPath(role))
            ?? throw new Exception($"No catalogue entry for {Catalog.Get_EffortPath(role)} — a role without a registered effort default cannot spawn");

        return definition.Default_OrNull?.GetValue<string>();
    }
}
