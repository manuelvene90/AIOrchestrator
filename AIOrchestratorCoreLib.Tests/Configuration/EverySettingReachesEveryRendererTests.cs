using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Configuration.PhoneSettings;
using AIOrchestratorCoreLib.Configuration.PulseSettings;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingEditor;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;
using AIOrchestratorCoreLib.Configuration.SettingsWriting;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.SettingsMenu;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Web;
using Xunit;
using Xunit.Abstractions;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;
using Harness = AIOrchestratorCoreLib.Tests.Bridge.SettingsMenuEngine_Harness;

namespace AIOrchestratorCoreLib.Tests.Configuration;

/// <summary>
/// SIXTY-SIX SETTINGS, THREE RENDERERS, ONE LIST. This is the test the design is for: it walks
/// SettingsCatalog.ALL and asserts that each definition reaches each surface, so a future catalogue entry
/// cannot appear in two renderers and be invisible in the third — the failure mode nobody would notice,
/// because each renderer looks complete on its own. (Sixty-six was the plan's count; the catalogue holds
/// seventy-two at this gate, measured 2026-09-24 — plan 03 registered six rows while this plan ran, the last
/// two being <c>away.afterMinutes</c> and <c>pulse.unchangedFor</c>, and both are named below so their absence
/// could not hide in a count. Nothing here counts: every assertion walks the catalogue.)
///
/// <para>
/// IT COVERS THE WPF RENDERER'S CONTENT AND NOT ITS APPEARANCE, and that limit is the reason
/// SettingsRow_Builder lives in CoreLib rather than in the app project: the test project is net10.0 and
/// cannot reference a net10.0-windows project, so the only WPF thing testable here is the thing the
/// window BINDS to. The appearance is a manual checklist in Task 9's report and it is not pretended
/// otherwise anywhere. The one reach into the app project is a TEXT read of SettingsWindow.xaml, to prove a
/// template exists for every control kind the editor can choose — a kind with no template would be drawn by
/// nothing, silently.
/// </para>
/// <para>
/// WHAT "THE TELEGRAM RENDERER" AND "THE WEB RENDERER" MEAN HERE: <c>SettingsMenu_Builder.Build</c> over the same
/// snapshot the engine reads (the engine only sends what it returns), and <c>SettingsRequest_Handler.Handle</c>'s
/// GET body, which is all the page draws from — the page names no catalogue path of its own
/// (<c>SettingsPageAssetTests</c>). The round-trips and the identical refusal go through each renderer's real
/// entry point: the engine's inbound loop, the handler's PUT, and the window's commit chain (editor → writer →
/// note formatter), which is CoreLib because the window holds no logic.
/// </para>
/// </summary>
public class EverySettingReachesEveryRendererTests : IDisposable
{
    /// <summary>The two rows plan 03 registered last (Tasks 18 and 19), asserted by name in every renderer.</summary>
    static readonly IReadOnlyList<string> NEWEST_PATHS = [PhoneSettings_Json.AWAY_AFTER_MINUTES_PATH, PulseSettings_Json.UNCHANGED_FOR_PATH];

    /// <summary>
    /// A config.json that makes the origins DIFFER row to row — a Phone toggle, the two newest rows, a nullable
    /// Choice stated as null, a free-text list carrying markup characters, and the masked secret — so "the same
    /// origin label" is compared across shipped default, preset and "set here", not across one label 66 times.
    /// </summary>
    const string VARIED_CONFIG =
        "{\"repos\":[],\"phone\":{\"appMessagesRing\":false},\"away\":{\"afterMinutes\":30},\"pulse\":{\"unchangedFor\":false}," +
        "\"effort\":{\"supervisor\":null},\"highRiskPatterns\":[\"rm -rf\",\"<b>&amp;\"],\"web\":{\"token\":\"tok-SENTINEL\"}}";

    /// <summary>A Phone number (5 – 120) the phone takes without a Kernel confirm, so all three write paths are one step.</summary>
    const string NUMBER_PATH = "phone.status.intervalMinutes";

    const string LOOPBACK_HOST = "127.0.0.1:7391";

    readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-every-setting-{Guid.NewGuid():N}");
    readonly ISupervisionPaths _paths;
    readonly ITestOutputHelper _out;

