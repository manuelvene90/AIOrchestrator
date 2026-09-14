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
    /// to move (D2, owner 2026-09-14; Task 10 moves it), so it reads the catalogue rather than naming a
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

    static IReadOnlyList<string> Words(string path)
    {
        return Sorted(Catalog.Find_OrNull(path)!.EnumValues);
    }

    static IReadOnlyList<string> Sorted(IEnumerable<string> words)
    {
        return words.Order(StringComparer.Ordinal).ToArray();
    }
}
