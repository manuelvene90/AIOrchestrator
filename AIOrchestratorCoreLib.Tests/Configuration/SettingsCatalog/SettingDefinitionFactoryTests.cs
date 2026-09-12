using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// A DEFINITION IS DATA THAT THREE RENDERERS AND ONE RESOLVER READ, so what it refuses to be built
/// with is the whole of its contract: a definition with no label renders as a blank row, and a
/// definition whose default is not of its own kind makes the bottom of the precedence chain a lie.
/// Both are cheaper to refuse at construction than to find in a WPF tab, a Telegram tap and an
/// HTTP PUT separately (spec §12: "a setting that exists in three renderers and one probe is the
/// shape most likely to drift").
/// </summary>
public class SettingDefinitionFactoryTests
{
    [Fact]
    public void Create_Bool_CarriesItsDefaultAsAJsonBoolean_AndRendersAsAToggle()
    {
        var definition = SettingDefinition_Factory.Create_Bool(
            path: "phone.appMessagesRing",
            shippedDefault: true,
            scope: SettingScopes.Machine,
            category: SettingCategories.Phone,
            label: "App messages ring",
            description: "Whether the app's own notices make the phone sound.",
            restart: RestartKinds.None);

        Assert.Equal(SettingKinds.Bool, definition.Kind);
        Assert.Equal(SettingRenderers.Toggle, definition.Renderer);
        Assert.True(definition.Default_OrNull!.GetValue<bool>());
        Assert.Null(definition.Validate_OrNull(JsonValue.Create(false)));
        Assert.NotNull(definition.Validate_OrNull(JsonValue.Create("yes")));
    }

    [Fact]
    public void Create_Enum_RefusesAValueOutsideItsList_AndNamesTheAllowedWords()
    {
        var definition = SettingDefinition_Factory.Create_Enum(
            path: "phone.receipts",
            values: ["ticks", "reactions"],
            shippedDefault: "ticks",
            scope: SettingScopes.Machine,
            category: SettingCategories.Receipts,
            label: "Receipts",
            description: "Ticks on a message, or reactions on the owner's own bubble.",
            restart: RestartKinds.None);

        Assert.Null(definition.Validate_OrNull(JsonValue.Create("reactions")));

        var message = definition.Validate_OrNull(JsonValue.Create("emoji"));

        Assert.NotNull(message);
        Assert.Contains("ticks", message);
        Assert.Contains("reactions", message);
    }

    /// <summary>A null default is legal ONLY where null is a meaning — effort's "no --effort flag".</summary>
    [Fact]
    public void Create_Enum_WithNullableTrue_AcceptsNullAsItsShippedDefault()
    {
        var definition = SettingDefinition_Factory.Create_Enum(
            path: "effort.supervisor",
            values: ["low", "medium", "high", "xhigh", "max"],
            shippedDefault: null,
            scope: SettingScopes.Orchestration,
            category: SettingCategories.Models,
            label: "Supervisor effort",
            description: "null means no --effort flag at all: the CLI's own default.",
            restart: RestartKinds.NextSpawn,
            nullable: true);

        Assert.Null(definition.Default_OrNull);
        Assert.Null(definition.Validate_OrNull(null));
        Assert.Null(definition.Validate_OrNull(JsonValue.Create("xhigh")));
        Assert.NotNull(definition.Validate_OrNull(JsonValue.Create("enormous")));
    }

    [Fact]
    public void Create_Int_RefusesOutsideItsRange_AndNamesBothBounds()
    {
        var definition = SettingDefinition_Factory.Create_Int(
            path: "phone.status.intervalMinutes",
            shippedDefault: 30,
            minimum: 5,
            maximum: 120,
            scope: SettingScopes.Machine,
            category: SettingCategories.Phone,
            label: "Periodic status interval",
            description: "Minutes between periodic STATUS messages.",
            restart: RestartKinds.None);

        Assert.Equal(SettingRenderers.Number, definition.Renderer);
        Assert.Null(definition.Validate_OrNull(JsonValue.Create(30)));

        var message = definition.Validate_OrNull(JsonValue.Create(240));

        Assert.NotNull(message);
        Assert.Contains("5", message);
        Assert.Contains("120", message);
    }

    /// <summary>
    /// THE MODEL WORD IS VALIDATED THROUGH THE SPAWN BUILDER'S OWN CHARSET, not a second copy of it
    /// (CLAUDE.md decision 12). The builder throws because a spawn must not happen; the catalogue
    /// returns a message because a renderer must say why it refused — one implementation, two
    /// reactions.
    /// </summary>
    [Fact]
    public void Create_String_WithTheModelValidator_RefusesAWordTheSpawnBuilderWouldRefuse()
    {
        var definition = SettingDefinition_Factory.Create_String(
            path: "models.supervisor",
            shippedDefault: "opus",
            scope: SettingScopes.Orchestration,
            category: SettingCategories.Models,
            label: "Supervisor model",
            description: "The model a supervisor session spawns with.",
            restart: RestartKinds.NextSpawn,
            legacyPath: "supervisorModel",
            validator: SettingValidators.MODEL_WORD);

        Assert.Equal("supervisorModel", definition.LegacyPath_OrNull);
        Assert.Null(definition.Validate_OrNull(JsonValue.Create("claude-fable-5-1")));
        Assert.NotNull(definition.Validate_OrNull(JsonValue.Create("opus; rm -rf /")));
    }

    [Fact]
    public void Create_Composite_NamesItsParser_AndRendersReadOnly()
    {
        var definition = SettingDefinition_Factory.Create_Composite(
            path: "planBackend",
            parserTypeName: "AIOrchestratorCoreLib.Planning.PlanBackend.PlanBackend_Loader",
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Plan backend",
            description: "Which component reads the ledger; hand-edited, read and never written.",
            restart: RestartKinds.Host);

        Assert.Equal(SettingKinds.Composite, definition.Kind);
        Assert.Equal(SettingRenderers.ReadOnly, definition.Renderer);
        Assert.Equal("AIOrchestratorCoreLib.Planning.PlanBackend.PlanBackend_Loader", definition.CompositeParser_OrNull);
    }

    [Fact]
    public void Create_Bool_WithABlankLabel_Throws_NamingThePath()
    {
        var thrown = Assert.Throws<ArgumentException>(() => SettingDefinition_Factory.Create_Bool(
            path: "phone.appMessagesRing",
            shippedDefault: true,
            scope: SettingScopes.Machine,
            category: SettingCategories.Phone,
            label: "   ",
            description: "…",
            restart: RestartKinds.None));

        Assert.Contains("phone.appMessagesRing", thrown.Message);
    }
}