    public EverySettingReachesEveryRendererTests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        File.WriteAllText(_paths.ConfigFile, VARIED_CONFIG);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------------------------------
    // Step 1 — every definition reaches every surface
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// THE WINDOW BINDS ONE ROW PER READING, and a tab per section: every catalogue path is in exactly one
    /// section, that section is its own category's, and the editor picks a control kind for it without throwing.
    /// </summary>
    [Fact]
    public void EveryDefinition_ProducesExactlyOneWpfRow()
    {
        var (readings, _) = Read_Snapshot();
        var sections = SettingsRow_Builder.Build_Sections(readings);
        var rows = sections.SelectMany(section => section.Rows.Select(row => (section.Category, Reading: row))).ToArray();

        foreach (var definition in Catalog.ALL)
        {
            var row = Assert.Single(rows, candidate => candidate.Reading.Definition.Path == definition.Path);

            Assert.Equal(definition.Category, row.Category);
            _ = SettingEditor_Factory.Create_ForReading(row.Reading).Kind;
        }

        Assert.Equal(Catalog.ALL.Count, rows.Length);
        Assert.All(NEWEST_PATHS, path => Assert.Single(rows, candidate => candidate.Reading.Definition.Path == path));

        _out.WriteLine($"catalogue: {Catalog.ALL.Count} rows — "
            + string.Join(", ", sections.Select(section => $"{section.Category} {section.Rows.Count}")));
    }

    /// <summary>
    /// EVERY ROW IS ONE TAP AWAY ON THE PHONE, AND ONLY ONE: each category the Categories view offers, paged to its
    /// end, draws a Setting button for each of its rows exactly once. A row on no page is unreachable from the
    /// phone; a row on two pages is a paging bug that would also hide another row.
    /// </summary>
    [Fact]
    public void EveryDefinition_AppearsOnExactlyOneTelegramMenuPage()
    {
        var (readings, presetName) = Read_Snapshot();
        var categoriesView = SettingsMenu_Builder.Build(SettingsMenuViews.Categories, null, null, 0, null, null, readings, presetName);
        var offeredCategories = categoriesView.Rows.SelectMany(row => row)
            .Select(button => SettingsButton_Data.Resolve_OrNull(SettingsButton_Data.Parse_OrNull(button.Data)!.Value))
            .Where(resolved => resolved?.View == SettingsMenuViews.Category)
            .Select(resolved => resolved!.Value.Category!.Value)
            .ToArray();

        Assert.Equal(
            SettingsRow_Builder.Build_Sections(readings).Where(section => section.Rows.Count > 0).Select(section => section.Category),
            offeredCategories);

        List<int> reached = [];

        foreach (var category in offeredCategories)
        {
            for (var page = 0; ; page++)
            {
                var view = SettingsMenu_Builder.Build(SettingsMenuViews.Category, category, null, page, null, null, readings, presetName);
                var buttons = view.Rows.SelectMany(row => row).Select(button => SettingsButton_Data.Parse_OrNull(button.Data)!.Value).ToArray();

                reached.AddRange(buttons.Where(button => button.View == SettingsMenuViews.Setting && button.Edit == null).Select(button => button.Index!.Value));

                var hasNext = buttons.Any(button => button.View == SettingsMenuViews.Category && button.Page == page + 1);

                if (!hasNext)
                    break;

                Assert.True(page < 50, $"{category}'s pages never end");
            }
        }

        for (var index = 0; index < Catalog.ALL.Count; index++)
            Assert.True(reached.Count(candidate => candidate == index) == 1, $"'{Catalog.ALL[index].Path}' is on {reached.Count(candidate => candidate == index)} menu pages, not one");

        Assert.Equal(Catalog.ALL.Count, reached.Count);

        foreach (var path in NEWEST_PATHS)
        {
            var setting = SettingsMenu_Builder.Build(SettingsMenuViews.Setting, null, Harness.Index_Of(path), 0, null, null, readings, presetName);

            Assert.Contains($"key: {path}", setting.Text, StringComparison.Ordinal);
        }
    }

    /// <summary>The page draws nothing the GET does not carry, so a row absent from the GET is absent from the page.</summary>
    [Fact]
    public void EveryDefinition_AppearsInTheWebGetResponse()
    {
        var rows = Get_WebRows();

        foreach (var definition in Catalog.ALL)
            Assert.Single(rows, row => row["path"]!.GetValue<string>() == definition.Path);

        Assert.Equal(Catalog.ALL.Count, rows.Count);
        Assert.All(NEWEST_PATHS, path => Assert.Single(rows, row => row["path"]!.GetValue<string>() == path));
    }

