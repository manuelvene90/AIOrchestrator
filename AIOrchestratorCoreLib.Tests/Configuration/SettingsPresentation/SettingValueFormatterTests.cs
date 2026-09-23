using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsPresentation;

/// <summary>
/// ONE WAY A VALUE READS AS TEXT, because the alternative is three. A WPF label, a Telegram button
/// caption and a web table cell showing "30", "30 min" and "30 minutes" for the same row is CLAUDE.md
/// decision 12's drift wearing three hats, and unlike a formatter inside one file it cannot be found by
/// reading any one of them.
///
/// <para>
/// ONE PHRASE FOR "NOTHING" (ruling P19, 2026-09-23): absent, JSON null and a blank string all read
/// <see cref="SettingValue_Formatter.NOT_SET"/>, an empty list reads <see cref="SettingValue_Formatter.NONE"/>.
/// What "nothing" MEANS for a given row — "no --effort flag", "voice notes are not transcribed" — stays in
/// that row's Description, because a path-keyed phrase table here would be a second home for catalogue facts.
/// </para>
/// </summary>
public class SettingValueFormatterTests
{
    /// <summary>
    /// A MISSING DEFINITION THROWS rather than reading as a pass (decision 20): a formatter test that could
    /// not find its row would otherwise be asserting on nothing.
    /// </summary>
    static ISettingDefinition Definition(string path)
    {
        return Catalog.Find_OrNull(path) ?? throw new InvalidOperationException($"'{path}' is not in the catalogue — this test measures nothing");
    }

