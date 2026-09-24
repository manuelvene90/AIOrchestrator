using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using Xunit;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.PhoneSettings;

/// <summary>
/// THE <c>phone</c> BLOCK, THROUGH THE LOADER — a real config.json on a temp supervision root, the
/// fixture <c>PerRoleModelDefaultsTests</c> uses. This is a loader test, not a resolver test: the
/// resolver's precedence has its own file, and what can break HERE is the wiring — a block parsed
/// without the preset tree, a path spelled differently from the catalogue's, a parser that throws on
/// a word the catalogue does not know.
/// </summary>
public class PhoneSettingsJsonTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public PhoneSettingsJsonTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-phone-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A MACHINE THAT SAYS NOTHING GETS classic (Presets_Loader: `preset` absent means classic), and
    /// classic is Manu's phone. Of this block's rows classic STATES only <c>topic.modeGlyphs</c> —
    /// every other row falls through to the catalogue's shipped default. The reply keyboard is one of
    /// those now: classic stated <c>on</c> until the owner answered D5 on 2026-09-14 ("off for both"),
    /// so it reads the shipped <c>off</c> like quiet does.
    /// </summary>
    [Fact]
    public void WithNoConfigFileAtAll_ThePhoneBlockIsClassics()
    {
        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal(ModeGlyphPlacements.Name, config.Phone.TopicModeGlyphs);

        // Not stated by classic — the catalogue's own defaults.
        Assert.Equal(ReplyKeyboardModes.Off, config.Phone.ReplyKeyboard);
        Assert.Equal(PhonePushModes.Filtered, config.Phone.Push);
        Assert.Equal(ReceiptStyles.Ticks, config.Phone.Receipts);
        Assert.True(config.Phone.AppMessagesRing);
        Assert.True(config.Phone.PeriodicStatus);
        Assert.Equal(30, config.Phone.PeriodicStatusIntervalMinutes);
    }

    /// <summary>Nathan's phone, and the row-for-row inverse of the one above.</summary>
    [Fact]
    public void UnderTheQuietPreset_ThePhoneBlockIsTheForks()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet"}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.Equal(PhonePushModes.Everything, config.Phone.Push);
        Assert.False(config.Phone.PeriodicStatus);
        Assert.False(config.Phone.AppMessagesRing);
        Assert.Equal(ReceiptStyles.Reactions, config.Phone.Receipts);
        Assert.Equal(TopicCloseActions.Delete, config.Phone.TopicOnClose);
        Assert.Equal(ReplyKeyboardModes.Off, config.Phone.ReplyKeyboard);
    }

    /// <summary>config.json beats the preset — the third rung, proven through the loader.</summary>
    [Fact]
    public void AValueInConfigJson_BeatsTheNamedPreset()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet","phone":{"receipts":"ticks"}}""");

        Assert.Equal(ReceiptStyles.Ticks, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone.Receipts);
    }

    /// <summary>
    /// A MISSPELLED VALUE COSTS THAT KEY ITS DEFAULT, NEVER THE LOAD — the rule EffortSettings_Json
    /// states and the reason OrchestratorConfig_Loader catches a bad `preset` word: the provider calls
    /// this on every tick with no try/catch above it, so a config the app refuses to load is a bridge
    /// that does not start.
    /// </summary>
    [Fact]
    public void AMisspelledReceiptStyle_FallsToTheDefault_AndDoesNotThrow()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"phone":{"receipts":"emoji"}}""");

        Assert.Equal(ReceiptStyles.Ticks, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone.Receipts);
    }

    /// <summary>
    /// THE RE-HOMED PAIR. `telegram.foldLongEntriesAbove` is what both machines' config.json already
    /// says; `phone.foldLongEntriesAbove` is what the catalogue registers. Both must resolve, and the
    /// new spelling must win when a file carries both — the resolver's stated order, proven here at the
    /// loader so the alias cannot quietly stop being read.
    /// </summary>
    [Fact]
    public void TheOldTelegramSpelling_StillResolves_AndTheNewOneWins()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"telegram":{"foldLongEntriesAbove":100}}""");
        Assert.Equal(100, OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramProse.FoldLongEntriesAbove);

        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"telegram":{"foldLongEntriesAbove":100},"phone":{"foldLongEntriesAbove":250}}""");
        Assert.Equal(250, OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramProse.FoldLongEntriesAbove);
    }

    /// <summary>
    /// THE TWO SPELLINGS OF EVERY WORD ARE THE SAME SPELLING. Each parser answers its default for a
    /// word it does not know, so a catalogue word that drifted from its parser's would never throw —
    /// it would silently read as the default, and for the default's own word nothing at all would
    /// change on the phone. The only place that drift is visible is a comparison of the two lists.
    /// </summary>
    [Fact]
    public void EveryCatalogueWord_IsAWordItsParserKnows()
    {
        Assert.Equal(Words("phone.push"), Sorted([PhonePush_Modes.FILTERED_TEXT, PhonePush_Modes.EVERYTHING_TEXT]));
        Assert.Equal(Words("phone.receipts"), Sorted([Receipt_Styles.TICKS_TEXT, Receipt_Styles.REACTIONS_TEXT]));
        Assert.Equal(Words("phone.replyKeyboard"), Sorted([ReplyKeyboard_Modes.OFF_TEXT, ReplyKeyboard_Modes.ON_TEXT]));
        Assert.Equal(Words("topic.onClose"), Sorted([TopicClose_Actions.DELETE_TEXT, TopicClose_Actions.CLOSE_TEXT]));
        Assert.Equal(Words("topic.modeGlyphs"), Sorted([ModeGlyph_Placements.NAME_TEXT, ModeGlyph_Placements.PULSE_HEADER_TEXT]));
    }

    /// <summary>
    /// TOPIC CLOSE'S DEFAULT IS THE CATALOGUE'S, NOT A LITERAL — the one parser whose fallback is ruled
    /// to move (D2, owner 2026-09-14; plan 03 Task 10 moved it to delete), so it reads the catalogue rather than naming a
    /// member. An unknown word must land wherever the catalogue's default currently is.
    /// </summary>
    [Fact]
    public void AnUnknownTopicCloseWord_ReadsAsTheCataloguesShippedDefault()
    {
        var shipped = Catalog.Find_OrNull("topic.onClose")!.Default_OrNull!.GetValue<string>();

        Assert.Equal(TopicClose_Actions.Parse_OrDefault(shipped), TopicClose_Actions.Parse_OrDefault("archive"));
        Assert.Equal(TopicCloseActions.Delete, TopicClose_Actions.Parse_OrDefault(" DELETE "));
        Assert.Equal(TopicCloseActions.Close, TopicClose_Actions.Parse_OrDefault("Close"));
    }

    /// <summary>
    /// THE OWNER'S WINDOW, 2026-09-23 (plan 03 task 13): <i>"it should be a buffer of 6 seconds, giving me
    /// the time to press wait if I need."</i> Classic states 6 s and NO discount — the finished-message
    /// wait equal to the window — because the fork's 2-second discount let a finished single message leave
    /// before ⏸ Wait could reach it. No config file at all is classic.
    /// </summary>
    [Fact]
    public void WithNoConfigFileAtAll_TheAggregationWindowIsSixSeconds_WithNoFinishedMessageDiscount()
    {
        var phone = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone;

        Assert.Equal(6, phone.AggregationSeconds);
        Assert.Equal(6, phone.FinishedMessageSeconds);
    }

    /// <summary>Quiet states nothing new: it IS today's behaviour, the fork's 3 s and 2 s (bb91051a).</summary>
    [Fact]
    public void UnderTheQuietPreset_TheAggregationWindowIsTodays_ThreeSecondsAndTwo()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet"}""");

        var phone = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone;

        Assert.Equal(3, phone.AggregationSeconds);
        Assert.Equal(2, phone.FinishedMessageSeconds);
    }

    /// <summary>The third rung for the window: config.json beats classic's 6 for either key, independently.</summary>
    [Fact]
    public void AnAggregationWindowInConfigJson_BeatsThePreset()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"phone":{"aggregationSeconds":10,"finishedMessageSeconds":1}}""");

        var phone = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone;

        Assert.Equal(10, phone.AggregationSeconds);
        Assert.Equal(1, phone.FinishedMessageSeconds);
    }

    /// <summary>
    /// A BAD WINDOW COSTS THAT KEY ITS PRESET VALUE, NEVER THE LOAD — zero (a window a message could not be
    /// held in), a word, a value past the row's 60 s ceiling, a negative discount. Each falls to classic's
    /// 6, and the load still answers.
    /// </summary>
    [Theory]
    [InlineData("""{"repos":[],"phone":{"aggregationSeconds":0,"finishedMessageSeconds":-1}}""")]
    [InlineData("""{"repos":[],"phone":{"aggregationSeconds":"six","finishedMessageSeconds":"none"}}""")]
    [InlineData("""{"repos":[],"phone":{"aggregationSeconds":999,"finishedMessageSeconds":61}}""")]
    public void AMisspelledOrOutOfRangeWindow_FallsToThePresetsValue_AndDoesNotThrow(string configJson)
    {
        File.WriteAllText(_paths.ConfigFile, configJson);

        var phone = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone;

        Assert.Equal(6, phone.AggregationSeconds);
        Assert.Equal(6, phone.FinishedMessageSeconds);
    }

    /// <summary>
    /// A DISCOUNT LONGER THAN THE WINDOW IS LEGAL CONFIG — each row is bounded on its own — and the block
    /// reports it AS STATED. Serving it as the window is the buffer's job at the point of use
    /// (<c>OwnerDeliveryBufferTests.AFinishedMessageDiscountLongerThanTheWindow_IsServedAsTheWindow</c>);
    /// clamping here as well would be a second copy of the rule.
    /// </summary>
    [Fact]
    public void AFinishedMessageDiscountAboveTheWindow_IsReportedAsStated()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet","phone":{"finishedMessageSeconds":20}}""");

        var phone = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone;

        Assert.Equal(3, phone.AggregationSeconds);
        Assert.Equal(20, phone.FinishedMessageSeconds);
    }

    /// <summary>
    /// NO PER-REPO TOPIC COLOUR UNDER CLASSIC (plan 03 task 14). Owner, 2026-09-23: <i>"the topic icon gets
    /// colored without any context of why, red, blue, green, seemingly random."</i> Classic states
    /// <c>topic.repoColours</c> false; no config file at all is classic.
    /// </summary>
    [Fact]
    public void WithNoConfigFileAtAll_TopicsAreNotColouredPerRepo()
    {
        Assert.False(OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone.TopicRepoColours);
    }

    /// <summary>Quiet states nothing: the shipped default IS today's behaviour, the fork's brief F1.</summary>
    [Fact]
    public void UnderTheQuietPreset_TopicsAreColouredPerRepo_AsToday()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet"}""");

        Assert.True(OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone.TopicRepoColours);
    }

    /// <summary>
    /// The third rung, in config.json's own nested spelling: a <c>topic</c> object beats classic's false,
    /// and a word where a boolean belongs costs the key its preset value, never the load.
    /// </summary>
    [Theory]
    [InlineData("""{"repos":[],"topic":{"repoColours":true}}""", true)]
    [InlineData("""{"repos":[],"preset":"quiet","topic":{"repoColours":false}}""", false)]
    [InlineData("""{"repos":[],"topic":{"repoColours":"yes please"}}""", false)]
    public void ARepoColoursValueInConfigJson_BeatsThePreset(string configJson, bool expected)
    {
        File.WriteAllText(_paths.ConfigFile, configJson);

        Assert.Equal(expected, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone.TopicRepoColours);
    }

    /// <summary>
    /// AWAY MODE AFTER AN HOUR UNDER CLASSIC (plan 03 task 18). Owner, 2026-09-23 (entry [95]): <i>"the away
    /// mode is triggered too soon all the time. That also should be a setting."</i> Classic states 60 — the
    /// controller's value, announced in entry [97] — and no config file at all is classic.
    /// </summary>
    [Fact]
    public void WithNoConfigFileAtAll_AwayModeStartsAfterAnHour()
    {
        Assert.Equal(60, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone.AwayAfterMinutes);
    }

    /// <summary>Quiet states nothing: the shipped default IS today's behaviour, fifteen minutes.</summary>
    [Fact]
    public void UnderTheQuietPreset_AwayModeStartsAfterTodaysFifteenMinutes()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet"}""");

        Assert.Equal(15, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone.AwayAfterMinutes);
    }

    /// <summary>
    /// The third rung, in config.json's nested spelling, both ways round — and 0, which is legal: it is
    /// "away mode never starts by itself", not a value to refuse.
    /// </summary>
    [Theory]
    [InlineData("""{"repos":[],"away":{"afterMinutes":0}}""", 0)]
    [InlineData("""{"repos":[],"away":{"afterMinutes":1440}}""", 1440)]
    [InlineData("""{"repos":[],"preset":"quiet","away":{"afterMinutes":45}}""", 45)]
    public void AnAwayDelayInConfigJson_BeatsThePreset(string configJson, int expected)
    {
        File.WriteAllText(_paths.ConfigFile, configJson);

        Assert.Equal(expected, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone.AwayAfterMinutes);
    }

    /// <summary>
    /// A BAD DELAY COSTS THE KEY ITS PRESET VALUE, NEVER THE LOAD — a negative, a value past the row's day,
    /// a word. Each falls to classic's 60, the way <c>phone.aggregationSeconds</c> does (task 13).
    /// </summary>
    [Theory]
    [InlineData("""{"repos":[],"away":{"afterMinutes":-1}}""")]
    [InlineData("""{"repos":[],"away":{"afterMinutes":1441}}""")]
    [InlineData("""{"repos":[],"away":{"afterMinutes":"an hour"}}""")]
    public void AMisspelledOrOutOfRangeAwayDelay_FallsToThePresetsValue_AndDoesNotThrow(string configJson)
    {
        File.WriteAllText(_paths.ConfigFile, configJson);

        Assert.Equal(60, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone.AwayAfterMinutes);
    }

    static IReadOnlyList<string> Words(string path)
    {
        return Sorted(Catalog.Find_OrNull(path)!.EnumValues);
    }

    static IReadOnlyList<string> Sorted(IEnumerable<string> words)
    {
        return words.Order(StringComparer.Ordinal).ToArray();
    }
}