    /// <summary>
    /// EVERY ROW HAS A RENDERER HINT, AND EVERY RENDERER HAS A DRAWING FOR IT — never a fallback. The catalogue's
    /// hint is a defined <see cref="SettingRenderers"/> value; every control kind the editor chooses from it has a
    /// DataTemplate keyed on it in SettingsWindow.xaml (a missing key leaves the WPF selector returning nothing);
    /// every EDITABLE hint has its own <c>case</c> in the page's renderControl (whose <c>default</c> draws
    /// nothing); and the Telegram Setting view draws every row without reaching its "unhandled" throw.
    /// </summary>
    [Fact]
    public void EveryDefinition_HasARendererHint_AndNoneIsDrawnByAFallback()
    {
        var (readings, presetName) = Read_Snapshot();
        var windowXaml = Read_RepoFile("AIOrchestrator", "SettingsWindow.xaml");
        var page = SettingsPage_Reader.Read_Html();
        var renderControl = Extract_JsFunction(page, "renderControl");

        foreach (var reading in readings)
        {
            var definition = reading.Definition;

            Assert.True(Enum.IsDefined(definition.Renderer), $"'{definition.Path}' carries no renderer hint");

            var kind = SettingEditor_Factory.Create_ForReading(reading).Kind;

            Assert.True(
                windowXaml.Contains($"x:Key=\"{{x:Static core:SettingEditorKinds.{kind}}}\"", StringComparison.Ordinal),
                $"'{definition.Path}' gets the {kind} control, and SettingsWindow.xaml has no template for it");

            if (reading.IsEditable)
            {
                Assert.True(
                    renderControl.Contains($"case '{definition.Renderer}':", StringComparison.Ordinal),
                    $"'{definition.Path}' is a {definition.Renderer} row, and the page's renderControl draws it through its default (nothing)");
            }
            else
            {
                Assert.Equal(SettingRenderers.ReadOnly, definition.Renderer);
                Assert.Equal(SettingEditorKinds.ReadOnly, kind);
            }

            _ = SettingsMenu_Builder.Build(SettingsMenuViews.Setting, null, Harness.Index_Of(definition.Path), 0, null, null, readings, presetName);
        }
    }

    /// <summary>
    /// AND ALL THREE PRINT THE SAME WORDS. One formatter, one origin label, one restart label (decision 12).
    /// A WPF label reading "30 minutes", a Telegram button reading "30 min" and a web cell reading "30" would
    /// each look right in its own file.
    /// </summary>
    [Fact]
    public void AllThreeRenderers_ShowTheSameValueText_ForEveryDefinition()
    {
        var (readings, presetName) = Read_Snapshot();
        var webRows = Get_WebRows();

        foreach (var reading in readings)
        {
            var path = reading.Definition.Path;
            var wpf = SettingEditor_Factory.Create_ForReading(reading).Reading;
            var web = webRows.Single(row => row["path"]!.GetValue<string>() == path);
            var phone = Build_SettingView(reading, readings, presetName);

            Assert.Equal(reading.DisplayValue, wpf.DisplayValue);
            Assert.Equal(reading.DisplayValue, web["displayValue"]!.GetValue<string>());
            Assert.Contains($"Now: {reading.DisplayValue} — ", phone, StringComparison.Ordinal);

            Assert.Equal(reading.RestartLabel, web["restartLabel"]!.GetValue<string>());
            Assert.Contains(reading.RestartLabel, phone, StringComparison.Ordinal);
        }

        // The rows this config.json made interesting, read literally so the comparison above is not three copies of one wrong answer.
        Assert.Equal("30", Find(readings, PhoneSettings_Json.AWAY_AFTER_MINUTES_PATH).DisplayValue);
        Assert.Equal(SettingValue_Formatter.OFF, Find(readings, PulseSettings_Json.UNCHANGED_FOR_PATH).DisplayValue);
        Assert.Equal(SettingsSnapshot_Reader.SECRET_SET, Find(readings, SettingsSnapshot_Reader.MASKED_SECRET_PATH).DisplayValue);

        // The mask holds in every renderer: no value on the wire, and the sentinel nowhere in any of the three.
        Assert.Null(webRows.Single(row => row["path"]!.GetValue<string>() == SettingsSnapshot_Reader.MASKED_SECRET_PATH)["value"]);
        Assert.DoesNotContain("tok-SENTINEL", Get_WebBody(), StringComparison.Ordinal);
        Assert.DoesNotContain("tok-SENTINEL", Build_SettingView(Find(readings, SettingsSnapshot_Reader.MASKED_SECRET_PATH), readings, presetName), StringComparison.Ordinal);
        Assert.Equal("", SettingEditor_Factory.Create_ForReading(Find(readings, SettingsSnapshot_Reader.MASKED_SECRET_PATH)).EditText);
    }