    static JsonNode Parsed(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void ABool_ReadsAsOnOrOff()
    {
        var definition = Definition("telegramStatusScreenshots");

        Assert.Equal("on", SettingValue_Formatter.Describe(definition, JsonValue.Create(true)));
        Assert.Equal("off", SettingValue_Formatter.Describe(definition, JsonValue.Create(false)));
        Assert.Equal("on", SettingValue_Formatter.Describe(definition, Parsed("true")));
    }

    [Fact]
    public void AnEnum_ReadsAsItsOwnWord()
    {
        var definition = Definition("phone.receipts");

        Assert.Equal("reactions", SettingValue_Formatter.Describe(definition, JsonValue.Create("reactions")));
        Assert.Equal("ticks", SettingValue_Formatter.Describe(definition, Parsed("\"ticks\"")));
    }

    /// <summary>
    /// effort.* SHIPS NULL, AND NULL IS A MEANING, NOT A BLANK (plan 02 task 7). The formatter says the
    /// generic "not set" (P19); the row's own description is what says that means no --effort flag at all,
    /// and this pins that the meaning is actually there for a renderer to show beside it.
    /// </summary>
    [Fact]
    public void ANullableEnumWithNoValue_ReadsAsNotSet_NotAsBlank()
    {
        var definition = Definition("effort.supervisor");

        var text = SettingValue_Formatter.Describe(definition, value: null);

        Assert.Equal(SettingValue_Formatter.NOT_SET, text);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("no --effort flag", definition.Description);
    }

    /// <summary>
    /// A supergroup chat id is past Int32, and a value read off disk is JsonElement-backed while a shipped
    /// default is int-backed — all three shapes must read as the same plain number, in the invariant culture
    /// (no thousands separator a machine in Italy would add).
    /// </summary>
    [Fact]
    public void AnInt_ReadsAsItsNumber_AndANullableAbsentOne_AsNotSet()
    {
        var expiry = Definition("buttonExpiryMinutes");
        var chatId = Definition("telegramSupergroupChatId");

        Assert.Equal("720", SettingValue_Formatter.Describe(expiry, JsonValue.Create(720)));
        Assert.Equal("1440", SettingValue_Formatter.Describe(expiry, Parsed("1440")));
        Assert.Equal("-1001234567890", SettingValue_Formatter.Describe(chatId, JsonValue.Create(-1001234567890L)));
        Assert.Equal("-1001234567890", SettingValue_Formatter.Describe(chatId, Parsed("-1001234567890")));
        Assert.Equal(SettingValue_Formatter.NOT_SET, SettingValue_Formatter.Describe(chatId, value: null));
    }

    /// <summary>
    /// voiceTranscribeCommand and owner.name SHIP "", and an empty cell in a table or an empty button caption
    /// reads as a rendering bug, not as a value. Blank is absent everywhere else in this catalogue (the
    /// resolver's own rule), so it reads the way absent reads.
    /// </summary>
    [Fact]
    public void AnEmptyString_ReadsAsNotSet_NotAsAnEmptyCell()
    {
        Assert.Equal(SettingValue_Formatter.NOT_SET, SettingValue_Formatter.Describe(Definition("voiceTranscribeCommand"), JsonValue.Create("")));
        Assert.Equal(SettingValue_Formatter.NOT_SET, SettingValue_Formatter.Describe(Definition("owner.name"), JsonValue.Create("   ")));
        Assert.Equal("Gianpiero", SettingValue_Formatter.Describe(Definition("owner.name"), JsonValue.Create("Gianpiero")));
    }

    /// <summary>classic's general.buttons is [] — "no bar at all" — which must read as a word, not as nothing.</summary>
    [Fact]
    public void AStringList_ReadsAsItsWordsInOrder_AndAnEmptyOne_AsNone()
    {
        var definition = Definition("general.buttons");

        Assert.Equal("summary, pending, limits", SettingValue_Formatter.Describe(definition, Parsed("""["summary","pending","limits"]""")));
        Assert.Equal("limits, summary", SettingValue_Formatter.Describe(definition, Parsed("""["limits","summary"]""")));
        Assert.Equal(SettingValue_Formatter.NONE, SettingValue_Formatter.Describe(definition, Parsed("[]")));
    }

    /// <summary>
    /// A BLANK WORD IN A LIST IS SHOWN, NOT SWALLOWED (fix round 1, 2026-09-23). highRiskPatterns has no
    /// validator and the resolver treats only a blank STRING as absent, so <c>[""]</c> and <c>["  "]</c> are
    /// accepted values — and joined as-is they read as a blank line, which the reading factory refuses, so one
    /// hand-edit took the whole snapshot down for every renderer at once. Every blank or whitespace-only word
    /// reads as <see cref="SettingValue_Formatter.BLANK_WORD"/>, one representation whatever whitespace it holds.
    /// </summary>
    [Fact]
    public void ABlankWordInAList_ReadsAsTwoQuotes_SoTheListNeverReadsBlank()
    {
        var definition = Definition("highRiskPatterns");

        Assert.Equal("\"\"", SettingValue_Formatter.BLANK_WORD);
        Assert.Equal(SettingValue_Formatter.BLANK_WORD, SettingValue_Formatter.Describe(definition, Parsed("""[""]""")));
        Assert.Equal(SettingValue_Formatter.BLANK_WORD, SettingValue_Formatter.Describe(definition, Parsed("""["  "]""")));
        Assert.Equal("\"\", \"\"", SettingValue_Formatter.Describe(definition, Parsed("""["", "\t"]""")));
        Assert.Equal("push, \"\", deploy", SettingValue_Formatter.Describe(definition, Parsed("""["push", "   ", "deploy"]""")));
    }

    /// <summary>
    /// ONE WORD, CAPTIONED AS THE JOINED READING SPELLS IT — the WPF list editor draws each element through this,
    /// so its items and the row's one-line reading cannot disagree (decision 12). Not cut: an editor listing a
    /// long pattern on its own line must show which pattern it is.
    /// </summary>
    [Fact]
    public void AListWord_IsCaptionedAsTheJoinedReadingSpellsIt_AndIsNotCut()
    {
        var longPattern = new string('x', SettingValue_Formatter.MAX_LENGTH + 10);

        Assert.Equal("push", SettingValue_Formatter.Describe_ListWord(JsonValue.Create("push")));
        Assert.Equal(SettingValue_Formatter.BLANK_WORD, SettingValue_Formatter.Describe_ListWord(JsonValue.Create("  ")));
        Assert.Equal("3", SettingValue_Formatter.Describe_ListWord(JsonValue.Create(3)));
        Assert.Equal("null", SettingValue_Formatter.Describe_ListWord(null));
        Assert.Equal(longPattern, SettingValue_Formatter.Describe_ListWord(JsonValue.Create(longPattern)));
        Assert.Equal("a b", SettingValue_Formatter.Describe_ListWord(JsonValue.Create("a\nb")));
    }

    /// <summary>
    /// repos and planBackend are STRUCTURES, and their raw JSON is as long as the owner's repo list — which
    /// on a Telegram button caption is a message that grows with every repo added.
    /// </summary>
    [Fact]
    public void AComposite_ReadsAsAOneLineSummary_NeverAsRawJsonOfUnboundedLength()
    {
        var repos = Definition("repos");
        var planBackend = Definition("planBackend");

        var threeRepos = Parsed("""
            [
              {"name": "Arb Studio", "path": "C:\\Users\\someone\\Documents\\Visual Studio 2022\\Projects\\ArbitrageProg"},
              {"name": "Suite", "path": "C:\\Users\\someone\\Documents\\Visual Studio 2022\\Projects\\Suite"},
              {"name": "Tickets", "path": "C:\\Users\\someone\\Desktop\\Tickets Workspace", "topicColor": 7322096}
            ]
            """);
        var backend = Parsed("""{"kind": "external", "assembly": "C:\\a\\very\\long\\path\\to\\some\\plugin\\folder\\Backend.dll", "type": "Some.Type.Name"}""");

        Assert.Equal("3 entries", SettingValue_Formatter.Describe(repos, threeRepos));
        Assert.Equal("1 entry", SettingValue_Formatter.Describe(repos, Parsed("""[{"name":"a","path":"b"}]""")));
        Assert.Equal(SettingValue_Formatter.NONE, SettingValue_Formatter.Describe(repos, Parsed("[]")));
        Assert.Equal(SettingValue_Formatter.NOT_SET, SettingValue_Formatter.Describe(planBackend, value: null));

        var backendText = SettingValue_Formatter.Describe(planBackend, backend);

        Assert.StartsWith("kind: external", backendText);
        Assert.True(backendText.Length <= SettingValue_Formatter.MAX_LENGTH, $"'{backendText}' is longer than {SettingValue_Formatter.MAX_LENGTH}");
        Assert.DoesNotContain("{", backendText);
        Assert.DoesNotContain("\n", backendText);
    }

    /// <summary>
    /// A LIST IS THE OWNER'S TO GROW (highRiskPatterns is free text), so its reading is bounded by the
    /// formatter, not by the owner's restraint. The count keeps it honest: a cut list that did not say how
    /// much it hid would read as the whole list.
    /// </summary>
    [Fact]
    public void ALongStringList_IsTruncatedWithACount_SoAButtonCaptionCannotGrowWithoutLimit()
    {
        var definition = Definition("highRiskPatterns");
        var words = Enumerable.Range(1, 20).Select(n => $"pattern{n}").ToArray();
        var value = new JsonArray(words.Select(word => (JsonNode?)JsonValue.Create(word)).ToArray());

        var text = SettingValue_Formatter.Describe(definition, value);

        Assert.True(text.Length <= SettingValue_Formatter.MAX_LENGTH, $"'{text}' is longer than {SettingValue_Formatter.MAX_LENGTH}");
        Assert.StartsWith("pattern1, pattern2", text);

        var hidden = int.Parse(text[(text.LastIndexOf('+') + 1)..].Split(' ')[0]);
        var shown = text[..text.LastIndexOf(", +", StringComparison.Ordinal)].Split(", ").Length;

        Assert.EndsWith(" more", text);
        Assert.Equal(words.Length, shown + hidden);
    }

    /// <summary>
    /// The same bound for free text, for the same reason: a voice-transcribe command line is a path plus
    /// arguments, and a pasted one may carry a newline. One line, bounded, and the cut is marked.
    /// </summary>
    [Fact]
    public void ALongString_IsCutToOneBoundedLine_AndSaysItWasCut()
    {
        var definition = Definition("voiceTranscribeCommand");
        var command = "C:\\Tools\\whisper\\whisper-cli.exe --model C:\\Tools\\whisper\\models\\large-v3.bin\n--language auto";

        var text = SettingValue_Formatter.Describe(definition, JsonValue.Create(command));

        Assert.True(text.Length <= SettingValue_Formatter.MAX_LENGTH, $"'{text}' is longer than {SettingValue_Formatter.MAX_LENGTH}");
        Assert.DoesNotContain("\n", text);
        Assert.EndsWith("…", text);
        Assert.StartsWith("C:\\Tools\\whisper", text);
    }
}
