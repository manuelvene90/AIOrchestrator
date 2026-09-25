using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsPresentation;

/// <summary>
/// THE ORIGIN IS WHAT THE OWNER IS ACTUALLY ASKING (spec §6.2: the renderers show the origin of every
/// value and offer Reset). "Why is my phone on reactions when the catalogue says ticks" is answered by
/// the words "from preset quiet" and by nothing else on the screen.
///
/// <para>
/// NO COUNT IS A LITERAL. The catalogue is growing on a sibling branch (plan 03 Tasks 13-15 add four
/// rows), so every "how many" below is computed from <c>SettingsCatalog.ALL</c> — a test that asserted 66
/// would go red for a correct change and teach the next reader to update numbers rather than read them.
/// </para>
/// </summary>
public class SettingsSnapshotReaderTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public SettingsSnapshotReaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-settings-reader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    static JsonObject Tree(string json) => (JsonObject)JsonNode.Parse(json)!;

    static JsonObject Classic => Presets_Loader.Load_Embedded(Presets_Loader.CLASSIC);

    static JsonObject Quiet => Presets_Loader.Load_Embedded(Presets_Loader.QUIET);

    /// <summary>A reading that is missing throws (decision 20) — a null here would make the assertion after it measure nothing.</summary>
    static ISettingReading Reading(IReadOnlyList<ISettingReading> readings, string path)
    {
        return readings.SingleOrDefault(reading => reading.Definition.Path == path)
            ?? throw new InvalidOperationException($"no reading for '{path}' — the test measures nothing");
    }

    static IOrchestrationSession Session(string? supervisorModelOverride = null, bool paused = false)
    {
        return OrchestrationSession_Factory.Create(
            "arb-fix", "Arb Studio", @"C:\repos\arb", new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc),
            telegramTopicId: null, supervisorPid: null, supervisorSpawnedUtc: null, communicatorSpawnedUtc: null,
            displayName: null, supervisorModelOverride: supervisorModelOverride, implementerModelOverride: null,
            members: [], telegramMode: TelegramDeliveryModes.Normal, closedUtc: null, paused: paused);
    }

    [Fact]
    public void EveryCatalogueEntry_ProducesExactlyOneReading()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);

        Assert.Equal(Catalog.ALL.Count, readings.Count);
        Assert.Equal(Catalog.ALL.Select(definition => definition.Path), readings.Select(reading => reading.Definition.Path));
        Assert.All(readings, reading => Assert.Same(Catalog.Find_OrNull(reading.Definition.Path), reading.Definition));
    }

    [Fact]
    public void AShippedDefault_SaysShippedDefault()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);

        var receipts = Reading(readings, "phone.receipts");

        Assert.Equal(SettingOrigins.ShippedDefault, receipts.Origin);
        Assert.Equal("shipped default", receipts.OriginLabel);
        Assert.Equal("ticks", receipts.DisplayValue);
        Assert.Equal("ticks", receipts.Value_OrNull!.GetValue<string>());
    }

    /// <summary>"from preset classic", not "from a preset" — the owner has two, and which one is the answer.</summary>
    [Fact]
    public void APresetValue_NamesThePresetItCameFrom()
    {
        var classic = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);
        var quiet = SettingsSnapshot_Reader.Read_All(configTree: null, Quiet, Presets_Loader.QUIET, session: null);

        var holdToggle = Reading(classic, "pulse.holdToggle");
        var receipts = Reading(quiet, "phone.receipts");

        Assert.Equal(SettingOrigins.Preset, holdToggle.Origin);
        Assert.Equal("from preset classic", holdToggle.OriginLabel);
        Assert.Equal("off", holdToggle.DisplayValue);

        Assert.Equal(SettingOrigins.Preset, receipts.Origin);
        Assert.Equal("from preset quiet", receipts.OriginLabel);
        Assert.Equal("reactions", receipts.DisplayValue);
    }

    [Fact]
    public void AConfigFileValue_SaysSetHere()
    {
        var readings = SettingsSnapshot_Reader.Read_All(
            Tree("""{"phone":{"receipts":"reactions"},"buttonExpiryMinutes":60}"""), Classic, Presets_Loader.CLASSIC, session: null);

        var receipts = Reading(readings, "phone.receipts");
        var expiry = Reading(readings, "buttonExpiryMinutes");

        Assert.Equal(SettingOrigins.ConfigFile, receipts.Origin);
        Assert.Equal("set here", receipts.OriginLabel);
        Assert.Equal("reactions", receipts.DisplayValue);
        Assert.Equal("set here", expiry.OriginLabel);
        Assert.Equal("60", expiry.DisplayValue);
    }

    /// <summary>
    /// A SESSION VALUE IS SHOWN WITH WHAT IT SHADOWS (ruling P4). Editing models.supervisor on a settings
    /// page writes config.json, and for THIS orchestration that write changes nothing on screen — so the note
    /// names the machine's own value and where it came from, or the owner edits a row and watches it not move.
    /// The row stays editable: IsEditable is the writer's rule and nothing else, and under D3 the only view
    /// that carries a session is read-only as a whole.
    /// </summary>
    [Fact]
    public void AnOrchestrationScopedKeyWithASessionOverride_SaysSo_AndCarriesTheSessionNote()
    {
        var readings = SettingsSnapshot_Reader.Read_All(
            Tree("""{"models":{"supervisor":"sonnet"}}"""), Classic, Presets_Loader.CLASSIC, Session(supervisorModelOverride: "haiku"));

        var supervisor = Reading(readings, "models.supervisor");

        Assert.Equal(SettingOrigins.Session, supervisor.Origin);
        Assert.Equal("haiku", supervisor.DisplayValue);
        Assert.Equal("set for this orchestration", supervisor.OriginLabel);
        Assert.True(supervisor.IsEditable);
        Assert.NotNull(supervisor.SessionNote_OrNull);
        Assert.Contains("sonnet", supervisor.SessionNote_OrNull);
        Assert.Contains("set here", supervisor.SessionNote_OrNull);

        // A Machine-scoped row ignores the session entirely, and an Orchestration-scoped row the session
        // says nothing about carries no note either — the note means "the session is answering here".
        Assert.Null(Reading(readings, "models.reviewer").SessionNote_OrNull);
        Assert.Null(Reading(readings, "models.implementer").SessionNote_OrNull);
        Assert.Equal(SettingOrigins.ShippedDefault, Reading(readings, "models.implementer").Origin);
    }

    /// <summary>
    /// D12: A READ-ONLY ROW SAYS WHERE IT IS CHANGED, and the catalogue's own description is where that
    /// sentence lives — the reading hands the definition over whole, so no renderer invents a second one.
    /// The five are named here on purpose: a new ReadOnly row turns this red until someone says where IT
    /// is changed, which is the question a greyed-out control makes the owner ask.
    ///
    /// <para>
    /// EACH FRAGMENT NAMES A PLACE, NOT A PHRASE THAT MERELY EXISTS (ruling P31, fix round 1). The first
    /// version pinned "its own editor" and "named parser is the authority" — sentences that were really in
    /// the descriptions and named nowhere the owner could go, so the test was green over the very gap D12
    /// describes. The place is a window, a file, or the command that toggles the state.
    /// </para>
    /// </summary>
    [Fact]
    public void AReadOnlyRow_IsNotEditable_AndSaysWhereItIsChangedInstead()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, Session(paused: true));

        var whereEachIsChanged = new Dictionary<string, string>
        {
            ["repos"] = "desktop app's main window",
            ["planBackend"] = "hand-edited in config.json",
            ["session.paused"] = "/pause",
            ["session.telegramMode"] = "/dnd and /mute",
            ["session.ownerPresence"] = "/pc",
        };

        var readOnly = readings.Where(reading => reading.Definition.Renderer == SettingRenderers.ReadOnly).ToArray();

        Assert.Equal(whereEachIsChanged.Keys.Order(), readOnly.Select(reading => reading.Definition.Path).Order());

        foreach (var reading in readOnly)
        {
            Assert.False(reading.IsEditable, $"'{reading.Definition.Path}' is ReadOnly and must not be editable");
            Assert.Empty(reading.OfferedValues);
            Assert.Contains(whereEachIsChanged[reading.Definition.Path], reading.Definition.Description);
        }

        // ONE RULE, read off the catalogue, for every row — so the writer's refusal and a renderer's
        // greyed-out control can never disagree about which rows are which.
        Assert.All(readings, reading => Assert.Equal(reading.Definition.Renderer != SettingRenderers.ReadOnly, reading.IsEditable));

        Assert.Equal("on", Reading(readings, "session.paused").DisplayValue);
    }

    /// <summary>
    /// A PICKER, NOT FREE TEXT, FOR THE KNOWN-WORD LISTS (D15, ruling P5). The validators are implemented
    /// now (plan 03 Task 1), so the picker is no longer the only thing between a typo and config.json — but
    /// it is still right: a renderer that offers only what the definition will accept never has to show a
    /// refusal for a word it offered. That is drawing, not validating — the definition is still the only
    /// thing that says yes (decision 21) — and the two agree because both read PulseField_Names.ALL.
    /// </summary>
    [Fact]
    public void PulseFields_OffersEveryKnownPulseField_AndNothingElse()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);

        var fields = Reading(readings, "pulse.fields");

        Assert.Equal(PulseField_Names.ALL, fields.OfferedValues);
        Assert.Null(fields.Definition.Validate_OrNull(new JsonArray(fields.OfferedValues.Select(word => (JsonNode?)JsonValue.Create(word)).ToArray())));
    }

    /// <summary>
    /// THE VERBS A BAR CAN ACTUALLY DRAW, not every verb the "/" menu has (ruling P5). Plan 03 Task 5 left 20
    /// menu verbs with no tap route — the builders refuse to draw them — so a picker over BotCommandMenu.ALL
    /// would offer 20 buttons that never appear. The set is read through TopicCommandButtons.Has_TapRoute,
    /// never copied, so the day a verb is wired it is offered without a change here. "tail sup" is a verb
    /// WITH its target and is not a menu command at all, which is why it is asserted by name.
    /// </summary>
    [Fact]
    public void PulseButtons_OffersEveryVerbATapCanRun_TailSupIncluded()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);

        var pulseButtons = Reading(readings, "pulse.buttons");
        var generalButtons = Reading(readings, "general.buttons");

        var everyConfigurableVerb = BotCommandMenu.ALL.Select(command => command.Command).Append("tail sup").Distinct().ToArray();
        var drawable = everyConfigurableVerb.Where(TopicCommandButtons.Has_TapRoute).ToArray();

        Assert.Contains("tail sup", pulseButtons.OfferedValues);

        // THE OFFER SET IS THE DRAWABLE SET — set equality against TopicCommandButtons.Has_TapRoute's own
        // predicate over the menu plus "tail sup", not a claim that the offers are one list a picker would
        // ever write whole (see the mixed-bar refusal below: "tail" and "tail sup" are both offered and
        // cannot share a bar).
        Assert.Equal(drawable.Order(), pulseButtons.OfferedValues.Order());
        Assert.All(pulseButtons.OfferedValues, verb => Assert.True(TopicCommandButtons.Has_TapRoute(verb), $"'{verb}' is offered but no tap can run it"));
        Assert.Equal(pulseButtons.OfferedValues.Count, pulseButtons.OfferedValues.Distinct().Count());
        Assert.Equal(pulseButtons.OfferedValues, generalButtons.OfferedValues);

        // THE PICKER'S CONTRACT, PINNED: "tail" and "tail sup" are both offered (candidates the owner picks
        // FROM), but the BOT_COMMANDS validator refuses them together on one bar — a repeated first token,
        // whatever follows it. This is the rule SettingValueParserTests relies on when it validates each
        // offer as its own single-element list rather than the whole offer set joined into one.
        var mixedBar = new JsonArray(JsonValue.Create("tail"), JsonValue.Create("tail sup"));
        var refusal = pulseButtons.Definition.Validate_OrNull(mixedBar);
        Assert.NotNull(refusal);
        Assert.Contains("appears more than once", refusal);
        Assert.Contains("appears more than once", generalButtons.Definition.Validate_OrNull(mixedBar));
    }

    /// <summary>
    /// highRiskPatterns IS FREE TEXT (ruling P5): its words are substrings the owner chooses, and there is no
    /// list of them to pick from. Offering nothing is what tells a renderer to draw add / remove / reorder
    /// over typed words instead of a picker.
    /// </summary>
    [Fact]
    public void HighRiskPatterns_OffersNothing_BecauseItIsAFreeTextList()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);

        var patterns = Reading(readings, "highRiskPatterns");

        Assert.Equal(SettingRenderers.OrderedList, patterns.Definition.Renderer);
        Assert.Empty(patterns.OfferedValues);
        Assert.True(patterns.IsEditable);
    }

    /// <summary>
    /// THE NULL MEANING IS A CHOICE OF ITS OWN for a nullable Enum (ruling P3): picking it states "no
    /// --effort flag" — a JSON null the resolver stops at, overriding classic's xhigh — which is not the same
    /// as Reset. It is offered in the formatter's own words, so the button reads exactly as the value will.
    /// </summary>
    [Fact]
    public void AnEnumRow_OffersItsEnumValues_AndANullableOne_AlsoOffersTheNullMeaning()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);

        var receipts = Reading(readings, "phone.receipts");
        var effort = Reading(readings, "effort.supervisor");

        Assert.Equal(receipts.Definition.EnumValues, receipts.OfferedValues);
        Assert.Equal(effort.Definition.EnumValues.Append(SettingValue_Formatter.NOT_SET), effort.OfferedValues);
        Assert.Equal("high", effort.DisplayValue);
        Assert.Equal("from preset classic", effort.OriginLabel);
    }

    /// <summary>
    /// A Toggle has exactly two choices and they are offered in the words the value reads as, so a renderer
    /// that draws choices draws the same two words it shows.
    /// </summary>
    [Fact]
    public void AToggleRow_OffersOnAndOff_InTheFormattersOwnWords()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);

        Assert.Equal(new[] { SettingValue_Formatter.ON, SettingValue_Formatter.OFF }, Reading(readings, "telegramStatusScreenshots").OfferedValues);
    }

    [Fact]
    public void ATextRow_OffersNothing_BecauseItIsFreeText()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);

        Assert.Empty(Reading(readings, "voiceTranscribeCommand").OfferedValues);
        Assert.Empty(Reading(readings, "models.supervisor").OfferedValues);
        Assert.Empty(Reading(readings, "buttonExpiryMinutes").OfferedValues);
        Assert.All(
            readings.Where(reading => reading.Definition.Renderer is SettingRenderers.Text or SettingRenderers.Number),
            reading => Assert.Empty(reading.OfferedValues));
    }

    /// <summary>
    /// web.token IS MASKED IN THE READING (ruling P2). GET /settings is open on loopback (D4), and a GET that
    /// returned the token would make "a set token is still enforced" enforce nothing. The mask is here, in the
    /// one reading all three renderers share, so none of them can forget it — and the origin is still true,
    /// because "who set the token" is not a secret.
    /// </summary>
    [Fact]
    public void TheWebToken_IsMasked_ItsValueNeverLeavesTheReader()
    {
        var withToken = SettingsSnapshot_Reader.Read_All(Tree("""{"web":{"token":"s3cret-token"}}"""), Classic, Presets_Loader.CLASSIC, session: null);
        var withoutToken = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);

        var set = Reading(withToken, SettingsSnapshot_Reader.MASKED_SECRET_PATH);
        var unset = Reading(withoutToken, SettingsSnapshot_Reader.MASKED_SECRET_PATH);

        Assert.Equal("web.token", SettingsSnapshot_Reader.MASKED_SECRET_PATH);
        Assert.Null(set.Value_OrNull);
        Assert.Equal(SettingsSnapshot_Reader.SECRET_SET, set.DisplayValue);
        Assert.Equal(SettingOrigins.ConfigFile, set.Origin);
        Assert.Null(unset.Value_OrNull);
        Assert.Equal(SettingValue_Formatter.NOT_SET, unset.DisplayValue);

        Assert.All(withToken, reading =>
        {
            Assert.DoesNotContain("s3cret", reading.DisplayValue);
            Assert.DoesNotContain("s3cret", reading.SessionNote_OrNull ?? "");
            Assert.DoesNotContain("s3cret", reading.Value_OrNull?.ToJsonString() ?? "");
        });
    }

    /// <summary>
    /// A READING'S VALUE BELONGS TO NOBODY ELSE. The embedded presets are parsed once and shared by the whole
    /// process; a value handed out still attached to that tree cannot be put into a response body (a
    /// JsonNode with a parent throws when re-parented), and one a caller edited in place would change the
    /// preset for everyone after it. The same holds between two callers of ONE reading: the web GET and the
    /// Telegram menu may serialise the same reading, and the first must not leave the value parented for the
    /// second.
    /// </summary>
    [Fact]
    public void AReadingsValue_IsDetachedFromTheTreeItWasReadFrom()
    {
        var readings = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);

        var buttons = Reading(readings, "pulse.buttons");

        Assert.Equal(SettingOrigins.Preset, buttons.Origin);
        Assert.Null(buttons.Value_OrNull!.Parent);

        var first = new JsonObject { ["value"] = buttons.Value_OrNull };
        var second = new JsonObject { ["value"] = buttons.Value_OrNull };
        ((JsonArray)first["value"]!).Add("limits");

        Assert.DoesNotContain("limits", second.ToJsonString());
        Assert.DoesNotContain("limits", buttons.DisplayValue);

        var again = Reading(SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null), "pulse.buttons");
        Assert.DoesNotContain("limits", again.DisplayValue);
        Assert.DoesNotContain("limits", Classic.ToJsonString());
    }

    /// <summary>
    /// THE PRESET NAME IS SHOWN, NEVER OFFERED (D13). The reader has to know it to label an origin, so it
    /// hands it back; making it a row would be plan 02's registry work and would put a machine-identity
    /// decision on a settings page beside a notification toggle.
    /// </summary>
    [Fact]
    public void TheSnapshot_CarriesTheActivePresetName_AndNoReadingForThePresetKeyItself()
    {
        File.WriteAllText(_paths.ConfigFile, """{"preset":"quiet"}""");

        var (readings, presetName) = SettingsSnapshot_Reader.Read_All_FromDisk(_paths, session: null, log: null);

        Assert.Equal(Presets_Loader.QUIET, presetName);
        Assert.DoesNotContain(readings, reading => reading.Definition.Path == Presets_Loader.PRESET_KEY);
        Assert.Equal("from preset quiet", Reading(readings, "phone.receipts").OriginLabel);

        var (_, absentName) = SettingsSnapshot_Reader.Read_All_FromDisk(SupervisionPaths_Factory.Create(Path.Combine(_tempRoot, "empty")), session: null, log: null);
        Assert.Equal(Presets_Loader.CLASSIC, absentName);
    }

    /// <summary>
    /// A MISTYPED PRESET IS CLASSIC, AND SAYS SO — the loader's own rule (Resolve_Preset_OrClassic), called
    /// rather than copied: a second copy of "a mistyped preset word means classic" is the drift this reader
    /// exists to end.
    /// </summary>
    [Fact]
    public void AMistypedPreset_ReadsAsClassic_AndOneWarningNamesTheWord()
    {
        File.WriteAllText(_paths.ConfigFile, """{"preset":"quite"}""");
        var log = new RecordingLog();

        var (readings, presetName) = SettingsSnapshot_Reader.Read_All_FromDisk(_paths, session: null, log);

        Assert.Equal(Presets_Loader.CLASSIC, presetName);
        Assert.Equal("from preset classic", Reading(readings, "pulse.holdToggle").OriginLabel);
        Assert.Single(log.Warnings);
        Assert.Contains("quite", log.Warnings[0]);
    }

    /// <summary>
    /// A CONFIG FILE THAT WILL NOT PARSE READS AS AN EMPTY ONE, never as a throw. This reader is called by
    /// an HTTP GET that must answer and by a Telegram tap that must answer, and a hand-edit with a trailing
    /// comma is the ordinary way a config.json stops parsing. Never silent either (decision 21's corollary):
    /// one warning line names the file.
    /// </summary>
    [Fact]
    public void ACorruptConfigFile_ReadsAsNobodyHavingSaidAnything_AndDoesNotThrow()
    {
        File.WriteAllText(_paths.ConfigFile, """{"phone": {"receipts": "reactions",}, """);
        var log = new RecordingLog();

        var (readings, presetName) = SettingsSnapshot_Reader.Read_All_FromDisk(_paths, session: null, log);
        var nobodySaid = SettingsSnapshot_Reader.Read_All(configTree: null, Classic, Presets_Loader.CLASSIC, session: null);

        Assert.Equal(Presets_Loader.CLASSIC, presetName);
        Assert.Equal(Catalog.ALL.Count, readings.Count);
        Assert.DoesNotContain(readings, reading => reading.Origin == SettingOrigins.ConfigFile);
        Assert.Equal(nobodySaid.Select(reading => reading.DisplayValue), readings.Select(reading => reading.DisplayValue));
        Assert.Single(log.Warnings);
        Assert.Contains(_paths.ConfigFile, log.Warnings[0]);
    }

    /// <summary>
    /// A VALUE THE CATALOGUE ACCEPTS NEVER TAKES THE SNAPSHOT DOWN (fix round 1, 2026-09-23). highRiskPatterns
    /// has no validator, so a list holding a blank word resolves as the owner's own value — and reproduced
    /// against the built DLL, <c>{"highRiskPatterns":[""]}</c> made Read_All throw "has a blank display value"
    /// for every renderer at once. Each shape is read through the whole snapshot, from disk as a renderer
    /// would, and every reading in it must carry words.
    /// </summary>
    [Theory]
    [InlineData("""[""]""", "\"\"")]
    [InlineData("""["  "]""", "\"\"")]
    [InlineData("""["push", "", "deploy", " "]""", "push, \"\", deploy, \"\"")]
    public void AListWithABlankWord_StillReads_AndEveryDisplayCarriesWords(string patterns, string expected)
    {
        File.WriteAllText(_paths.ConfigFile, $$"""{"highRiskPatterns": {{patterns}}}""");

        var (readings, _) = SettingsSnapshot_Reader.Read_All_FromDisk(_paths, session: null, log: null);

        var reading = Reading(readings, "highRiskPatterns");

        Assert.Equal(SettingOrigins.ConfigFile, reading.Origin);
        Assert.Equal(expected, reading.DisplayValue);
        Assert.All(readings, each => Assert.False(string.IsNullOrWhiteSpace(each.DisplayValue), $"'{each.Definition.Path}' reads blank"));
    }

    [Fact]
    public void ReadOne_FindsARowByItsPath_OrItsLegacyPath_AndIsNullForAnUnknownOne()
    {
        var config = Tree("""{"supervisorModel":"sonnet"}""");

        var byPath = SettingsSnapshot_Reader.Read_One_OrNull("models.supervisor", config, Classic, Presets_Loader.CLASSIC, session: null);
        var byLegacy = SettingsSnapshot_Reader.Read_One_OrNull("supervisorModel", config, Classic, Presets_Loader.CLASSIC, session: null);

        Assert.NotNull(byPath);
        Assert.Equal("sonnet", byPath.DisplayValue);
        Assert.Equal("set here", byPath.OriginLabel);
        Assert.Same(byPath.Definition, byLegacy!.Definition);
        Assert.Null(SettingsSnapshot_Reader.Read_One_OrNull("no.such.setting", config, Classic, Presets_Loader.CLASSIC, session: null));
        Assert.Null(SettingsSnapshot_Reader.Read_One_OrNull(Presets_Loader.PRESET_KEY, config, Classic, Presets_Loader.CLASSIC, session: null));
    }

    /// <summary>The host's door (ruling P10): one row, straight off disk, with the same tolerance as the whole snapshot.</summary>
    [Fact]
    public void ReadOneFromDisk_ReadsTheSameRowTheSnapshotDoes()
    {
        File.WriteAllText(_paths.ConfigFile, """{"web":{"listen":"127.0.0.1:8080"}}""");

        var listen = SettingsSnapshot_Reader.Read_One_FromDisk_OrNull("web.listen", _paths, session: null, log: null);
        var (readings, _) = SettingsSnapshot_Reader.Read_All_FromDisk(_paths, session: null, log: null);

        Assert.NotNull(listen);
        Assert.Equal("127.0.0.1:8080", listen.DisplayValue);
        Assert.Equal(Reading(readings, "web.listen").OriginLabel, listen.OriginLabel);
        Assert.Null(SettingsSnapshot_Reader.Read_One_FromDisk_OrNull("no.such.setting", _paths, session: null, log: null));
    }

    /// <summary>Captures what the reader reported, so "one warning names the file" can be asserted.</summary>
    sealed class RecordingLog : IOrchestrationLog
    {
        public List<string> Warnings { get; } = [];

        public void Log_Info(string orchId, string message)
        {
        }

        public void Log_Warning(string orchId, string message)
        {
            Warnings.Add(message);
        }

        public void Log_Error(string orchId, string message, Exception? exception)
        {
        }

        public event Action<IOrchestrationLogEntry>? EntryLogged
        {
            add { }
            remove { }
        }
    }
}
