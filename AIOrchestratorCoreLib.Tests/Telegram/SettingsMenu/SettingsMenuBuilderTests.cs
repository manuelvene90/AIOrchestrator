using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.SettingsMenu;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram.SettingsMenu;

/// <summary>
/// THE /settings MENU, PROVEN WITHOUT A BOT. Every view is a static function of the readings, so what the
/// phone shows — the text, the captions, the payload behind each — is asserted here, and Task 5 only has to
/// send it.
/// </summary>
public class SettingsMenuBuilderTests
{
    static JsonObject Tree(string json) => (JsonObject)JsonNode.Parse(json)!;

    static JsonObject Classic => Presets_Loader.Load_Embedded(Presets_Loader.CLASSIC);

    static IReadOnlyList<ISettingReading> Readings(JsonObject? config = null)
    {
        return SettingsSnapshot_Reader.Read_All(config, Classic, Presets_Loader.CLASSIC, session: null);
    }

    /// <summary>A reading that is missing throws (decision 20) — a null here would make the assertion after it measure nothing.</summary>
    static ISettingReading Reading(IReadOnlyList<ISettingReading> readings, string path)
    {
        return readings.SingleOrDefault(reading => reading.Definition.Path == path)
            ?? throw new InvalidOperationException($"no reading for '{path}' — the test measures nothing");
    }

    static int Index_Of(string path)
    {
        for (var index = 0; index < SettingsCatalog.ALL.Count; index++)
        {
            if (SettingsCatalog.ALL[index].Path == path)
                return index;
        }

        throw new InvalidOperationException($"'{path}' is not a catalogue row — the test measures nothing");
    }

