using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using Xunit;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// THE INVARIANTS SPEC §6.1 NAMES, and each one is a defect that would otherwise surface in three
/// renderers separately: "every path is unique; every definition has a default of the declared kind,
/// a label and a description; both shipped presets parse and reference only catalogue paths; a
/// Composite entry names a parser that exists."
///
/// <para>
/// THE CATALOGUE IS REACHED THROUGH AN ALIAS, and that is not cosmetic: this test's own namespace
/// ends in <c>SettingsCatalog</c> too, so the bare word resolves to the enclosing NAMESPACE before
/// C# ever looks at a using directive, and every reference would be CS0118 "namespace used like a
/// type". The alias names the type once and the rest of the file reads as the brief wrote it.
/// </para>
/// </summary>
public class SettingsCatalogTests
{
    [Fact]
    public void EveryPath_IsUnique_AndSoIsEveryLegacyPath()
    {
        var paths = Catalog.ALL.Select(definition => definition.Path).ToList();
        Assert.Equal(paths.Count, paths.Distinct(StringComparer.Ordinal).Count());

        var legacy = Catalog.ALL.Where(d => d.LegacyPath_OrNull != null).Select(d => d.LegacyPath_OrNull!).ToList();
        Assert.Equal(legacy.Count, legacy.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(legacy.Intersect(paths, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryDefinition_HasALabelAndADescription()
    {
        foreach (var definition in Catalog.ALL)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.Label), $"{definition.Path} has no label");
            Assert.False(string.IsNullOrWhiteSpace(definition.Description), $"{definition.Path} has no description");
        }
    }

    /// <summary>
    /// THE BOTTOM OF THE PRECEDENCE CHAIN CANNOT BE A LIE. A default of the wrong kind, or one the
    /// definition's own validator refuses, means every layer above it is resolving against nonsense.
    /// </summary>
    [Fact]
    public void EveryShippedDefault_SatisfiesItsOwnDefinition()
    {
        foreach (var definition in Catalog.ALL)
        {
            if (definition.Kind == SettingKinds.Composite)
                continue;

            Assert.Null(definition.Validate_OrNull(definition.Default_OrNull));
        }
    }

    [Fact]
    public void EveryCompositeEntry_NamesATypeThatExists()
    {
        foreach (var definition in Catalog.ALL.Where(d => d.Kind == SettingKinds.Composite))
        {
            var type = typeof(Catalog).Assembly.GetType(definition.CompositeParser_OrNull!);
            Assert.True(type != null, $"{definition.Path} names parser '{definition.CompositeParser_OrNull}', which is not a type in this assembly");
        }
    }

    /// <summary>
    /// A CATEGORY IS THE MENU STRUCTURE OF EVERY RENDERER (spec §6.1), so an empty one is a blank
    /// tab. `Kit` is the one exemption and it is named rather than skipped by a predicate: it fills
    /// in plan 05 with `repos[].code` and the per-user exports, and naming it here means the day
    /// that lands, this test is the thing that notices the exemption is stale.
    /// </summary>
    [Fact]
    public void EveryCategory_ExceptKit_HasAtLeastOneEntry()
    {
        foreach (var category in Enum.GetValues<SettingCategories>())
        {
            if (category == SettingCategories.Kit)
                continue;

            Assert.NotEmpty(Catalog.In_Category(category));
        }
    }

    [Fact]
    public void EveryRole_HasAModelEntryAndAnEffortEntry()
    {
        foreach (var role in SessionRole_Names.ALL)
        {
            Assert.NotNull(Catalog.Find_OrNull(Catalog.Get_ModelPath(role)));
            Assert.NotNull(Catalog.Find_OrNull(Catalog.Get_EffortPath(role)));
        }
    }

    /// <summary>
    /// THE GAP THIS PLAN CLOSES. `runners.&lt;role&gt;.wake` and `runners.&lt;role&gt;.bookkeeping`
    /// sit right beside `runner` and `resume` in config.json (RunnerConfigs_Json reads and writes all
    /// four already) but carried no catalogue row, so they were settable only by hand-editing
    /// config.json — no preset, no settings surface. Named per role, the way
    /// <see cref="EveryRole_HasAModelEntryAndAnEffortEntry"/> is, so a missing role names itself.
    /// </summary>
    [Fact]
    public void EveryRole_HasAWakeEntryAndABookkeepingEntry()
    {
        foreach (var role in SessionRole_Names.ALL)
        {
            var key = SessionRole_Names.Get_ConfigKey(role);

            Assert.NotNull(Catalog.Find_OrNull($"{RunnerConfigs_Json.RUNNERS_KEY}.{key}.{RunnerConfigs_Json.WAKE_KEY}"));
            Assert.NotNull(Catalog.Find_OrNull($"{RunnerConfigs_Json.RUNNERS_KEY}.{key}.{RunnerConfigs_Json.BOOKKEEPING_KEY}"));
        }
    }

    /// <summary>
    /// THE DEFAULTS MUST NOT MOVE. <see cref="RoleRunnerConfig_Factory.Create_Default"/> is the one
    /// place that decides what a machine stating nothing actually runs — Watcher, Channel, for every
    /// role — and the catalogue row's shipped default is read FROM it (the same direction
    /// <c>runner</c>'s and <c>resume</c>'s rows already take), so this pins that registering the two
    /// keys here did not silently change behaviour for a machine whose config.json says nothing.
    /// </summary>
    [Fact]
    public void TheWakeAndBookkeepingShippedDefaults_MatchRoleRunnerConfigFactory_ForEveryRole()
    {
        foreach (var role in SessionRole_Names.ALL)
        {
            var key = SessionRole_Names.Get_ConfigKey(role);
            var roleDefault = RoleRunnerConfig_Factory.Create_Default(role);

            var wakeDefinition = Catalog.Find_OrNull($"{RunnerConfigs_Json.RUNNERS_KEY}.{key}.{RunnerConfigs_Json.WAKE_KEY}")!;
            var bookkeepingDefinition = Catalog.Find_OrNull($"{RunnerConfigs_Json.RUNNERS_KEY}.{key}.{RunnerConfigs_Json.BOOKKEEPING_KEY}")!;

            Assert.Equal(WakeMode_Names.WATCHER, wakeDefinition.Default_OrNull!.GetValue<string>());
            Assert.Equal(WakeModes.Watcher, roleDefault.Wake);

            Assert.Equal(BookkeepingSink_Names.CHANNEL, bookkeepingDefinition.Default_OrNull!.GetValue<string>());
            Assert.Equal(BookkeepingSinks.Channel, roleDefault.Bookkeeping);
        }
    }

    /// <summary>
    /// THE ROUND TRIP, THROUGH THE ACTUAL READER — not just that the catalogue path string matches
    /// <see cref="RunnerConfigs_Json"/>'s constants by eye. A value written at the catalogue's own path
    /// (<see cref="SettingsJson_Path.Write"/>, the same walk the resolver and every renderer use) must
    /// come back out of <see cref="RunnerConfigs_Json.Parse"/> as the non-default mode — proving a
    /// settings surface that only knows the catalogue path actually reaches the session's real
    /// behaviour, for both keys, on a role that is not the first in the list (so a hard-coded
    /// "supervisor" in either writer could not pass this by accident).
    /// </summary>
    [Fact]
    public void AWakeAndBookkeepingValue_WrittenAtTheCataloguePath_RoundTripsThroughRunnerConfigsJson()
    {
        var configRoot = new JsonObject();

        SettingsJson_Path.Write(configRoot, "runners.implementer.wake", JsonValue.Create(WakeMode_Names.TICKET));
        SettingsJson_Path.Write(configRoot, "runners.implementer.bookkeeping", JsonValue.Create(BookkeepingSink_Names.LOG));

        var parsed = RunnerConfigs_Json.Parse(configRoot);
        var implementer = parsed.Get_ForRole(SessionRoles.Implementer);
        var supervisor = parsed.Get_ForRole(SessionRoles.Supervisor);

        Assert.Equal(WakeModes.Ticket, implementer.Wake);
        Assert.Equal(BookkeepingSinks.Log, implementer.Bookkeeping);

        // AND THE ROLE THAT WAS NOT WRITTEN KEEPS THE DEFAULT — proving the write landed at the
        // implementer's own node rather than leaking into every role's.
        Assert.Equal(WakeModes.Watcher, supervisor.Wake);
        Assert.Equal(BookkeepingSinks.Channel, supervisor.Bookkeeping);
    }

    /// <summary>
    /// THE OLD SPELLING STILL FINDS THE SETTING. Six model keys and two telegram keys are re-homed
    /// under a block by spec §6.4 (`telegram.foldLongEntriesAbove` → `phone.foldLongEntriesAbove`,
    /// "with the old path read as an alias"), and every config.json on both brothers' machines is
    /// written in the old spelling. A rename that loses the owner's value is a model change nobody
    /// asked for — the exact failure spec §5.2 warns about for the model default.
    /// </summary>
    [Fact]
    public void TheLegacySpellings_StillResolveToTheirDefinitions()
    {
        Assert.Equal("models.supervisor", Catalog.Find_OrNull("supervisorModel")!.Path);
        Assert.Equal("models.general", Catalog.Find_OrNull("generalSupervisorModel")!.Path);
        Assert.Equal("phone.foldLongEntriesAbove", Catalog.Find_OrNull("telegram.foldLongEntriesAbove")!.Path);
        Assert.Equal("phone.attachEntriesAbove", Catalog.Find_OrNull("telegram.attachEntriesAbove")!.Path);
    }

    /// <summary>
    /// THE BOT TOKEN IS NOT IN THE CATALOGUE, and this is an assertion rather than a comment because
    /// every renderer lists the catalogue: a Telegram /settings menu that prints the token posts the
    /// token into the chat the token controls. It lives in secrets.json, which the loader reads
    /// separately and this registry never names.
    /// </summary>
    [Fact]
    public void NoDefinition_ExposesTheBotToken()
    {
        Assert.DoesNotContain(Catalog.ALL, d =>
            d.Path.Contains("token", StringComparison.OrdinalIgnoreCase) && d.Path.Contains("bot", StringComparison.OrdinalIgnoreCase));
        Assert.Null(Catalog.Find_OrNull("telegramBotToken"));
    }

    /// <summary>
    /// THE FOUR ORCHESTRATION-SCOPE DIALS, and only those four plus the three session states: Scope
    /// says WHERE the topmost layer of a value may live, and a renderer that writes a Machine-scope
    /// key into session.json would write it where no reader looks.
    /// </summary>
    [Fact]
    public void OnlyTheDialsAndTheSessionStates_AreOrchestrationScoped()
    {
        var orchestrationScoped = Catalog.ALL
            .Where(d => d.Scope == SettingScopes.Orchestration)
            .Select(d => d.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["effort.implementer", "effort.supervisor", "models.implementer", "models.supervisor",
             "session.ownerPresence", "session.paused", "session.telegramMode"],
            orchestrationScoped);
    }

    /// <summary>
    /// A REAL TELEGRAM SUPERGROUP CHAT ID IS THREE ORDERS OF MAGNITUDE PAST INT32
    /// (<c>-1001234567890</c>). The shipped definition stays <c>Kind = Int</c> with no range and must
    /// accept that value while still refusing a non-integer.
    /// </summary>
    [Fact]
    public void TelegramSupergroupChatId_AcceptsAValuePastInt32_AndStillRefusesANonInteger()
    {
        var definition = Catalog.Find_OrNull("telegramSupergroupChatId")!;

        Assert.Null(definition.Validate_OrNull(JsonValue.Create(-1001234567890L)));
        Assert.NotNull(definition.Validate_OrNull(JsonValue.Create("abc")));
        Assert.NotNull(definition.Validate_OrNull(JsonValue.Create(1.5)));
    }

    /// <summary>
    /// THE THREE <c>session.*</c> ROWS ARE STATE THE APP WRITES, NOT A SETTING A RENDERER MAY EDIT
    /// (CLAUDE.md decision 21) — a renderer draws its control from <c>Renderer</c>, so each of the
    /// three must ship <c>ReadOnly</c> while still keeping its own <c>Kind</c>.
    /// </summary>
    [Fact]
    public void EverySessionStateRow_ShipsReadOnly_ButKeepsItsOwnKind()
    {
        var paused = Catalog.Find_OrNull("session.paused")!;
        var telegramMode = Catalog.Find_OrNull("session.telegramMode")!;
        var ownerPresence = Catalog.Find_OrNull("session.ownerPresence")!;

        Assert.Equal(SettingKinds.Bool, paused.Kind);
        Assert.Equal(SettingRenderers.ReadOnly, paused.Renderer);

        Assert.Equal(SettingKinds.Enum, telegramMode.Kind);
        Assert.Equal(SettingRenderers.ReadOnly, telegramMode.Renderer);

        Assert.Equal(SettingKinds.Enum, ownerPresence.Kind);
        Assert.Equal(SettingRenderers.ReadOnly, ownerPresence.Renderer);
    }
}