    [Fact]
    public void AllThreeRenderers_ShowTheSameOriginLabel_ForEveryDefinition()
    {
        var (readings, presetName) = Read_Snapshot();
        var webRows = Get_WebRows();

        foreach (var reading in readings)
        {
            var web = webRows.Single(row => row["path"]!.GetValue<string>() == reading.Definition.Path);

            Assert.Equal(reading.OriginLabel, SettingEditor_Factory.Create_ForReading(reading).Reading.OriginLabel);
            Assert.Equal(reading.OriginLabel, web["originLabel"]!.GetValue<string>());
            Assert.Equal(reading.Origin.ToString(), web["origin"]!.GetValue<string>());
            Assert.Contains($" — {reading.OriginLabel}", Build_SettingView(reading, readings, presetName), StringComparison.Ordinal);
        }

        // Three different origins were compared, not one label seventy-two times.
        Assert.Equal(SettingOrigin_Labels.CONFIG_FILE, Find(readings, PhoneSettings_Json.AWAY_AFTER_MINUTES_PATH).OriginLabel);
        Assert.Equal(SettingOrigin_Labels.CONFIG_FILE, Find(readings, PulseSettings_Json.UNCHANGED_FOR_PATH).OriginLabel);
        Assert.Contains(readings, reading => reading.Origin == SettingOrigins.ShippedDefault);
        Assert.Contains(readings, reading => reading.Origin == SettingOrigins.Preset);
        Assert.Equal(Presets_Loader.CLASSIC, presetName);
    }

    /// <summary>
    /// THE PAGE'S OWN WORDS ARE THE DESKTOP'S. The page cannot call C#, so the sentences both of them draw exist
    /// twice — here is where the two copies are held to each other (Task 9's report listed them). Words only one
    /// renderer has a situation for (the page's network failure and token lock, the window's "Unchanged") are not
    /// drift and are not listed.
    /// </summary>
    [Fact]
    public void TheWebPageAndTheDesktop_DrawTheSameSentences()
    {
        var page = SettingsPage_Reader.Read_Html();
        var editors = Read_Snapshot().Readings.Select(SettingEditor_Factory.Create_ForReading).ToArray();

        Assert.Contains($"'{SettingsRow_Builder.READ_ONLY_NOTE}'", page, StringComparison.Ordinal);
        Assert.Contains($"'{SettingsRow_Builder.EMPTY_SECTION_NOTE}'", page, StringComparison.Ordinal);
        Assert.Contains($"'{SettingWriteNote_Formatter.SAVED}'", page, StringComparison.Ordinal);
        Assert.Contains($"(result.message || '{SettingWriteNote_Formatter.RESET}')", page, StringComparison.Ordinal);

        // "preset: classic": the page concatenates the same prefix onto the GET's preset name.
        Assert.Equal("preset: classic", SettingsRow_Builder.Describe_PresetHeader("classic"));
        Assert.Contains("'preset: ' + data.preset", page, StringComparison.Ordinal);

        // "Not applied (RefusedInvalid).": the same shape over the same outcome word.
        Assert.Equal("Not applied (RefusedInvalid).", SettingWriteNote_Formatter.Describe(SettingsWriteOutcomes.RefusedInvalid, null).Text);
        Assert.Contains("('Not applied (' + result.outcome + ').')", page, StringComparison.Ordinal);

        // The range hint: every bounded Number row's C# text, rebuilt the page's way from the GET's minimum and maximum.
        var rangeHint = Extract_JsFunction(page, "rangeHint");

        Assert.Contains("return minimum + ' – ' + maximum;", rangeHint, StringComparison.Ordinal);
        Assert.Contains("return 'at least ' + minimum;", rangeHint, StringComparison.Ordinal);
        Assert.Contains("return 'at most ' + maximum;", rangeHint, StringComparison.Ordinal);

        var bounded = editors.Where(editor => editor.RangeHint_OrNull != null).ToArray();

        Assert.NotEmpty(bounded);

        foreach (var editor in bounded)
            Assert.Equal(Describe_RangeThePagesWay(editor.Reading.Definition), editor.RangeHint_OrNull);
    }

