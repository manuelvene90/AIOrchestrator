using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsPresentation;

/// <summary>
/// THE WPF WINDOW'S CONTENT, TESTED WHERE IT CAN BE. The test project cannot reference the WPF app, so the
/// sections the window binds to — and the Telegram menu pages over, and the web GET serialises — are built
/// here, in CoreLib, and pinned here. A section that went missing in the window would be missing from all
/// three, and this file is the only place that can see it.
/// </summary>
public class SettingsRowBuilderTests
{
    static IReadOnlyList<ISettingReading> Readings()
    {
        return SettingsSnapshot_Reader.Read_All(configTree: null, Presets_Loader.Load_Embedded(Presets_Loader.CLASSIC), Presets_Loader.CLASSIC, session: null);
    }

    /// <summary>
    /// CATALOGUE ORDER, THEN THE EMPTY ONES (ruling P26). SettingCategories' enum order (Phone, Pulse, …) is
    /// not the order the catalogue lists its rows in, and the catalogue's is the one its own class doc calls
    /// "renderer order". Kit has no row to take a place from, so it goes last. Spelled out rather than
    /// computed, because computing it would be the builder asserting its own algorithm.
    /// </summary>
    [Fact]
    public void EveryCategory_BecomesOneSection_InCatalogueOrder()
    {
        var sections = SettingsRow_Builder.Build_Sections(Readings());

        Assert.Equal(
            new[]
            {
                SettingCategories.Models, SettingCategories.Kernel, SettingCategories.Phone, SettingCategories.Receipts,
                SettingCategories.Pulse, SettingCategories.Owner, SettingCategories.Kit,
            },
            sections.Select(section => section.Category));
        Assert.Equal(Enum.GetValues<SettingCategories>().Order(), sections.Select(section => section.Category).Order());
        Assert.All(sections, section => Assert.False(string.IsNullOrWhiteSpace(section.Title)));
        Assert.Equal(sections.Count, sections.Select(section => section.Title).Distinct().Count());
    }

    /// <summary>Plan 05 fills Kit; until then every renderer must survive a section with nothing in it.</summary>
    [Fact]
    public void TheKitCategory_ProducesAnEmptySection_AndDoesNotThrow()
    {
        var sections = SettingsRow_Builder.Build_Sections(Readings());

        var kit = Assert.Single(sections, section => section.Category == SettingCategories.Kit);

        Assert.Empty(kit.Rows);
        Assert.Equal("Kit", kit.Title);
    }

    [Fact]
    public void EverySection_CarriesOnlyItsOwnCategorysRows_InCatalogueOrder()
    {
        var sections = SettingsRow_Builder.Build_Sections(Readings());

        foreach (var section in sections)
        {
            Assert.All(section.Rows, row => Assert.Equal(section.Category, row.Definition.Category));
            Assert.Equal(
                Catalog.In_Category(section.Category).Select(definition => definition.Path),
                section.Rows.Select(row => row.Definition.Path));
        }
    }

    [Fact]
    public void TheSectionsCoverEveryReading_WithNoneDuplicatedAndNoneLost()
    {
        var readings = Readings();

        var sections = SettingsRow_Builder.Build_Sections(readings);
        var rows = sections.SelectMany(section => section.Rows).ToArray();

        Assert.Equal(readings.Count, rows.Length);
        Assert.Equal(readings.Count, rows.Distinct().Count());
        Assert.All(readings, reading => Assert.Contains(reading, rows));
    }

    /// <summary>
    /// A PARTIAL LIST STILL GETS EVERY SECTION — D3's orchestration view hands over only its own rows, and a
    /// renderer paging over sections must not have to guess which ones exist today.
    /// </summary>
    [Fact]
    public void ASubsetOfReadings_StillGetsEverySection_WithTheOthersEmpty()
    {
        var subset = Readings().Where(reading => reading.Definition.Scope == SettingScopes.Orchestration).ToArray();

        var sections = SettingsRow_Builder.Build_Sections(subset);

        Assert.Equal(Enum.GetValues<SettingCategories>().Length, sections.Count);
        Assert.Equal(subset, sections.SelectMany(section => section.Rows).OrderBy(row => Array.IndexOf(subset, row)));
        Assert.Empty(sections.Single(section => section.Category == SettingCategories.Phone).Rows);
    }

    /// <summary>
    /// THE CONNECTION TAB'S TWO IDS ARE CATALOGUE ROWS (D11): each path is a real Kernel Int row, the selection
    /// returns exactly their readings in order, and they stay in the Kernel section too — the Connection tab
    /// draws them a second time, it does not take them away from where the catalogue files them.
    /// </summary>
    [Fact]
    public void TheConnectionRows_AreTheTwoTelegramIds_DrawnFromTheSameReadings()
    {
        var readings = Readings();

        foreach (var path in SettingsRow_Builder.CONNECTION_PATHS)
        {
            var definition = Catalog.Find_OrNull(path);

            Assert.NotNull(definition);
            Assert.Equal(SettingCategories.Kernel, definition!.Category);
            Assert.Equal(SettingKinds.Int, definition.Kind);
        }

        var connection = SettingsRow_Builder.Select_ConnectionRows(readings);
        var kernel = SettingsRow_Builder.Build_Sections(readings).Single(section => section.Category == SettingCategories.Kernel).Rows;

        Assert.Equal(new[] { "telegramSupergroupChatId", "telegramOwnerUserId" }, connection.Select(reading => reading.Definition.Path));
        Assert.All(connection, reading => Assert.Contains(reading, readings));
        Assert.All(connection, reading => Assert.Contains(reading, kernel));
    }

    /// <summary>
    /// THE KIT SECTION IS EMPTY AND SAYS SO (checklist item 2): it is always a section, it has no rows until plan
    /// 05, and the window draws <see cref="SettingsRow_Builder.EMPTY_SECTION_NOTE"/> in it rather than a blank tab.
    /// </summary>
    [Fact]
    public void TheKitSection_IsEmpty_AndTheEmptySectionNoteIsWords()
    {
        var kit = SettingsRow_Builder.Build_Sections(Readings()).Single(section => section.Category == SettingCategories.Kit);

        Assert.Empty(Catalog.In_Category(SettingCategories.Kit));
        Assert.Empty(kit.Rows);
        Assert.Equal("Kit", kit.Title);
        Assert.False(string.IsNullOrWhiteSpace(SettingsRow_Builder.EMPTY_SECTION_NOTE));
    }

    /// <summary>D13: the preset is named in the header in the web page's own words, never offered as a row.</summary>
    [Fact]
    public void ThePresetHeader_NamesThePreset()
    {
        Assert.Equal("preset: classic", SettingsRow_Builder.Describe_PresetHeader(Presets_Loader.CLASSIC));
        Assert.Equal("preset: quiet", SettingsRow_Builder.Describe_PresetHeader(Presets_Loader.QUIET));
    }

    /// <summary>Checklist item 3: every tab holds exactly its category's catalogue rows — counted from the catalogue, never a literal.</summary>
    [Fact]
    public void EverySection_HoldsExactlyItsCategorysCatalogueRows()
    {
        foreach (var (category, _, rows) in SettingsRow_Builder.Build_Sections(Readings()))
            Assert.Equal(Catalog.In_Category(category).Select(definition => definition.Path), rows.Select(row => row.Definition.Path));
    }
}
