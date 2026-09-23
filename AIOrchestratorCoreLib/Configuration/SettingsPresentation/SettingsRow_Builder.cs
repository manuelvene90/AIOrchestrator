using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation;

/// <summary>
/// THE SECTIONS EVERY RENDERER SHOWS, one per category, each with its title and its rows. The WPF window
/// binds a tab to each, the Telegram menu pages over them, and the web GET serialises them — so a section
/// built three times could come out in three orders under three names.
///
/// <para>
/// IT LIVES IN CoreLib, NOT IN THE APP, SO THE WINDOW'S CONTENT IS TESTABLE. The test project targets
/// <c>net10.0</c> and cannot reference the WPF app, and there is no UI harness in this repo: anything the
/// window decided for itself — which tabs, in which order, which rows under which — would be decided where
/// no test can reach. Built here, it is pinned by <c>SettingsRowBuilderTests</c>, and the window's XAML is
/// left with layout and binding, which is all the UI-relaxed project is allowed to hold.
/// </para>
/// <para>
/// ORDER (ruling P26): categories in the order they first appear in <c>SettingsCatalog.ALL</c> — the
/// catalogue's own "renderer order", Models, Kernel, Phone, Receipts, Pulse, Owner — then any category the
/// catalogue has no row in, in <see cref="SettingCategories"/>' own order. That is Kit today, empty until
/// plan 05, and EVERY category is always a section even with nothing in it: a renderer paging over sections
/// must not have to know which categories happen to be populated this release, and a partial list of
/// readings (an orchestration's own rows, D3) keeps the same section layout as the whole snapshot.
/// </para>
/// </summary>
public static class SettingsRow_Builder
{
    static readonly IReadOnlyList<SettingCategories> SECTION_ORDER = Build_SectionOrder();

    public static IReadOnlyList<(SettingCategories Category, string Title, IReadOnlyList<ISettingReading> Rows)> Build_Sections(
        IReadOnlyList<ISettingReading> readings)
    {
        List<(SettingCategories Category, string Title, IReadOnlyList<ISettingReading> Rows)> sections = [];

        foreach (var category in SECTION_ORDER)
        {
            var rows = readings.Where(reading => reading.Definition.Category == category).ToArray();

            sections.Add((category, Describe_Title(category), rows));
        }

        return sections;
    }

    /// <summary>The heading a renderer shows for a category — a tab, a menu page, a table caption.</summary>
    public static string Describe_Title(SettingCategories category)
    {
        return category switch
        {
            SettingCategories.Models => "Models and effort",
            SettingCategories.Kernel => "Kernel",
            SettingCategories.Phone => "Phone",
            SettingCategories.Receipts => "Receipts",
            SettingCategories.Pulse => "Pulse",
            SettingCategories.Owner => "Owner",
            SettingCategories.Kit => "Kit",
            _ => throw new InvalidOperationException($"Unhandled SettingCategories: {category}"),
        };
    }

    static IReadOnlyList<SettingCategories> Build_SectionOrder()
    {
        List<SettingCategories> order = [];

        foreach (var definition in Catalog.ALL)
        {
            if (!order.Contains(definition.Category))
                order.Add(definition.Category);
        }

        foreach (var category in Enum.GetValues<SettingCategories>())
        {
            if (!order.Contains(category))
                order.Add(category);
        }

        return order;
    }
}