    /// <summary>
    /// AND ALL THREE REFUSE THE SAME THINGS, for the same reason and with the same message — because none of
    /// them decides: Settings_Writer asks the definition (CLAUDE.md decision 21).
    /// </summary>
    [Fact]
    public void EveryReadOnlyDefinition_IsUneditableInAllThree()
    {
        var (readings, presetName) = Read_Snapshot();
        var webRows = Get_WebRows();
        var readOnly = readings.Where(reading => reading.Definition.Renderer == SettingRenderers.ReadOnly).ToArray();
        var configBefore = File.ReadAllBytes(_paths.ConfigFile);

        Assert.NotEmpty(readOnly);

        foreach (var reading in readOnly)
        {
            var path = reading.Definition.Path;

            // WPF: the read-only control, and nothing to reset.
            var editor = SettingEditor_Factory.Create_ForReading(reading);

            Assert.Equal(SettingEditorKinds.ReadOnly, editor.Kind);
            Assert.False(editor.CanReset);

            // Telegram: the sentence and a Back button, nothing that edits.
            var setting = SettingsMenu_Builder.Build(SettingsMenuViews.Setting, null, Harness.Index_Of(path), 0, null, null, readings, presetName);

            Assert.False(SettingsMenu_Builder.Is_EditableOnThePhone(reading));
            Assert.Contains(SettingsRow_Builder.READ_ONLY_NOTE, setting.Text, StringComparison.Ordinal);
            Assert.Equal(SettingsMenu_Builder.BACK, Assert.Single(Assert.Single(setting.Rows)).Label);

            // Web: not editable in the GET, and a PUT and a DELETE are both refused by the writer's own rule.
            Assert.False(webRows.Single(row => row["path"]!.GetValue<string>() == path)["editable"]!.GetValue<bool>());

            var put = Put(new JsonObject { [path] = reading.Value_OrNull?.DeepClone() });
            var delete = Handle("DELETE", $"{SettingsRequest_Handler.SETTINGS_PATH}?{SettingsRequest_Handler.RESET_QUERY_KEY}={Uri.EscapeDataString(path)}", "");

            Assert.Equal(422, put.Status);
            Assert.Equal(nameof(SettingsWriteOutcomes.RefusedReadOnly), Read_SingleResult(put.Body)["outcome"]!.GetValue<string>());
            Assert.Equal(422, delete.Status);
            Assert.Equal(nameof(SettingsWriteOutcomes.RefusedReadOnly), Read_SingleResult(delete.Body)["outcome"]!.GetValue<string>());
        }

        Assert.Equal(configBefore, File.ReadAllBytes(_paths.ConfigFile));
    }

