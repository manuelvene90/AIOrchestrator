using System.Globalization;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsPresentation;

/// <summary>
/// ONE WAY TYPED TEXT BECOMES A VALUE (ruling P13), because two renderers take typed text — the Telegram
/// reply step and the WPF text box — and each would otherwise grow its own parser (decision 12). And the
/// parser NEVER REFUSES (decision 21): what it cannot read as the row's kind it passes through raw, so the
/// DEFINITION refuses it in the definition's own words — the only words a refusal may be in.
/// </summary>
public class SettingValueParserTests
{
    static ISettingDefinition Definition(string path)
    {
        return Catalog.Find_OrNull(path) ?? throw new InvalidOperationException($"'{path}' is not in the catalogue — this test measures nothing");
    }

    /// <summary>
    /// A chat id is past Int32, so the number is a long. And the culture is the invariant one on purpose:
    /// this machine's owner types from an Italian phone, where "1.000" is a thousand and "1,5" is one and a
    /// half — neither is a whole number to the catalogue, and neither may quietly become one here.
    /// </summary>
    [Fact]
    public void AnInt_ParsesToALong_InTheInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");

        try
        {
            var chatId = SettingValue_Parser.Parse(Definition("telegramSupergroupChatId"), "-1001234567890");
            var expiry = SettingValue_Parser.Parse(Definition("buttonExpiryMinutes"), " 720 ");

            Assert.Equal(-1001234567890L, chatId!.GetValue<long>());
            Assert.Equal(720L, expiry!.GetValue<long>());
            Assert.Null(Definition("buttonExpiryMinutes").Validate_OrNull(expiry));

            var thousand = SettingValue_Parser.Parse(Definition("buttonExpiryMinutes"), "1.000");
            Assert.Equal("1.000", thousand!.GetValue<string>());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AWordThatIsNotANumber_IsPassedThroughRaw_SoTheDefinitionRefusesItInItsOwnWords()
    {
        var definition = Definition("buttonExpiryMinutes");

        var value = SettingValue_Parser.Parse(definition, "twelve");

        Assert.Equal("twelve", value!.GetValue<string>());
        Assert.Equal("'buttonExpiryMinutes' must be a whole number", definition.Validate_OrNull(value));
    }

    [Fact]
    public void AnEnumWord_IsPassedThrough_AndAnUnknownOneIsTheDefinitionsToRefuse()
    {
        var definition = Definition("phone.receipts");

        Assert.Equal("reactions", SettingValue_Parser.Parse(definition, "reactions")!.GetValue<string>());
        Assert.Equal("reactions", SettingValue_Parser.Parse(definition, " reactions ")!.GetValue<string>());

        var typo = SettingValue_Parser.Parse(definition, "reaction");
        Assert.Contains("'reaction' is not a valid value for 'phone.receipts'", definition.Validate_OrNull(typo));
    }

    /// <summary>A list is typed as one line on a phone, so commas separate it; blank words are the owner's stray commas.</summary>
    [Fact]
    public void AList_IsSplitOnCommas_EachWordTrimmed_AndBlankIsTheEmptyList()
    {
        var definition = Definition("pulse.fields");

        var value = SettingValue_Parser.Parse(definition, "supervisor, members,updated, ");
        var empty = SettingValue_Parser.Parse(definition, "   ");

        Assert.Equal(new[] { "supervisor", "members", "updated" }, ((JsonArray)value!).Select(node => node!.GetValue<string>()));
        Assert.Empty((JsonArray)empty!);
        Assert.Null(definition.Validate_OrNull(value));

        var typo = SettingValue_Parser.Parse(definition, "supervisor, suprvisor");
        Assert.Contains("'suprvisor' is not a pulse field", definition.Validate_OrNull(typo));
    }

    [Fact]
    public void ABool_ReadsTheFormattersWords_AndJsonsOwn()
    {
        var definition = Definition("telegramStatusScreenshots");

        Assert.True(SettingValue_Parser.Parse(definition, "on")!.GetValue<bool>());
        Assert.False(SettingValue_Parser.Parse(definition, "off")!.GetValue<bool>());
        Assert.True(SettingValue_Parser.Parse(definition, "True")!.GetValue<bool>());
        Assert.False(SettingValue_Parser.Parse(definition, "false")!.GetValue<bool>());

        var maybe = SettingValue_Parser.Parse(definition, "maybe");
        Assert.Equal("'telegramStatusScreenshots' must be true or false", definition.Validate_OrNull(maybe));
    }

    /// <summary>
    /// THE NULL MEANING IS A VALUE, NOT A RESET (ruling P3): "not set" on a nullable row is JSON null — the
    /// stated "nothing", which overrides classic's xhigh — and on any other row it is just a word, for the
    /// definition to refuse or keep. Nullability is read off the definition itself (P19): null is acceptable.
    /// </summary>
    [Fact]
    public void TheNullMeaning_ParsesToNull_OnlyForARowThatAcceptsNull()
    {
        Assert.Null(SettingValue_Parser.Parse(Definition("effort.supervisor"), SettingValue_Formatter.NOT_SET));
        Assert.Null(SettingValue_Parser.Parse(Definition("telegramOwnerUserId"), "  "));

        var receipts = SettingValue_Parser.Parse(Definition("phone.receipts"), SettingValue_Formatter.NOT_SET);
        Assert.Equal(SettingValue_Formatter.NOT_SET, receipts!.GetValue<string>());
        Assert.NotNull(Definition("phone.receipts").Validate_OrNull(receipts));
    }

    /// <summary>Free text is the owner's exactly as typed — a command line's spacing is not the parser's to tidy.</summary>
    [Fact]
    public void AString_IsPassedThroughUntouched()
    {
        var value = SettingValue_Parser.Parse(Definition("voiceTranscribeCommand"), " whisper --lang it ");

        Assert.Equal(" whisper --lang it ", value!.GetValue<string>());
    }

    /// <summary>
    /// WHAT A RENDERER OFFERS, IT CAN WRITE — ONE TAP AT A TIME. Every Toggle and Choice offer, parsed back,
    /// is a value its own definition accepts and reads as the very words on the button; every OrderedList
    /// offer, parsed back as the SINGLE-ELEMENT list a picker actually writes when the owner taps one word,
    /// is a value its own definition accepts and reads back the same way. It is NOT every offer joined into
    /// one list: the offer set is candidates a picker lets the owner choose FROM, not a list meant to be
    /// written whole — <c>pulse.buttons</c>' offers legitimately contain both "tail" and "tail sup", which
    /// the BOT_COMMANDS validator refuses together on one bar (see
    /// <c>SettingsSnapshotReaderTests.PulseButtons_OffersEveryVerbATapCanRun_TailSupIncluded</c> for that
    /// refusal pinned). A renderer that draws an offer and then shows a refusal for the single word it just
    /// drew is the one failure a picker exists to prevent.
    /// </summary>
    [Fact]
    public void EveryOfferedValue_ParsesToAValueItsDefinitionAccepts_AndReadsBackAsTheSameWords()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Presets_Loader.Load_Embedded(Presets_Loader.CLASSIC), Presets_Loader.CLASSIC, session: null);

        foreach (var reading in readings)
        {
            var definition = reading.Definition;

            if (definition.Renderer is SettingRenderers.Toggle or SettingRenderers.Choice or SettingRenderers.OrderedList)
            {
                foreach (var offered in reading.OfferedValues)
                {
                    var value = SettingValue_Parser.Parse(definition, offered);

                    Assert.Null(definition.Validate_OrNull(value));
                    Assert.Equal(offered, SettingValue_Formatter.Describe(definition, value));
                }
            }
        }
    }
}
