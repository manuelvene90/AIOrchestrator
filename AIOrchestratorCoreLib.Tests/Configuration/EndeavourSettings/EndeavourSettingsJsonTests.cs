using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.EndeavourSettings;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.EndeavourSettings;

/// <summary>
/// THE SIBLING CAP IS DATA (owner decision O2, 2026-09-23): <c>endeavour.maxOpenSiblings</c>, default 3,
/// resolved catalogue → preset → config.json. Read through the LOADER with real JSON on disk, the
/// fixture <c>PerRoleModelDefaultsTests</c> uses, because the loader is the only reader that has both
/// trees — a test of the parser alone would not prove the block is wired.
/// </summary>
public class EndeavourSettingsJsonTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public EndeavourSettingsJsonTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-endeavour-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void WithNoConfigAtAll_TheCapIsThree()
    {
        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal(3, config.Endeavour.MaxOpenSiblings);
    }

    [Fact]
    public void AValueInConfigJson_IsRead()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"endeavour":{"maxOpenSiblings":2}}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal(2, config.Endeavour.MaxOpenSiblings);
    }

    /// <summary>
    /// The loader runs every tick with no try/catch above it, so a hand-typed 0 or 11 must cost the key
    /// its default and never the load. The refusal is the catalogue row's own range, applied by the
    /// resolver per layer — not a second clamp restated here.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void AnOutOfRangeValue_FallsToTheDefault_AndDoesNotThrow(int stated)
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"endeavour":{"maxOpenSiblings":""" + stated + "}}");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal(3, config.Endeavour.MaxOpenSiblings);
    }

    [Fact]
    public void AWrongType_FallsToTheDefault_AndDoesNotThrow()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"endeavour":{"maxOpenSiblings":"three"}}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal(3, config.Endeavour.MaxOpenSiblings);
    }

    /// <summary>
    /// A COPYING FACTORY THAT FORGETS THE BLOCK RESETS IT. OrchestratorConfig_Factory.Create_WithStatusScreenshots
    /// rebuilds a whole config from a source. If it does not pass Endeavour through, toggling
    /// screenshots silently puts the cap back to the default.
    /// </summary>
    [Fact]
    public void CreateWithStatusScreenshots_KeepsTheEndeavourBlock()
    {
        var original = OrchestratorConfig_Factory.Create(
            [RepoEntry_Factory.Create("Arb Studio", @"C:\repos\arb")],
            "opus", "opus", null, null, "sonnet", "sonnet",
            null, null, null,
            telegramStatusScreenshots: false,
            null, null,
            endeavour: EndeavourSettings_Factory.Create(5));

        var flipped = OrchestratorConfig_Factory.Create_WithStatusScreenshots(original, true);

        Assert.Equal(5, flipped.Endeavour.MaxOpenSiblings);
    }

    /// <summary>
    /// The row's shape is the O2 answer plus the pre-flight placement ruling: Machine, Kernel, no restart
    /// (the executor reads the provider per request), and at the END of the flat Kernel rows — just
    /// before the session-state rows — rather than beside the guardrail rows plan 03's high-risk toggle
    /// may touch. /settings payloads index <see cref="Catalog.ALL"/>, so where a row lands decides which
    /// open menus go stale.
    /// </summary>
    [Fact]
    public void TheCatalogueRow_HasTheO2Shape_AndSitsBeforeTheSessionStateRows()
    {
        var row = Catalog.Find_OrNull(Catalog.ENDEAVOUR_MAX_OPEN_SIBLINGS_PATH);

        Assert.NotNull(row);
        Assert.Equal("endeavour.maxOpenSiblings", row.Path);
        Assert.Equal(SettingKinds.Int, row.Kind);
        Assert.Equal(3, row.Default_OrNull!.GetValue<int>());
        Assert.Equal(1, row.Minimum);
        Assert.Equal(10, row.Maximum);
        Assert.Equal(SettingScopes.Machine, row.Scope);
        Assert.Equal(SettingCategories.Kernel, row.Category);
        Assert.Equal(RestartKinds.None, row.Restart);

        var kernel = Catalog.In_Category(SettingCategories.Kernel);
        var index = kernel.ToList().IndexOf(row);
        var next = kernel[index + 1];

        Assert.Equal(SettingScopes.Orchestration, next.Scope);
    }

    /// <summary>
    /// A cap below one is not a cap, it is a refusal of the requester itself — the count includes it.
    /// An invariant violation, so the factory throws naming the value; the JSON path never reaches it
    /// because the catalogue's range refuses the value first.
    /// </summary>
    [Fact]
    public void TheFactory_RefusesACapBelowOne()
    {
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => EndeavourSettings_Factory.Create(0));

        Assert.Contains("0", thrown.Message);
    }
}
