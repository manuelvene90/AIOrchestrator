using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingEditor;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsPresentation.SettingEditor;

/// <summary>
/// THE WPF WINDOW'S DECISIONS, PINNED WHERE A TEST CAN REACH THEM (plan 04 Task 9). The window is
/// <c>net10.0-windows</c> and this project cannot reference it, so every choice it would make — which control
/// a row gets, what a box holds, which value an edit writes — is made by <c>SettingEditor_Factory</c> and
/// asserted here. The window's XAML binds these members and forwards clicks; if a test below goes red, the
/// window is wrong in the same way, and nothing else would say so.
/// </summary>
public class SettingEditorTests
{
    static JsonObject Classic => Presets_Loader.Load_Embedded(Presets_Loader.CLASSIC);

    static JsonObject Tree(string json) => (JsonObject)JsonNode.Parse(json)!;

    /// <summary>A missing reading throws (decision 20): a null here would make the assertions after it measure nothing.</summary>
    static ISettingReading Reading(string path, string? configJson = null)
    {
        return SettingsSnapshot_Reader.Read_One_OrNull(path, configJson == null ? null : Tree(configJson), Classic, Presets_Loader.CLASSIC, session: null)
            ?? throw new InvalidOperationException($"no reading for '{path}' — the test measures nothing");
    }

    static ISettingEditor Editor(string path, string? configJson = null)
    {
        return SettingEditor_Factory.Create_ForReading(Reading(path, configJson));
    }

    static string[] Words(JsonNode? node)
    {
        return ((JsonArray)node!).Select(element => element!.GetValue<string>()).ToArray();
    }