    /// <summary>
    /// ONE WRONG VALUE, THREE DOORS, ONE SENTENCE. The phone half goes through the reply step (ruling P24) — the
    /// typed-value path, where the owner's own text is what gets refused — through the real inbound loop.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnInvalidValue_IsRefusedIdenticallyByTheTelegramTap_TheWebPut_AndTheWindowsCommit()
    {
        const string TYPED = "500";

        var reason = Catalog.Find_OrNull(NUMBER_PATH)!.Validate_OrNull(JsonValue.Create(500L));

        Assert.NotNull(reason);

        // THE WINDOW: the Number box's Apply — the editor parses the text, the writer asks the definition, the note formatter draws the answer.
        var configBefore = File.ReadAllBytes(_paths.ConfigFile);
        var wpfEditor = SettingEditor_Factory.Create_ForReading(Find(Read_Snapshot().Readings, NUMBER_PATH));
        var (wpfOutcome, wpfMessage) = Settings_Writer.Apply(_paths, NUMBER_PATH, wpfEditor.Build_FromText(TYPED), log: null);
        var wpfNote = SettingWriteNote_Formatter.Describe(wpfOutcome, wpfMessage);

        Assert.Equal(SettingsWriteOutcomes.RefusedInvalid, wpfOutcome);
        Assert.True(wpfNote.IsRefusal);
        Assert.Equal(reason, wpfNote.Text);
        Assert.Equal(configBefore, File.ReadAllBytes(_paths.ConfigFile));

        // THE PAGE: its Number box sends a JSON number for a whole number, and draws results[].message.
        var put = Put(new JsonObject { [NUMBER_PATH] = 500 });
        var result = Read_SingleResult(put.Body);

        Assert.Equal(422, put.Status);
        Assert.Equal(nameof(SettingsWriteOutcomes.RefusedInvalid), result["outcome"]!.GetValue<string>());
        Assert.Equal(reason, result["message"]!.GetValue<string>());
        Assert.Equal(configBefore, File.ReadAllBytes(_paths.ConfigFile));

        // THE PHONE: /settings, ✎ Reply on the row, then the owner types the value.
        using var harness = new Harness("gate-refusal");
        var phoneConfigBefore = File.ReadAllBytes(harness.Paths.ConfigFile);
        IReadOnlyList<(long Id, string Text, TelegramSendSounds Sound)> answered = [];

        await harness.Run_WhileAsync(harness.Build_Engine(), async () =>
        {
            await harness.Owner_Types_Async("/settings", threadId: null);
            await harness.Owner_Taps_Async(Harness.Payload(SettingsMenuViews.Setting, NUMBER_PATH, SettingsMenuEdits.Reply, null), harness.Live_MenuMessageId());

            var before = harness.Telegram.Sent_WithIds.Count;

            await harness.Owner_Types_Async(TYPED, threadId: null);
            answered = harness.Sent_Since(before);
        });

        // The refusal is the note line, then the step's "send another value" — the first line is the whole comparison.
        Assert.Contains(answered, sent => sent.Text.Split('\n')[0] == reason);
        Assert.Equal(phoneConfigBefore, File.ReadAllBytes(harness.Paths.ConfigFile));
        Assert.Equal(0, harness.Count_RoutedMessages());
    }

    // ---------------------------------------------------------------------------------------
    // Step 2 — the three round-trips of spec §9
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// THE WINDOW'S NUMBER BOX → the writer → a fresh read off disk shows the value AND "set here". The newest
    /// Phone row is the one written. The inverse: a path the catalogue does not know is refused in the writer's
    /// words and the file does not move.
    /// </summary>
    [Fact]
    public void RoundTrip_TheWindowsCommit_IsReadBackWithItsValueAndItsOrigin()
    {
        var editor = SettingEditor_Factory.Create_ForReading(Find(Read_Snapshot().Readings, PhoneSettings_Json.AWAY_AFTER_MINUTES_PATH));
        var value = editor.Build_FromText("45");

        Assert.False(editor.Is_Unchanged(value));

        var (outcome, message) = Settings_Writer.Apply(_paths, PhoneSettings_Json.AWAY_AFTER_MINUTES_PATH, value, log: null);

        Assert.Equal((SettingWriteNote_Formatter.SAVED, false), SettingWriteNote_Formatter.Describe(outcome, message));

        var reread = Find(Read_Snapshot().Readings, PhoneSettings_Json.AWAY_AFTER_MINUTES_PATH);

        Assert.Equal(45, reread.Value_OrNull!.GetValue<long>());
        Assert.Equal(SettingOrigins.ConfigFile, reread.Origin);
        Assert.Equal(SettingOrigin_Labels.CONFIG_FILE, reread.OriginLabel);

        var configBefore = File.ReadAllBytes(_paths.ConfigFile);
        var (unknownOutcome, unknownMessage) = Settings_Writer.Apply(_paths, "phone.noSuchSetting", JsonValue.Create(1), log: null);

        Assert.Equal(SettingsWriteOutcomes.RefusedUnknownPath, unknownOutcome);
        Assert.Equal(unknownMessage, SettingWriteNote_Formatter.Describe(unknownOutcome, unknownMessage).Text);
        Assert.Equal(configBefore, File.ReadAllBytes(_paths.ConfigFile));
    }