    static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) View(
        SettingsMenuViews view, string path, IReadOnlyList<ISettingReading> readings, int page = 0, SettingsMenuEdits? edit = null, string? word = null)
    {
        return SettingsMenu_Builder.Build(view, category: null, Index_Of(path), page, edit, word, readings, Presets_Loader.CLASSIC);
    }

    static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) Category(SettingCategories category, int page, IReadOnlyList<ISettingReading> readings)
    {
        return SettingsMenu_Builder.Build(SettingsMenuViews.Category, category, settingIndex: null, page, edit: null, word: null, readings, Presets_Loader.CLASSIC);
    }

    static IEnumerable<(string Data, string Label)> Buttons(IReadOnlyList<IReadOnlyList<(string Data, string Label)>> rows) => rows.SelectMany(row => row);

    /// <summary>Every payload on a keyboard, parsed AND resolved — a button this build cannot read back is a defect, not a stale menu.</summary>
    static IReadOnlyList<(SettingsMenuViews View, SettingCategories? Category, int? Index, int Page, SettingsMenuEdits? Edit, string? Word, string Label)> Taps(
        IReadOnlyList<IReadOnlyList<(string Data, string Label)>> rows)
    {
        return Buttons(rows).Select(button =>
        {
            var parsed = SettingsButton_Data.Parse_OrNull(button.Data) ?? throw new InvalidOperationException($"'{button.Data}' does not parse");
            var resolved = SettingsButton_Data.Resolve_OrNull(parsed) ?? throw new InvalidOperationException($"'{button.Data}' resolves as stale");

            return (resolved.View, resolved.Category, resolved.Index, resolved.Page, resolved.Edit, resolved.Word, button.Label);
        }).ToArray();
    }

    /// <summary>Every tap on every page of a category — the page count computed from its section, never a literal.</summary>
    static IReadOnlyList<(SettingsMenuViews View, SettingCategories? Category, int? Index, int Page, SettingsMenuEdits? Edit, string? Word, string Label)> All_CategoryTaps(
        SettingCategories category, IReadOnlyList<ISettingReading> readings)
    {
        var count = Sections(readings).Single(section => section.Category == category).Rows.Count;
        List<(SettingsMenuViews View, SettingCategories? Category, int? Index, int Page, SettingsMenuEdits? Edit, string? Word, string Label)> taps = [];

        for (var page = 0; page * SettingsMenu_Builder.ROWS_PER_PAGE < count; page++)
            taps.AddRange(Taps(Category(category, page, readings).Rows));

        return taps;
    }

    static IReadOnlyList<(SettingCategories Category, string Title, IReadOnlyList<ISettingReading> Rows)> Sections(IReadOnlyList<ISettingReading> readings)
    {
        return SettingsRow_Builder.Build_Sections(readings);
    }

    [Fact]
    public void TheCategoriesView_ListsEveryCategoryThatHasRows_WithItsCount()
    {
        var readings = Readings();
        var (_, rows) = SettingsMenu_Builder.Build(SettingsMenuViews.Categories, null, null, 0, null, null, readings, Presets_Loader.CLASSIC);
        var taps = Taps(rows);

        var populated = Sections(readings).Where(section => section.Rows.Count > 0).ToArray();

        Assert.Equal(populated.Select(section => (SettingCategories?)section.Category), taps.Select(tap => tap.Category));
        Assert.All(taps, tap => Assert.Equal(SettingsMenuViews.Category, tap.View));

        foreach (var section in populated)
        {
            var tap = taps.Single(candidate => candidate.Category == section.Category);

            Assert.Contains(section.Title, tap.Label);
            Assert.Contains(section.Rows.Count.ToString(), tap.Label);
        }
    }

    /// <summary>And the day plan 05 fills it, it is: the list is computed from the sections, not written by hand.</summary>
    [Fact]
    public void TheKitCategory_IsNotOffered_BecauseItIsEmpty()
    {
        var readings = Readings();

        Assert.Empty(Sections(readings).Single(section => section.Category == SettingCategories.Kit).Rows);

        var (_, rows) = SettingsMenu_Builder.Build(SettingsMenuViews.Categories, null, null, 0, null, null, readings, Presets_Loader.CLASSIC);

        Assert.DoesNotContain(Taps(rows), tap => tap.Category == SettingCategories.Kit);
    }

    [Fact]
    public void AnEmptyCategory_ReachedAnyway_SaysSo_AndOffersBack()
    {
        var (text, rows) = Category(SettingCategories.Kit, 0, Readings());

        Assert.Contains(SettingsRow_Builder.EMPTY_SECTION_NOTE, text);
        Assert.Contains(Taps(rows), tap => tap.View == SettingsMenuViews.Categories);
    }

    [Fact]
    public void ACategoryView_ShowsEachSettingsLabelAndItsCurrentValue()
    {
        var readings = Readings(Tree("""{"phone":{"receipts":"reactions"}}"""));
        var receipts = Sections(readings).Single(section => section.Category == SettingCategories.Receipts);
        var (text, rows) = Category(SettingCategories.Receipts, 0, readings);
        var taps = Taps(rows);

        foreach (var reading in receipts.Rows)
        {
            Assert.Contains($"{reading.Definition.Label}: {reading.DisplayValue}", text);

            var tap = taps.Single(candidate => candidate.Index == Index_Of(reading.Definition.Path));

            Assert.Equal(SettingsMenuViews.Setting, tap.View);
            Assert.Contains(reading.Definition.Label, tap.Label);
            Assert.Contains(reading.DisplayValue, tap.Label);
        }

        Assert.Contains("reactions", text);
    }

    /// <summary>The same words the other two renderers use — the reading's own label, never a word of the menu's.</summary>
    [Fact]
    public void ACategoryView_ShowsTheOriginBesideTheValue()
    {
        var readings = Readings(Tree("""{"phone":{"receipts":"reactions"}}"""));
        var (text, _) = Category(SettingCategories.Receipts, 0, readings);
        var receipts = Reading(readings, "phone.receipts");

        Assert.Equal(SettingOrigin_Labels.CONFIG_FILE, receipts.OriginLabel);
        Assert.Contains($"{receipts.Definition.Label}: {receipts.DisplayValue} — {receipts.OriginLabel}", text);
    }

    /// <summary>
    /// KERNEL IS THIRTY-SEVEN ROWS AND THE OWNER ASKED WHETHER THIS WAS EVEN POSSIBLE. It is, by paging: no
    /// view ever emits more than ROWS_PER_PAGE setting buttons plus one navigation row, so the tallest
    /// keyboard is a fixed size no matter how the catalogue grows.
    /// </summary>
    [Fact]
    public void TheBiggestCategory_IsPaged_AndNoPageExceedsTheRowLimit()
    {
        var readings = Readings();
        var biggest = Sections(readings).OrderByDescending(section => section.Rows.Count).First();
        var pages = (biggest.Rows.Count + SettingsMenu_Builder.ROWS_PER_PAGE - 1) / SettingsMenu_Builder.ROWS_PER_PAGE;

        Assert.True(pages > 1, $"{biggest.Title} has {biggest.Rows.Count} rows — the paging is not exercised");

        for (var page = 0; page < pages; page++)
        {
            var (_, rows) = Category(biggest.Category, page, readings);
            var settingButtons = Taps(rows).Count(tap => tap.View == SettingsMenuViews.Setting);

            Assert.InRange(settingButtons, 1, SettingsMenu_Builder.ROWS_PER_PAGE);
            Assert.True(rows.Count <= SettingsMenu_Builder.ROWS_PER_PAGE + 1, $"page {page} has {rows.Count} rows");
        }
    }

    [Fact]
    public void EverySettingInACategory_AppearsOnExactlyOnePage()
    {
        var readings = Readings();

        foreach (var section in Sections(readings).Where(section => section.Rows.Count > 0))
        {
            var pages = (section.Rows.Count + SettingsMenu_Builder.ROWS_PER_PAGE - 1) / SettingsMenu_Builder.ROWS_PER_PAGE;
            List<int> seen = [];

            for (var page = 0; page < pages; page++)
                seen.AddRange(Taps(Category(section.Category, page, readings).Rows).Where(tap => tap.View == SettingsMenuViews.Setting).Select(tap => tap.Index!.Value));

            Assert.Equal(section.Rows.Select(reading => Index_Of(reading.Definition.Path)), seen);
        }
    }

    [Fact]
    public void ThePageRow_OffersPreviousAndNext_OnlyWhereThereIsOne()
    {
        var readings = Readings();
        var kernel = Sections(readings).Single(section => section.Category == SettingCategories.Kernel);
        var last = (kernel.Rows.Count - 1) / SettingsMenu_Builder.ROWS_PER_PAGE;

        Assert.True(last >= 2, "Kernel needs three pages for this test to see a middle one");

        for (var page = 0; page <= last; page++)
        {
            var pageTaps = Taps(Category(SettingCategories.Kernel, page, readings).Rows).Where(tap => tap.View == SettingsMenuViews.Category).ToArray();

            Assert.Equal(page > 0, pageTaps.Any(tap => tap.Page == page - 1));
            Assert.Equal(page < last, pageTaps.Any(tap => tap.Page == page + 1));
        }

        // A single-page category has no page row buttons at all.
        var owner = Taps(Category(SettingCategories.Owner, 0, readings).Rows);

        Assert.DoesNotContain(owner, tap => tap.View == SettingsMenuViews.Category);
    }

    /// <summary>A page beyond the end (a stale menu, a catalogue that shrank) draws the last page rather than an empty one.</summary>
    [Fact]
    public void APageBeyondTheEnd_DrawsTheLastPage()
    {
        var readings = Readings();

        var first = Category(SettingCategories.Owner, 0, readings);
        var beyond = Category(SettingCategories.Owner, 99, readings);

        Assert.Equal(first.Text, beyond.Text);
        Assert.Equal(Buttons(first.Rows), Buttons(beyond.Rows));
        Assert.NotEmpty(Buttons(first.Rows));
    }

    [Fact]
    public void EveryViewExceptCategories_OffersBack()
    {
        var readings = Readings();

        Assert.Contains(Taps(Category(SettingCategories.Phone, 0, readings).Rows), tap => tap.View == SettingsMenuViews.Categories);

        var index = Index_Of("phone.receipts");
        var setting = Taps(View(SettingsMenuViews.Setting, "phone.receipts", readings).Rows);
        var back = setting.Single(tap => tap.View == SettingsMenuViews.Category);

        Assert.Equal(SettingCategories.Receipts, back.Category);
        Assert.Equal(Sections(readings).Single(section => section.Category == SettingCategories.Receipts).Rows
            .Select(reading => reading.Definition.Path).ToList().IndexOf("phone.receipts") / SettingsMenu_Builder.ROWS_PER_PAGE, back.Page);

        Assert.Contains(Taps(View(SettingsMenuViews.Values, "phone.receipts", readings).Rows),
            tap => tap.View == SettingsMenuViews.Setting && tap.Index == index && tap.Edit == null);

        Assert.Contains(Taps(View(SettingsMenuViews.Confirm, "telegramStatusScreenshots", readings, edit: SettingsMenuEdits.Set, word: "on").Rows),
            tap => tap.View == SettingsMenuViews.Setting && tap.Edit == null);
    }

    [Fact]
    public void AnEditableSettingView_OffersReset()
    {
        var readings = Readings(Tree("""{"phone":{"receipts":"reactions"}}"""));
        var taps = Taps(View(SettingsMenuViews.Setting, "phone.receipts", readings).Rows);

        var reset = taps.Single(tap => tap.Edit == SettingsMenuEdits.Reset);

        Assert.Equal(SettingsMenuViews.Setting, reset.View);
        Assert.Equal(Index_Of("phone.receipts"), reset.Index);
    }

    /// <summary>Reset follows the WPF editor's one rule: only config.json has a key to delete — a preset's value has none.</summary>
    [Fact]
    public void ASettingNotSetHere_OffersNoReset_BecauseThereIsNoKeyToDelete()
    {
        var taps = Taps(View(SettingsMenuViews.Setting, "phone.receipts", Readings()).Rows);

        Assert.DoesNotContain(taps, tap => tap.Edit == SettingsMenuEdits.Reset);
    }

    [Fact]
    public void AReadOnlySettingView_OffersNeitherAValueNorReset_AndSaysWhereItIsChanged()
    {
        var readings = Readings();

        foreach (var path in new[] { "repos", "planBackend", "session.paused" })
        {
            var reading = Reading(readings, path);
            var (text, rows) = View(SettingsMenuViews.Setting, path, readings);
            var taps = Taps(rows);

            Assert.False(reading.IsEditable);
            Assert.All(taps, tap => Assert.Null(tap.Edit));
            Assert.DoesNotContain(taps, tap => tap.View is SettingsMenuViews.Values or SettingsMenuViews.Confirm);
            Assert.Contains(SettingsRow_Builder.READ_ONLY_NOTE, text);
            Assert.Contains(reading.Definition.Description, text);
        }
    }

    [Fact]
    public void ASettingView_ShowsTheValueTheOriginAndWhenAChangeTakesEffect()
    {
        var readings = Readings();
        var reading = Reading(readings, "pulse.stepMinutes");
        var (text, _) = View(SettingsMenuViews.Setting, "pulse.stepMinutes", readings);

        Assert.Contains(reading.Definition.Label, text);
        Assert.Contains(reading.Definition.Path, text);
        Assert.Contains($"{reading.DisplayValue} — {reading.OriginLabel}", text);
        Assert.Contains(reading.RestartLabel, text);
    }

    /// <summary>A TOGGLE FLIPS IN ONE TAP; A CHOICE OPENS ITS WORDS (spec §8.2).</summary>
    [Fact]
    public void ABoolSetting_IsOneButtonThatCarriesTheOppositeValue()
    {
        foreach (var (config, expected) in new[] { ("""{"phone":{"appMessagesRing":true}}""", SettingValue_Formatter.OFF), ("""{"phone":{"appMessagesRing":false}}""", SettingValue_Formatter.ON) })
        {
            var taps = Taps(View(SettingsMenuViews.Setting, "phone.appMessagesRing", Readings(Tree(config))).Rows);
            var sets = taps.Where(tap => tap.Edit == SettingsMenuEdits.Set).ToArray();

            var flip = Assert.Single(sets);

            Assert.Equal(expected, flip.Word);
            Assert.Equal(SettingsMenuViews.Setting, flip.View);
            Assert.True(SettingsButton_Data.Writes_OnTap(flip.View, flip.Edit));
        }
    }

    [Fact]
    public void AnEnumSetting_OpensAValuesViewWithOneButtonPerWord_AndMarksTheCurrentOne()
    {
        var readings = Readings(Tree("""{"phone":{"receipts":"reactions"}}"""));
        var reading = Reading(readings, "phone.receipts");

        var setting = Taps(View(SettingsMenuViews.Setting, "phone.receipts", readings).Rows);

        Assert.Contains(setting, tap => tap.View == SettingsMenuViews.Values && tap.Edit == null);
        Assert.DoesNotContain(setting, tap => tap.Edit == SettingsMenuEdits.Set);

        var values = Taps(View(SettingsMenuViews.Values, "phone.receipts", readings).Rows).Where(tap => tap.Edit == SettingsMenuEdits.Set).ToArray();

        Assert.Equal(reading.OfferedValues, values.Select(tap => tap.Word));
        Assert.Single(values, tap => tap.Label.StartsWith(SettingsMenu_Builder.CURRENT_MARK, StringComparison.Ordinal));
        Assert.StartsWith(SettingsMenu_Builder.CURRENT_MARK, values.Single(tap => tap.Word == "reactions").Label);
        Assert.All(values, tap => Assert.Equal(SettingsMenuViews.Setting, tap.View));
    }

    [Fact]
    public void ANullableEnum_AlsoOffersTheNullMeaning_AsItsOwnButton()
    {
        var readings = Readings();
        var values = Taps(View(SettingsMenuViews.Values, "effort.reviewer", readings).Rows).Where(tap => tap.Edit == SettingsMenuEdits.Set).ToArray();

        Assert.Contains(values, tap => tap.Word == SettingValue_Formatter.NOT_SET);

        foreach (var level in EffortLevels.ALL)
            Assert.Contains(values, tap => tap.Word == level);
    }

    [Fact]
    public void ANumberOrTextOrListSetting_OffersTheReplyStep_NotAValueButton()
    {
        var readings = Readings();

        foreach (var path in new[] { "pulse.stepMinutes", "owner.name", "pulse.fields", "highRiskPatterns", "web.token" })
        {
            var taps = Taps(View(SettingsMenuViews.Setting, path, readings).Rows);

            Assert.Contains(taps, tap => tap.Edit == SettingsMenuEdits.Reply && tap.View == SettingsMenuViews.Setting);
            Assert.DoesNotContain(taps, tap => tap.Edit is SettingsMenuEdits.Set or SettingsMenuEdits.Add or SettingsMenuEdits.Remove);
            Assert.False(SettingsButton_Data.Writes_OnTap(SettingsMenuViews.Setting, SettingsMenuEdits.Reply));
        }
    }

    /// <summary>
    /// OFFERS ARE CANDIDATES (ruling P39): a picker tap adds or removes ONE word of the current list, never
    /// writes the offers as a list. And "progress" is both a pulse.fields word and a pulse.buttons verb, so the
    /// view names WHICH list the words are for.
    /// </summary>
    [Fact]
    public void APickerList_OffersOneWordAtATime_AddingOrRemovingFromTheCurrentList_AndNamesItsList()
    {
        var readings = Readings(Tree("""{"pulse":{"fields":["progress"],"buttons":["show"]}}"""));

        foreach (var path in new[] { "pulse.fields", "pulse.buttons" })
        {
            var reading = Reading(readings, path);
            var (text, rows) = View(SettingsMenuViews.Values, path, readings);
            var taps = Taps(rows);
            var words = taps.Where(tap => tap.Edit is SettingsMenuEdits.Add or SettingsMenuEdits.Remove).ToArray();

            Assert.Contains(reading.Definition.Label, text);
            Assert.Contains(reading.Definition.Path, text);
            Assert.All(words, tap => Assert.Equal(SettingsMenuViews.Values, tap.View));
            Assert.All(words, tap => Assert.Contains(tap.Word!, tap.Label));
            Assert.DoesNotContain(taps, tap => tap.Edit == SettingsMenuEdits.Set);

            var present = ((JsonArray)reading.Value_OrNull!).Select(element => element!.GetValue<string>()).ToHashSet();

            foreach (var tap in words)
                Assert.Equal(present.Contains(tap.Word!) ? SettingsMenuEdits.Remove : SettingsMenuEdits.Add, tap.Edit);
        }

        var fieldsProgress = Taps(View(SettingsMenuViews.Values, "pulse.fields", readings).Rows).Single(tap => tap.Word == "progress");

        Assert.Equal(SettingsMenuEdits.Remove, fieldsProgress.Edit);
    }

    [Fact]
    public void AValuesView_IsPaged_WhenItsOffersExceedOnePage()
    {
        var readings = Readings();
        var biggest = readings.OrderByDescending(reading => reading.OfferedValues.Count).First();
        var perPage = SettingsMenu_Builder.ROWS_PER_PAGE * SettingsMenu_Builder.VALUES_PER_ROW;
        var pages = (biggest.OfferedValues.Count + perPage - 1) / perPage;
        List<string> seen = [];

        for (var page = 0; page < pages; page++)
        {
            var rows = View(SettingsMenuViews.Values, biggest.Definition.Path, readings, page).Rows;

            Assert.True(rows.Count <= SettingsMenu_Builder.ROWS_PER_PAGE + 1, $"values page {page} of {biggest.Definition.Path} has {rows.Count} rows");
            Assert.All(rows, row => Assert.True(row.Count <= SettingsMenu_Builder.VALUES_PER_ROW + 1));

            seen.AddRange(Taps(rows).Where(tap => tap.Word != null).Select(tap => tap.Word!));
        }

        Assert.Equal(biggest.OfferedValues, seen);
    }

    /// <summary>THE FENCED KEYS (D2). A refusal that says why is a decision; a greyed button is a mystery.</summary>
    [Fact]
    public void TelegramInbound_IsShownAndRefused_WithALineSayingItIsHowThisMenuReachesYou()
    {
        var readings = Readings();
        var reading = Reading(readings, "telegramInbound");

        Assert.Contains(All_CategoryTaps(SettingCategories.Kernel, readings), tap => tap.Index == Index_Of("telegramInbound"));

        Assert.True(reading.IsEditable);
        Assert.False(SettingsMenu_Builder.Is_EditableOnThePhone(reading));

        foreach (var view in new[] { SettingsMenuViews.Setting, SettingsMenuViews.Values, SettingsMenuViews.Confirm })
        {
            var (text, rows) = View(view, "telegramInbound", readings, edit: view == SettingsMenuViews.Confirm ? SettingsMenuEdits.Set : null, word: view == SettingsMenuViews.Confirm ? "off" : null);

            Assert.Contains(SettingsMenu_Builder.PHONE_FENCE_NOTE, text);
            Assert.Contains("how this menu reaches you", text);
            Assert.All(Taps(rows), tap => Assert.Null(tap.Edit));
        }
    }

    [Fact]
    public void TheTwoTelegramIds_AreShownAndRefused_ForTheSameReason()
    {
        var readings = Readings();

        foreach (var path in new[] { "telegramSupergroupChatId", "telegramOwnerUserId" })
        {
            var (text, rows) = View(SettingsMenuViews.Setting, path, readings);

            Assert.False(SettingsMenu_Builder.Is_EditableOnThePhone(Reading(readings, path)));
            Assert.Contains(SettingsMenu_Builder.PHONE_FENCE_NOTE, text);
            Assert.All(Taps(rows), tap => Assert.Null(tap.Edit));
        }

        Assert.Equal(["telegramInbound", "telegramSupergroupChatId", "telegramOwnerUserId"], SettingsMenu_Builder.PHONE_FENCED_PATHS);
        Assert.All(SettingsMenu_Builder.PHONE_FENCED_PATHS, path => Assert.NotNull(SettingsCatalog.Find_OrNull(path)));
    }

    /// <summary>
    /// THE REST OF KERNEL TAKES A SECOND TAP (D2 (a), ruling P6). The toggle's first tap lands on a Confirm
    /// view, and a Confirm view never writes; only its Yes does.
    /// </summary>
    [Fact]
    public void AKernelEdit_OpensAConfirmView_AndTheFirstTapWritesNothing()
    {
        var readings = Readings();
        var flip = Taps(View(SettingsMenuViews.Setting, "telegramStatusScreenshots", readings).Rows).Single(tap => tap.Edit == SettingsMenuEdits.Set);

        Assert.Equal(SettingsMenuViews.Confirm, flip.View);
        Assert.False(SettingsButton_Data.Writes_OnTap(flip.View, flip.Edit));

        var (text, rows) = View(SettingsMenuViews.Confirm, "telegramStatusScreenshots", readings, edit: flip.Edit, word: flip.Word);
        var yes = Taps(rows).Single(tap => tap.Edit != null);

        Assert.Equal(SettingsMenuViews.Setting, yes.View);
        Assert.Equal(SettingsMenuEdits.Set, yes.Edit);
        Assert.Equal(flip.Word, yes.Word);
        Assert.True(SettingsButton_Data.Writes_OnTap(yes.View, yes.Edit));
        Assert.Contains(Reading(readings, "telegramStatusScreenshots").DisplayValue, text);
        Assert.Contains(flip.Word!, text);

        // A Kernel Choice goes through Confirm too, and so does a Kernel Reset.
        var runner = Taps(View(SettingsMenuViews.Values, "runners.implementer.runner", readings).Rows).Where(tap => tap.Edit == SettingsMenuEdits.Set).ToArray();

        Assert.NotEmpty(runner);
        Assert.All(runner, tap => Assert.Equal(SettingsMenuViews.Confirm, tap.View));

        var reset = Taps(View(SettingsMenuViews.Setting, "buttonExpiryMinutes", Readings(Tree("""{"buttonExpiryMinutes":60}"""))).Rows).Single(tap => tap.Edit == SettingsMenuEdits.Reset);

        Assert.Equal(SettingsMenuViews.Confirm, reset.View);

        // And outside Kernel the same toggle writes on its first tap.
        var phone = Taps(View(SettingsMenuViews.Setting, "phone.appMessagesRing", readings).Rows).Single(tap => tap.Edit == SettingsMenuEdits.Set);

        Assert.Equal(SettingsMenuViews.Setting, phone.View);
    }

    /// <summary>A reply-step value is HELD by Task 5; the confirm's Yes carries only the id (ruling P6), and the view shows what was typed.</summary>
    [Fact]
    public void AKernelReplyConfirm_ShowsTheHeldValue_AndItsYesCarriesOnlyTheId()
    {
        var readings = Readings();
        var (text, rows) = View(SettingsMenuViews.Confirm, "buttonExpiryMinutes", readings, edit: SettingsMenuEdits.ApplyHeldReply, word: "90");
        var yes = Taps(rows).Single(tap => tap.Edit != null);

        Assert.Contains("90", text);
        Assert.Equal(SettingsMenuEdits.ApplyHeldReply, yes.Edit);
        Assert.Null(yes.Word);
        Assert.Equal(Index_Of("buttonExpiryMinutes"), yes.Index);
    }

    /// <summary>
    /// A TYPED SECRET IS NEVER DRAWN BACK (fix round 1 of 7763a6b, ruling P40). The Kernel Confirm of a held
    /// web.token reply used to read "Change it to &lt;the token&gt;?" — a second, BOT-sent copy in the chat history,
    /// which the owner cannot delete, while the reply prompt had just promised "the value is never shown back".
    /// A sentinel that appears nowhere — text, caption or payload — is the only proof that holds.
    /// </summary>
    [Fact]
    public void ASecretReplyConfirm_ShowsTheTypedValueNowhere()
    {
        const string sentinel = "SENTINEL-7f3a-token";
        var (text, rows) = View(SettingsMenuViews.Confirm, SettingsSnapshot_Reader.MASKED_SECRET_PATH, Readings(), edit: SettingsMenuEdits.ApplyHeldReply, word: sentinel);

        Assert.DoesNotContain(sentinel, text);
        Assert.All(Buttons(rows), button => Assert.DoesNotContain(sentinel, button.Label));
        Assert.All(Buttons(rows), button => Assert.DoesNotContain(sentinel, button.Data));

        var yes = Taps(rows).Single(tap => tap.Edit != null);

        Assert.Equal(SettingsMenuEdits.ApplyHeldReply, yes.Edit);
        Assert.Null(yes.Word);
    }

    [Fact]
    public void TheReplyPrompt_NamesTheSettingItsValueAndSlashCancel()
    {
        var readings = Readings();
        var number = Reading(readings, "pulse.stepMinutes");
        var prompt = SettingsMenu_Builder.Build_ReplyPrompt(number, expiryMinutes: 5);

        Assert.Contains(number.Definition.Label, prompt);
        Assert.Contains(number.DisplayValue, prompt);
        Assert.Contains("/cancel", prompt);
        Assert.Contains("5 minutes", prompt);

        // The picker's words are named as candidates, and the list it replaces is named by its label.
        var fields = Reading(readings, "pulse.fields");
        var fieldsPrompt = SettingsMenu_Builder.Build_ReplyPrompt(fields, expiryMinutes: 5);

        Assert.Contains(fields.Definition.Label, fieldsPrompt);
        Assert.All(fields.OfferedValues, word => Assert.Contains(word, fieldsPrompt));

        // Kernel says a confirm follows.
        Assert.Contains(SettingsMenu_Builder.KERNEL_CONFIRM_NOTE, SettingsMenu_Builder.Build_ReplyPrompt(Reading(readings, "buttonExpiryMinutes"), 5));
    }

    [Fact]
    public void TheHeader_NamesTheActivePreset()
    {
        var readings = Readings();
        var header = SettingsRow_Builder.Describe_PresetHeader(Presets_Loader.CLASSIC);

        Assert.Contains(header, SettingsMenu_Builder.Build(SettingsMenuViews.Categories, null, null, 0, null, null, readings, Presets_Loader.CLASSIC).Text);
        Assert.Contains(header, Category(SettingCategories.Phone, 0, readings).Text);
        Assert.Contains(header, View(SettingsMenuViews.Setting, "phone.receipts", readings).Text);
        Assert.Contains("preset: quiet", SettingsMenu_Builder.Build(SettingsMenuViews.Categories, null, null, 0, null, null, readings, Presets_Loader.QUIET).Text);
    }

    [Fact]
    public void TheTextStaysUnderTelegramsMessageLimit_ForTheBiggestCategory()
    {
        var readings = Readings();

        foreach (var section in Sections(readings).Where(section => section.Rows.Count > 0))
        {
            for (var page = 0; page * SettingsMenu_Builder.ROWS_PER_PAGE < section.Rows.Count; page++)
                Assert.True(Category(section.Category, page, readings).Text.Length <= TelegramMessage_Chunker.TELEGRAM_MAX_MESSAGE_LENGTH);
        }

        // Every Setting view too — the descriptions are the longest text the menu shows.
        foreach (var definition in SettingsCatalog.ALL)
        {
            var text = View(SettingsMenuViews.Setting, definition.Path, readings).Text;

            Assert.True(text.Length <= TelegramMessage_Chunker.TELEGRAM_MAX_MESSAGE_LENGTH, $"{definition.Path}: {text.Length}");
        }
    }

    /// <summary>
    /// D3: an orchestration topic's /settings is READ-ONLY, shows that orchestration's own rows (the session.*
    /// state and the two dials), and points at the commands that DO change them.
    /// </summary>
    [Fact]
    public void TheOrchestrationView_IsReadOnly_AndNamesModelAndEffort()
    {
        var session = OrchestrationSession_Factory.Create(
            "arb-fix", "Arb Studio", @"C:\repos\arb", new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc),
            telegramTopicId: null, supervisorPid: null, supervisorSpawnedUtc: null, communicatorSpawnedUtc: null,
            displayName: null, supervisorModelOverride: "sonnet", implementerModelOverride: null,
            members: [], telegramMode: TelegramDeliveryModes.Normal, closedUtc: null, paused: true);

        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session);
        var (text, rows) = SettingsMenu_Builder.Build(SettingsMenuViews.Orchestration, null, null, 0, null, null, readings, Presets_Loader.CLASSIC);

        Assert.Empty(rows);
        Assert.Contains("/model", text);
        Assert.Contains("/effort", text);

        foreach (var reading in readings.Where(reading => reading.Definition.Scope == SettingScopes.Orchestration))
            Assert.Contains($"{reading.Definition.Label}: {reading.DisplayValue} — {reading.OriginLabel}", text);

        Assert.Contains("sonnet", text);
        Assert.Contains(SettingOrigin_Labels.SESSION, text);
        Assert.DoesNotContain(Reading(readings, "telegramInbound").Definition.Label, text);
    }

    /// <summary>Every button the machine menu can draw, on every view, parses and resolves — and fits.</summary>
    [Fact]
    public void EveryButtonOfEveryView_ResolvesInThisBuild()
    {
        var readings = Readings(Tree("""{"pulse":{"fields":["progress"]},"buttonExpiryMinutes":60,"phone":{"receipts":"reactions"}}"""));

        Assert.NotEmpty(Taps(SettingsMenu_Builder.Build(SettingsMenuViews.Categories, null, null, 0, null, null, readings, Presets_Loader.CLASSIC).Rows));

        foreach (var section in Sections(readings))
            Taps(Category(section.Category, 0, readings).Rows);

        foreach (var definition in SettingsCatalog.ALL)
        {
            Taps(View(SettingsMenuViews.Setting, definition.Path, readings).Rows);
            Taps(View(SettingsMenuViews.Values, definition.Path, readings).Rows);
            Taps(View(SettingsMenuViews.Confirm, definition.Path, readings, edit: SettingsMenuEdits.Reset).Rows);
        }
    }
}