    /// <summary>
    /// EVERY ROW GETS A CONTROL, AND THE CONTROL FOLLOWS THE RENDERER — with the one split P5 makes: an
    /// OrderedList is a picker when it offers words and a typed list when it offers none. A row the writer
    /// would refuse is never drawn as a control (P4). Walked over the whole catalogue, so a row plan 03 adds
    /// is covered the day it lands.
    /// </summary>
    [Fact]
    public void EveryCatalogueRow_GetsTheControlItsRendererNames()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);

        foreach (var reading in readings)
        {
            var kind = SettingEditor_Factory.Create_ForReading(reading).Kind;
            var expected = reading.Definition.Renderer switch
            {
                SettingRenderers.Toggle => SettingEditorKinds.Toggle,
                SettingRenderers.Choice => SettingEditorKinds.Choice,
                SettingRenderers.Number => SettingEditorKinds.Number,
                SettingRenderers.Text => SettingEditorKinds.Text,
                SettingRenderers.OrderedList when reading.OfferedValues.Count > 0 => SettingEditorKinds.WordPicker,
                SettingRenderers.OrderedList => SettingEditorKinds.FreeTextList,
                _ => SettingEditorKinds.ReadOnly,
            };

            Assert.True(expected == kind, $"'{reading.Definition.Path}' drew {kind}, expected {expected}");
            Assert.Equal(!reading.IsEditable, kind == SettingEditorKinds.ReadOnly);
        }
    }

    /// <summary>
    /// P5 BY NAME: the three lists with known words are pickers (D15 — a typo is unreachable), and
    /// <c>highRiskPatterns</c> is a free-text list the owner types into. Named, because the walk above would
    /// stay green if the reading layer stopped offering words to all four.
    /// </summary>
    [Fact]
    public void TheFourLists_AreThreePickersAndOneTypedList()
    {
        Assert.Equal(SettingEditorKinds.WordPicker, Editor("pulse.fields").Kind);
        Assert.Equal(SettingEditorKinds.WordPicker, Editor("pulse.buttons").Kind);
        Assert.Equal(SettingEditorKinds.WordPicker, Editor("general.buttons").Kind);
        Assert.Equal(SettingEditorKinds.FreeTextList, Editor("highRiskPatterns").Kind);
    }

    [Fact]
    public void TheReadOnlyRows_DrawNoControl_AndOfferNoReset_EvenWhenConfigJsonHoldsThem()
    {
        var repos = Editor("repos", """{"repos":[{"name":"Arb Studio","path":"C:\\repos\\arb"}]}""");

        Assert.Equal(SettingEditorKinds.ReadOnly, repos.Kind);
        Assert.Equal(SettingOrigins.ConfigFile, repos.Reading.Origin);
        Assert.False(repos.CanReset);
        Assert.Empty(repos.Items);
        Assert.Equal(string.Empty, repos.EditText);

        foreach (var path in new[] { "planBackend", "session.paused", "session.telegramMode", "session.ownerPresence" })
            Assert.Equal(SettingEditorKinds.ReadOnly, Editor(path).Kind);
    }

    /// <summary>
    /// THE BOX HOLDS THE VALUE, NOT ITS READING: "not set" in an id box would be sent back by Apply as a word,
    /// and the definition would refuse the owner for a word they never typed.
    /// </summary>
    [Fact]
    public void ANullableNumberWithNothingSet_HoldsAnEmptyBox_NotTheWordsNotSet()
    {
        var chatId = Editor("telegramSupergroupChatId");

        Assert.Equal(SettingEditorKinds.Number, chatId.Kind);
        Assert.Equal(SettingValue_Formatter.NOT_SET, chatId.Reading.DisplayValue);
        Assert.Equal(string.Empty, chatId.EditText);
        Assert.Null(chatId.RangeHint_OrNull);
    }

    [Fact]
    public void ANumberRow_HoldsItsInvariantNumber_AndShowsTheWebPagesRangeWords()
    {
        var expiry = Editor("highRiskCodeExpiryMinutes", """{"highRiskCodeExpiryMinutes": 25}""");

        Assert.Equal("25", expiry.EditText);
        Assert.Equal("1 – 1440", expiry.RangeHint_OrNull);

        var chatId = Editor("telegramSupergroupChatId", """{"telegramSupergroupChatId": -1001234567890}""");

        Assert.Equal("-1001234567890", chatId.EditText);
    }

    /// <summary>Every Number row with a bound shows one, and a Text row never does — the hint is for drawing only.</summary>
    [Fact]
    public void ARangeHint_IsShownExactlyWhenANumberRowHasABound()
    {
        foreach (var definition in Catalog.ALL)
        {
            var editor = Editor(definition.Path);
            var hasBound = definition.Minimum != null || definition.Maximum != null;

            Assert.True(
                (editor.Kind == SettingEditorKinds.Number && hasBound) == (editor.RangeHint_OrNull != null),
                $"'{definition.Path}' ({editor.Kind}) range hint: '{editor.RangeHint_OrNull}'");
        }
    }

    /// <summary>
    /// TYPED TEXT GOES THROUGH THE ONE PARSER (P13): an id becomes a long the definition accepts, and a typo stays
    /// raw so the DEFINITION refuses it in its own words — the editor never refuses anything itself.
    /// </summary>
    [Fact]
    public void TypedText_IsTheParsersValue_AndAWrongWordReachesTheDefinitionRaw()
    {
        var chatId = Editor("telegramSupergroupChatId");

        var typed = chatId.Build_FromText(" -1001234567890 ");
        Assert.Equal(-1001234567890L, typed!.GetValue<long>());
        Assert.Null(chatId.Reading.Definition.Validate_OrNull(typed));

        var typo = chatId.Build_FromText("12a");
        Assert.Equal("12a", typo!.GetValue<string>());
        Assert.NotNull(chatId.Reading.Definition.Validate_OrNull(typo));

        // Blank on a nullable row is the stated "nothing" (P3), never a Reset.
        Assert.Null(chatId.Build_FromText("   "));
    }

    [Fact]
    public void AToggle_ReadsItsValue_AndWritesTheParsersBool()
    {
        var off = Editor("telegramStatusScreenshots", """{"telegramStatusScreenshots": false}""");
        var on = Editor("telegramStatusScreenshots", """{"telegramStatusScreenshots": true}""");

        Assert.Equal(SettingEditorKinds.Toggle, on.Kind);
        Assert.True(on.IsOn);
        Assert.Equal(SettingValue_Formatter.ON, on.CurrentOffer_OrNull);
        Assert.False(off.IsOn);
        Assert.Equal(SettingValue_Formatter.OFF, off.CurrentOffer_OrNull);

        Assert.True(off.Build_FromToggle(true)!.GetValue<bool>());
        Assert.False(on.Build_FromToggle(false)!.GetValue<bool>());
    }

    /// <summary>
    /// A CHOICE SELECTS ITS CURRENT WORD, AND A NULLABLE ONE OFFERS THE NULL MEANING (checklist item 7): picking
    /// "not set" writes JSON null — a stated nothing that beats classic's xhigh (P3) — never a Reset.
    /// </summary>
    [Fact]
    public void AChoice_SelectsItsCurrentWord_AndItsNotSetOfferWritesNull()
    {
        var effort = Editor("effort.supervisor");

        Assert.Equal(SettingEditorKinds.Choice, effort.Kind);
        Assert.Equal(effort.Reading.DisplayValue, effort.CurrentOffer_OrNull);
        Assert.Contains(SettingValue_Formatter.NOT_SET, effort.Reading.OfferedValues);
        Assert.Equal(effort.Reading.Definition.EnumValues.Append(SettingValue_Formatter.NOT_SET).ToHashSet(), effort.Reading.OfferedValues.ToHashSet());

        Assert.Null(effort.Build_FromText(SettingValue_Formatter.NOT_SET));

        foreach (var word in effort.Reading.Definition.EnumValues)
            Assert.Equal(word, effort.Build_FromText(word)!.GetValue<string>());
    }

    /// <summary>A Reset needs a key to delete: only when config.json is the layer that answered, as on the web page.</summary>
    [Fact]
    public void Reset_IsOfferedOnlyWhenConfigJsonAnswered()
    {
        Assert.False(Editor("highRiskCodeExpiryMinutes").CanReset);
        Assert.True(Editor("highRiskCodeExpiryMinutes", """{"highRiskCodeExpiryMinutes": 25}""").CanReset);
    }

    /// <summary>
    /// THE BLANK WORD STAYS BLANK (the Task 1 carry): the list reads <c>""</c> for an empty element, and every
    /// edit is made on the ELEMENTS, so moving or keeping it writes the empty string back — never the two-quote
    /// caption as a literal word the guardrail would then match.
    /// </summary>
    [Fact]
    public void AFreeTextList_EditsItsElements_NotTheirCaptions_SoABlankWordStaysBlank()
    {
        var patterns = Editor("highRiskPatterns", """{"highRiskPatterns": ["push", "", "deploy"]}""");

        Assert.Equal(new[] { "push", SettingValue_Formatter.BLANK_WORD, "deploy" }, patterns.Items.Select(item => item.Caption));
        Assert.Equal(new[] { false, true, true }, patterns.Items.Select(item => item.CanMoveUp));
        Assert.Equal(new[] { true, true, false }, patterns.Items.Select(item => item.CanMoveDown));

        Assert.Equal(new[] { "", "push", "deploy" }, Words(patterns.Build_Moved(1, -1)));
        Assert.Equal(new[] { "push", "deploy", "" }, Words(patterns.Build_Moved(1, 1)));
        Assert.Equal(new[] { "push", "deploy" }, Words(patterns.Build_Removed(1)));
        Assert.Equal(new[] { "", "deploy" }, Words(patterns.Build_Removed(0)));
    }

    /// <summary>Typed words are the parser's list (commas separate, blanks drop, P13); a box holding no word adds nothing and writes nothing.</summary>
    [Fact]
    public void AddingToAFreeTextList_AppendsTheParsedWords_AndNothingTypedIsNoEdit()
    {
        var patterns = Editor("highRiskPatterns", """{"highRiskPatterns": ["push"]}""");

        Assert.Equal(new[] { "push", "drop table", "rm -rf" }, Words(patterns.Build_Added_OrNull(" drop table , rm -rf,")));
        Assert.Null(patterns.Build_Added_OrNull("   "));
        Assert.Null(patterns.Build_Added_OrNull(","));

        var empty = Editor("highRiskPatterns", """{"highRiskPatterns": []}""");
        Assert.Empty(empty.Items);
        Assert.Equal(new[] { "deploy" }, Words(empty.Build_Added_OrNull("deploy")));
    }

    /// <summary>A picker offers only what is not already in the list, and what it adds is a word the definition accepts.</summary>
    [Fact]
    public void APicker_OffersOnlyTheWordsNotInTheList_AndAddsAWordTheDefinitionAccepts()
    {
        var offers = Editor("pulse.fields").Reading.OfferedValues;
        var first = offers[0];
        var fields = Editor("pulse.fields", $$$"""{"pulse": {"fields": ["{{{first}}}"]}}""");

        Assert.Equal(SettingOrigins.ConfigFile, fields.Reading.Origin);
        Assert.Equal(offers.Skip(1), fields.RemainingOffers);

        var added = fields.Build_Added_OrNull(fields.RemainingOffers[0]);
        Assert.Equal(new[] { first, fields.RemainingOffers[0] }, Words(added));
        Assert.Null(fields.Reading.Definition.Validate_OrNull(added));
    }

    [Fact]
    public void AListEdit_OnARowThatIsNotAList_OrOffTheEnd_Throws()
    {
        var expiry = Editor("highRiskCodeExpiryMinutes");
        var patterns = Editor("highRiskPatterns", """{"highRiskPatterns": ["push", "deploy"]}""");

        Assert.Throws<InvalidOperationException>(() => expiry.Build_Removed(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => patterns.Build_Moved(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => patterns.Build_Moved(1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => patterns.Build_Removed(2));
        Assert.Empty(expiry.RemainingOffers);
        Assert.Empty(expiry.Items);
    }

    /// <summary>The masked token (P2) holds an empty box: its value never leaves the reader, so the editor cannot put it on screen either.</summary>
    [Fact]
    public void TheMaskedToken_HoldsAnEmptyBox()
    {
        var token = Editor(SettingsSnapshot_Reader.MASKED_SECRET_PATH, """{"web": {"token": "s3cret"}}""");

        Assert.Equal(SettingsSnapshot_Reader.SECRET_SET, token.Reading.DisplayValue);
        Assert.Equal(string.Empty, token.EditText);
    }
}