    /// <summary>
    /// A /settings TAP → the writer → a fresh read off disk. A Phone toggle, so no Kernel confirm stands between
    /// the tap and the write. The inverse: the same payload with a check that names no row is a stale menu —
    /// answered, and nothing written.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task RoundTrip_ASettingsTap_IsReadBackWithItsValueAndItsOrigin()
    {
        const string TOGGLE_PATH = "phone.appMessagesRing";

        using var harness = new Harness("gate-tap");
        var tap = Harness.Payload(SettingsMenuViews.Setting, TOGGLE_PATH, SettingsMenuEdits.Set, SettingValue_Formatter.OFF);
        var staleTap = Replace_Check(Harness.Payload(SettingsMenuViews.Setting, PulseSettings_Json.UNCHANGED_FOR_PATH, SettingsMenuEdits.Set, SettingValue_Formatter.OFF));
        byte[] afterTheTap = [];

        await harness.Run_WhileAsync(harness.Build_Engine(), async () =>
        {
            await harness.Owner_Types_Async("/settings", threadId: null);
            await harness.Owner_Taps_Async(tap, harness.Live_MenuMessageId());

            afterTheTap = File.ReadAllBytes(harness.Paths.ConfigFile);

            await harness.Owner_Taps_Async(staleTap, harness.Live_MenuMessageId());
        });

        var reread = Find(SettingsSnapshot_Reader.Read_All_FromDisk(harness.Paths, session: null, log: null).Readings, TOGGLE_PATH);

        Assert.False(reread.Value_OrNull!.GetValue<bool>());
        Assert.Equal(SettingOrigins.ConfigFile, reread.Origin);
        Assert.Equal(SettingOrigin_Labels.CONFIG_FILE, reread.OriginLabel);

        Assert.Equal(afterTheTap, File.ReadAllBytes(harness.Paths.ConfigFile));
        Assert.Null(harness.Read_ConfigValue_OrNull(PulseSettings_Json.UNCHANGED_FOR_PATH));
    }

    /// <summary>
    /// A PUT → the writer → a fresh read off disk, for the newest Pulse row. The inverse: an unknown path in the
    /// same body shape is a 422 carrying the writer's message, and the file does not move.
    /// </summary>
    [Fact]
    public void RoundTrip_AWebPut_IsReadBackWithItsValueAndItsOrigin()
    {
        File.WriteAllText(_paths.ConfigFile, "{\"repos\":[]}");

        var before = Find(Read_Snapshot().Readings, PulseSettings_Json.UNCHANGED_FOR_PATH);

        Assert.NotEqual(SettingOrigins.ConfigFile, before.Origin);

        var put = Put(new JsonObject { [PulseSettings_Json.UNCHANGED_FOR_PATH] = false });

        Assert.Equal(200, put.Status);
        Assert.Equal(nameof(SettingsWriteOutcomes.Applied), Read_SingleResult(put.Body)["outcome"]!.GetValue<string>());

        var reread = Find(Read_Snapshot().Readings, PulseSettings_Json.UNCHANGED_FOR_PATH);

        Assert.False(reread.Value_OrNull!.GetValue<bool>());
        Assert.Equal(SettingOrigins.ConfigFile, reread.Origin);
        Assert.Equal(SettingOrigin_Labels.CONFIG_FILE, reread.OriginLabel);

        var configBefore = File.ReadAllBytes(_paths.ConfigFile);
        var unknown = Put(new JsonObject { ["pulse.noSuchSetting"] = true });
        var (_, unknownMessage) = Settings_Writer.Apply(_paths, "pulse.noSuchSetting", JsonValue.Create(true), log: null);

        Assert.Equal(422, unknown.Status);
        Assert.Equal(nameof(SettingsWriteOutcomes.RefusedUnknownPath), Read_SingleResult(unknown.Body)["outcome"]!.GetValue<string>());
        Assert.Equal(unknownMessage, Read_SingleResult(unknown.Body)["message"]!.GetValue<string>());
        Assert.Equal(configBefore, File.ReadAllBytes(_paths.ConfigFile));
    }

    /// <summary>
    /// NO RENDERER READS THE RAW TREES (ruling P30). The web host resolves the page token over the raw trees
    /// because it must compare it; a renderer that did the same would bypass the mask that keeps web.token off
    /// every screen. The renderers' own files are scanned for both doors to the unmasked value.
    /// </summary>
    [Fact]
    public void NoRenderer_ReadsTheUnmaskedTrees()
    {
        string[] renderers =
        [
            Read_RepoFile("AIOrchestratorCoreLib", "Telegram", "SettingsMenu", "SettingsMenu_Builder.cs"),
            Read_RepoFile("AIOrchestratorCoreLib", "Bridge", "SettingsMenu", "SettingsMenuModel.cs"),
            Read_RepoFile("AIOrchestratorCoreLib", "Web", "SettingsRequest_Handler.cs"),
            Read_RepoFile("AIOrchestratorCoreLib", "Configuration", "SettingsPresentation", "SettingEditor", "SettingEditor_Factory.cs"),
            Read_RepoFile("AIOrchestrator", "SettingsWindow.xaml.cs"),
        ];

        foreach (var source in renderers)
        {
            var code = Strip_Comments(source);

            Assert.DoesNotContain("Read_Trees_FromDisk", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Settings_Resolver.Resolve", code, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    (IReadOnlyList<ISettingReading> Readings, string PresetName) Read_Snapshot()
    {
        return SettingsSnapshot_Reader.Read_All_FromDisk(_paths, session: null, log: null);
    }

    static ISettingReading Find(IReadOnlyList<ISettingReading> readings, string path)
    {
        return readings.Single(reading => reading.Definition.Path == path);
    }

    static string Build_SettingView(ISettingReading reading, IReadOnlyList<ISettingReading> readings, string presetName)
    {
        return SettingsMenu_Builder.Build(SettingsMenuViews.Setting, null, Harness.Index_Of(reading.Definition.Path), 0, null, null, readings, presetName).Text;
    }

    (int Status, string ContentType, string Body) Handle(string method, string target, string body)
    {
        return SettingsRequest_Handler.Handle(
            method,
            target,
            name => name == SettingsRequest_Handler.HOST_HEADER ? LOOPBACK_HOST : null,
            body,
            _paths,
            configuredToken: "",
            log: null);
    }

    (int Status, string ContentType, string Body) Put(JsonObject edits)
    {
        return Handle("PUT", SettingsRequest_Handler.SETTINGS_PATH, edits.ToJsonString());
    }

    string Get_WebBody()
    {
        var (status, _, body) = Handle("GET", SettingsRequest_Handler.SETTINGS_PATH, "");

        Assert.Equal(200, status);

        return body;
    }

    IReadOnlyList<JsonObject> Get_WebRows()
    {
        var sections = JsonNode.Parse(Get_WebBody())!["sections"]!.AsArray();

        return [.. sections.SelectMany(section => section!["rows"]!.AsArray()).Select(row => row!.AsObject())];
    }

    static JsonObject Read_SingleResult(string body)
    {
        return Assert.Single(JsonNode.Parse(body)!["results"]!.AsArray())!.AsObject();
    }

    /// <summary>The page's rangeHint, in C#: the GET's minimum and maximum, joined exactly as the script joins them.</summary>
    static string Describe_RangeThePagesWay(ISettingDefinition definition)
    {
        if (definition.Minimum != null && definition.Maximum != null)
            return $"{definition.Minimum} – {definition.Maximum}";

        return definition.Minimum != null ? $"at least {definition.Minimum}" : $"at most {definition.Maximum}";
    }

    /// <summary>A payload whose row check names no row — what a menu drawn before a catalogue change carries.</summary>
    static string Replace_Check(string payload)
    {
        var fields = payload.Split(':');
        var check = fields[4];

        fields[4] = check == "0000" ? "ffff" : "0000";

        return string.Join(':', fields);
    }

    /// <summary>One JavaScript function's body, from its declaration to the next top-level function — refusing to run if it is not there.</summary>
    static string Extract_JsFunction(string page, string name)
    {
        var start = page.IndexOf($"function {name}(", StringComparison.Ordinal);

        Assert.True(start >= 0, $"settings.html has no function {name} — this check can prove nothing about a function it cannot find");

        var end = page.IndexOf("\nfunction ", start + 1, StringComparison.Ordinal);

        return end < 0 ? page[start..] : page[start..end];
    }

    static string Strip_Comments(string source)
    {
        return Regex.Replace(source, @"^\s*//.*$", "", RegexOptions.Multiline);
    }

    /// <summary>A source file of this checkout, found by walking up from the test binary — refusing to run if it is not found (decision 20).</summary>
    static string Read_RepoFile(params string[] relative)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            var candidate = Path.Combine([folder.FullName, .. relative]);

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        Assert.Fail($"{Path.Combine(relative)} was not found walking up from {AppContext.BaseDirectory}");

        return "";
    }
}
