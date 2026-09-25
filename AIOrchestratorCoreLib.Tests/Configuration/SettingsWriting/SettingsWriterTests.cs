using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;
using AIOrchestratorCoreLib.Configuration.SettingsWriting;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsWriting;

/// <summary>
/// THE ONE WRITER OF A CATALOGUE PATH (plan 04 Task 2). The Telegram menu, the web PUT and the WPF window
/// all hand their edit here; the definition says yes or no and this class says where the bytes go.
///
/// <para>
/// EVERY "AFTER" IS READ BACK THROUGH THE RENDERERS' OWN READING (<see cref="SettingsSnapshot_Reader"/>),
/// not by poking at the JSON alone: a write that lands in the file under a spelling the resolver reads
/// behind another one is a write the owner is told was applied and never sees (ruling P12). The raw tree
/// is checked too, because "the key is gone" and "the key holds null" read the same through a resolver
/// that falls to a default either way.
/// </para>
/// <para>
/// NO COUNT IS A LITERAL, for the reason <c>SettingsSnapshotReaderTests</c> gives: the catalogue grows on a
/// sibling branch, so every "every other key" below walks <see cref="Catalog.ALL"/>.
/// </para>
/// </summary>
public class SettingsWriterTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public SettingsWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-settings-writer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    const string INTERVAL_PATH = "phone.status.intervalMinutes";

    JsonObject Read_Config()
    {
        return (JsonObject)JsonNode.Parse(File.ReadAllText(_paths.ConfigFile))!;
    }

    /// <summary>What every renderer would show for <paramref name="path"/> now — missing throws, so an assertion after it always measures something.</summary>
    ISettingReading Reading(string path)
    {
        return SettingsSnapshot_Reader.Read_One_FromDisk_OrNull(path, _paths, session: null, log: null)
            ?? throw new InvalidOperationException($"no reading for '{path}' — the test measures nothing");
    }

    /// <summary>A catalogue row by path — missing throws, so an assertion after it always measures something.</summary>
    static ISettingDefinition Definition(string path)
    {
        return Catalog.Find_OrNull(path)
            ?? throw new InvalidOperationException($"no catalogue row '{path}' — the test measures nothing");
    }

    /// <summary>
    /// THE DEFINITION SAYS YES OR NO, AND THIS CLASS SAYS WHERE THE BYTES GO (CLAUDE.md decision 21: hooks
    /// advise, the app enforces at the point of effect — and the point of effect for a setting is the write).
    /// Three renderers each carrying their own "is 240 a legal interval" is three answers to one question;
    /// the catalogue already knows, and its message is the one the owner sees.
    /// </summary>
    [Fact]
    public void AValidValue_IsWritten_AtItsCataloguePath()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");

        var result = Settings_Writer.Apply(_paths, INTERVAL_PATH, JsonValue.Create(45), log: null);

        Assert.Equal((SettingsWriteOutcomes.Applied, (string?)null), result);

        // NESTED, the shape config.json is written in — never the flat dotted key the presets use.
        var written = Read_Config();
        Assert.False(written.ContainsKey(INTERVAL_PATH));
        Assert.Equal(45, written["phone"]!["status"]!["intervalMinutes"]!.GetValue<int>());

        var reading = Reading(INTERVAL_PATH);
        Assert.Equal(SettingOrigins.ConfigFile, reading.Origin);
        Assert.Equal(45, reading.Value_OrNull!.GetValue<int>());
    }

    [Fact]
    public void AValueTheDefinitionRefuses_IsNotWritten_AndTheMessageIsTheCataloguesOwn()
    {
        const string original = """{"repos":[],"phone":{"status":{"intervalMinutes":45}}}""";
        File.WriteAllText(_paths.ConfigFile, original);

        var value = JsonValue.Create(240);
        var cataloguesMessage = Definition(INTERVAL_PATH).Validate_OrNull(value);

        // The refusal is real before the writer is asked, or this test would pin nothing.
        Assert.NotNull(cataloguesMessage);

        var result = Settings_Writer.Apply(_paths, INTERVAL_PATH, value, log: null);

        Assert.Equal(SettingsWriteOutcomes.RefusedInvalid, result.Outcome);
        Assert.Equal(cataloguesMessage, result.Message_OrNull);
        Assert.Equal(original, File.ReadAllText(_paths.ConfigFile));
    }

    /// <summary>
    /// THE ONE "DID IT TAKE EFFECT" (plan 04 Task 9 moved it here from the web handler, so the WPF window and the
    /// handler cannot classify an outcome two ways). Walked over every outcome, so a seventh must be classified.
    /// </summary>
    [Fact]
    public void TookEffect_IsTrueForAppliedAndResetOnly()
    {
        foreach (var outcome in Enum.GetValues<SettingsWriteOutcomes>())
            Assert.Equal(outcome is SettingsWriteOutcomes.Applied or SettingsWriteOutcomes.Reset, Settings_Writer.Took_Effect(outcome));
    }

    /// <summary>
    /// <c>preset</c> IS A REAL config.json KEY AND NOT A ROW (D13): the renderers show it in their header and
    /// never offer it, so the writer that serves them must not write it either.
    /// </summary>
    [Theory]
    [InlineData("phone.notARealSetting")]
    [InlineData("preset")]
    public void AnUnknownPath_IsRefused_WithoutTouchingTheFile(string path)
    {
        const string original = """{"repos":[],"preset":"classic"}""";
        File.WriteAllText(_paths.ConfigFile, original);

        var writes = 0;
        var results = Settings_Writer.Apply_Many(
            _paths,
            [(path, JsonValue.Create("quiet"))],
            log: null,
            (file, text) =>
            {
                writes++;
                Atomic_FileWriter.Write_AllText(file, text);
            });

        var (resultPath, outcome, message) = Assert.Single(results);
        Assert.Equal(path, resultPath);
        Assert.Equal(SettingsWriteOutcomes.RefusedUnknownPath, outcome);
        Assert.Contains($"'{path}'", message);
        Assert.Equal(0, writes);
        Assert.Equal(original, File.ReadAllText(_paths.ConfigFile));
    }

    /// <summary>
    /// THE OLD SPELLING FINDS ITS ROW, AND THE WRITE LANDS UNDER THE NEW ONE: a write under the old spelling
    /// would re-create the alias forever. The old key is removed in the same write — a flat
    /// <c>supervisorModel</c> left beside <c>models.supervisor</c> is a second answer the day the alias is
    /// retired. A NEIGHBOUR SHARING THE OLD BLOCK SURVIVES: <c>telegram.attachEntriesAbove</c> is another
    /// row's legacy spelling, and removing the one key must not take its parent object with it.
    /// </summary>
    [Fact]
    public void ALegacySpelling_ResolvesToItsDefinition_AndIsWrittenUnderTheNEWPath()
    {
        File.WriteAllText(
            _paths.ConfigFile,
            """{"repos":[],"supervisorModel":"haiku","telegram":{"foldLongEntriesAbove":900,"attachEntriesAbove":3}}""");

        Assert.Equal(SettingsWriteOutcomes.Applied, Settings_Writer.Apply(_paths, "supervisorModel", JsonValue.Create("sonnet"), log: null).Outcome);
        Assert.Equal(SettingsWriteOutcomes.Applied, Settings_Writer.Apply(_paths, "telegram.foldLongEntriesAbove", JsonValue.Create(1200), log: null).Outcome);

        var written = Read_Config();

        Assert.False(written.ContainsKey("supervisorModel"));
        Assert.Equal("sonnet", written["models"]!["supervisor"]!.GetValue<string>());

        Assert.False(written["telegram"]!.AsObject().ContainsKey("foldLongEntriesAbove"));
        Assert.Equal(1200, written["phone"]!["foldLongEntriesAbove"]!.GetValue<int>());
        Assert.Equal(3, written["telegram"]!["attachEntriesAbove"]!.GetValue<int>());

        Assert.Equal("sonnet", OrchestratorConfig_Loader.Load_OrEmpty(_paths).SupervisorModel);
        Assert.Equal(1200, OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramProse.FoldLongEntriesAbove);
    }

    /// <summary>
    /// A FLAT DOTTED KEY IS REMOVED BEFORE THE NESTED ONE IS WRITTEN (ruling P12). The resolver reads the
    /// literal whole-path key BEFORE the nested walk, and the presets are written flat — so an owner who
    /// copied a preset line into config.json has a key that shadows every write below it, and every write
    /// would read as applied while the screen never moved.
    /// </summary>
    [Fact]
    public void AFlatSpelledKey_IsReplaced_NotLeftShadowingTheWrite()
    {
        File.WriteAllText(_paths.ConfigFile, $$"""{"repos":[],"{{INTERVAL_PATH}}":45}""");

        Assert.Equal(SettingsWriteOutcomes.Applied, Settings_Writer.Apply(_paths, INTERVAL_PATH, JsonValue.Create(60), log: null).Outcome);

        var written = Read_Config();
        Assert.False(written.ContainsKey(INTERVAL_PATH));
        Assert.Equal(60, written["phone"]!["status"]!["intervalMinutes"]!.GetValue<int>());

        Assert.Equal(60, Reading(INTERVAL_PATH).Value_OrNull!.GetValue<int>());
    }

    /// <summary>
    /// JSON null IS A VALUE FOR THE DEFINITION TO JUDGE (ruling P3). For a row that accepts it — an effort
    /// level, where null means "no --effort flag" — it is WRITTEN, as a stated nothing that beats classic's
    /// high (xhigh until the owner's 2026-09-23 request). It is never turned into a Reset: a deleted key
    /// falls back to that very high.
    /// </summary>
    [Fact]
    public void ANull_ForARowThatAcceptsOne_IsWrittenAsAStatedNothing_NotADeletion()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");

        // classic states high here, so "null beats the preset" has something to beat.
        Assert.Equal("high", Reading("effort.supervisor").Value_OrNull!.GetValue<string>());

        Assert.Equal((SettingsWriteOutcomes.Applied, (string?)null), Settings_Writer.Apply(_paths, "effort.supervisor", value: null, log: null));

        var effort = Read_Config()["effort"]!.AsObject();
        Assert.True(effort.ContainsKey("supervisor"));
        Assert.Null(effort["supervisor"]);

        var reading = Reading("effort.supervisor");
        Assert.Equal(SettingOrigins.ConfigFile, reading.Origin);
        Assert.Null(reading.Value_OrNull);
    }

    [Fact]
    public void ANull_ForARowThatRefusesOne_IsRefused_WithTheCataloguesMessage()
    {
        const string original = """{"repos":[]}""";
        File.WriteAllText(_paths.ConfigFile, original);

        var cataloguesMessage = Definition(INTERVAL_PATH).Validate_OrNull(null);
        Assert.NotNull(cataloguesMessage);

        var result = Settings_Writer.Apply(_paths, INTERVAL_PATH, value: null, log: null);

        Assert.Equal((SettingsWriteOutcomes.RefusedInvalid, cataloguesMessage), result);
        Assert.Equal(original, File.ReadAllText(_paths.ConfigFile));
    }

    /// <summary>
    /// A ReadOnly ROW IS REFUSED BY ONE RULE READ OFF THE CATALOGUE, never by a list of paths kept here.
    /// repos, planBackend and the three session.* state rows are all ReadOnly for different reasons and the
    /// writer needs to know none of them — a second list would be a place for the fifth one to be forgotten.
    ///
    /// <para>
    /// EACH VALUE IS ONE THE DEFINITION WOULD ACCEPT, asserted first, so the refusal can only be the
    /// ReadOnly rule's — a value refused as invalid would turn this green by the other route (decision 20).
    /// The "where it is changed instead" is the row's own Description, whose place fragments
    /// <c>SettingsSnapshotReaderTests.AReadOnlyRow_IsNotEditable_AndSaysWhereItIsChangedInstead</c> pins.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("repos", """[]""")]
    [InlineData("planBackend", """{"kind":"plan-md"}""")]
    [InlineData("session.paused", "true")]
    [InlineData("session.telegramMode", "\"Deferred\"")]
    [InlineData("session.ownerPresence", "\"Terminal\"")]
    public void AReadOnlyRow_IsRefused_AndSaysWhereItIsChangedInstead(string path, string valueJson)
    {
        const string original = """{"repos":[{"name":"Arb Studio","path":"/repos/arb"}]}""";
        File.WriteAllText(_paths.ConfigFile, original);

        var definition = Definition(path);
        var value = JsonNode.Parse(valueJson);

        Assert.Equal(SettingRenderers.ReadOnly, definition.Renderer);
        Assert.Null(definition.Validate_OrNull(value));

        var result = Settings_Writer.Apply(_paths, path, value, log: null);

        Assert.Equal(SettingsWriteOutcomes.RefusedReadOnly, result.Outcome);
        Assert.Contains(definition.Description, result.Message_OrNull);
        Assert.Equal(original, File.ReadAllText(_paths.ConfigFile));
    }

    /// <summary>
    /// RESET DELETES THE KEY, IT DOES NOT WRITE THE DEFAULT (spec §6.2). A materialised default is a default
    /// that can never move again, frozen on the first button press — the rule the loader already keeps for
    /// reviewerModel, now enforced for every catalogue row.
    /// </summary>
    [Fact]
    public void Reset_DeletesTheKey_AndTheValueFallsBackToThePresetOrTheShippedDefault()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"effort":{"supervisor":"low"},"phone":{"status":{"intervalMinutes":45}}}""");

        // classic states effort.supervisor and not the interval — one row falls to each lower layer.
        var classic = Presets_Loader.Load_Embedded(Presets_Loader.CLASSIC);
        Assert.NotNull(classic["effort.supervisor"]);
        Assert.False(classic.ContainsKey(INTERVAL_PATH));

        Assert.Equal((SettingsWriteOutcomes.Reset, (string?)null), Settings_Writer.Reset(_paths, "effort.supervisor", log: null));
        Assert.Equal((SettingsWriteOutcomes.Reset, (string?)null), Settings_Writer.Reset(_paths, INTERVAL_PATH, log: null));

        // DELETED, not nulled: a JSON null is a stated nothing (ruling P3), which Reset must never write.
        var written = Read_Config();
        Assert.False(written["effort"]!.AsObject().ContainsKey("supervisor"));
        Assert.False(written["phone"]!["status"]!.AsObject().ContainsKey("intervalMinutes"));

        var effort = Reading("effort.supervisor");
        Assert.Equal(SettingOrigins.Preset, effort.Origin);
        Assert.True(JsonNode.DeepEquals(classic["effort.supervisor"], effort.Value_OrNull));

        var interval = Reading(INTERVAL_PATH);
        Assert.Equal(SettingOrigins.ShippedDefault, interval.Origin);
        Assert.True(JsonNode.DeepEquals(Definition(INTERVAL_PATH).Default_OrNull, interval.Value_OrNull));
    }

    /// <summary>
    /// RESET REMOVES EVERY SPELLING THE RESOLVER WOULD READ — the flat dotted key (P12), the nested key and
    /// the legacy one. Leaving the legacy key would make Reset a no-op on exactly the machines that most
    /// need it: every config.json written before the catalogue spells its models the old way.
    /// </summary>
    [Fact]
    public void Reset_RemovesTheFlatAndTheLegacySpellingToo_OrItIsANoOpWhereItIsMostNeeded()
    {
        File.WriteAllText(
            _paths.ConfigFile,
            """{"repos":[],"supervisorModel":"haiku","models.supervisor":"sonnet","models":{"supervisor":"fable"}}""");

        Assert.Equal(SettingsWriteOutcomes.Reset, Settings_Writer.Reset(_paths, "models.supervisor", log: null).Outcome);

        var written = Read_Config();
        Assert.False(written.ContainsKey("supervisorModel"));
        Assert.False(written.ContainsKey("models.supervisor"));
        Assert.False(written["models"]!.AsObject().ContainsKey("supervisor"));

        Assert.Equal(SettingOrigins.ShippedDefault, Reading("models.supervisor").Origin);
    }

    [Fact]
    public void Reset_OnAKeyThatWasNeverSet_IsHarmless_AndSaysSo()
    {
        const string original = """{"repos":[],"telegramInbound":"off"}""";
        File.WriteAllText(_paths.ConfigFile, original);

        var result = Settings_Writer.Reset(_paths, INTERVAL_PATH, log: null);

        Assert.Equal(SettingsWriteOutcomes.Reset, result.Outcome);
        Assert.Contains($"'{INTERVAL_PATH}'", result.Message_OrNull);
        Assert.Contains("nothing to reset", result.Message_OrNull);

        // Harmless means the file is not even rewritten, byte for byte.
        Assert.Equal(original, File.ReadAllText(_paths.ConfigFile));
    }

    /// <summary>
    /// RESET OF A ReadOnly ROW IS REFUSED BY THE SAME RULE AS A WRITE: a Reset of <c>repos</c> would delete
    /// the owner's repository list outright, which is a write by another name.
    /// </summary>
    [Theory]
    [InlineData("repos", SettingsWriteOutcomes.RefusedReadOnly)]
    [InlineData("session.paused", SettingsWriteOutcomes.RefusedReadOnly)]
    [InlineData("phone.notARealSetting", SettingsWriteOutcomes.RefusedUnknownPath)]
    public void Reset_OfARowItMayNotWrite_IsRefused_AndTheFileIsUntouched(string path, SettingsWriteOutcomes expected)
    {
        const string original = """{"repos":[{"name":"Arb Studio","path":"/repos/arb"}],"session":{"paused":true},"phone":{"notARealSetting":1}}""";
        File.WriteAllText(_paths.ConfigFile, original);

        var result = Settings_Writer.Reset(_paths, path, log: null);

        Assert.Equal(expected, result.Outcome);
        Assert.Contains($"'{path}'", result.Message_OrNull);
        Assert.Equal(original, File.ReadAllText(_paths.ConfigFile));
    }

    /// <summary>
    /// UNKNOWN KEYS SURVIVE, which is the property spec §8 was actually asking for when it said "write
    /// through the fork's merging Save" (D6). Agents edit config.json at runtime; a settings write that
    /// dropped planBackend would be the defect Save's own docstring records being fixed twice.
    /// </summary>
    [Fact]
    public void AHandEditedKeyThisBuildDoesNotKnow_SurvivesAWrite()
    {
        File.WriteAllText(_paths.ConfigFile, """
            {
              "repos": [],
              "planBackend": { "kind": "external", "assembly": "/opt/adapters/Adapter.dll", "type": "Adapter.PlanBackend" },
              "somethingNobodyHereKnowsAbout": { "nested": [1, 2, 3], "flag": true }
            }
            """);
        var before = Read_Config();

        Settings_Writer.Apply(_paths, INTERVAL_PATH, JsonValue.Create(45), log: null);

        var after = Read_Config();
        Assert.True(JsonNode.DeepEquals(before["planBackend"], after["planBackend"]));
        Assert.True(JsonNode.DeepEquals(before["somethingNobodyHereKnowsAbout"], after["somethingNobodyHereKnowsAbout"]));
        Assert.Equal("Adapter.PlanBackend", OrchestratorConfig_Loader.Load_OrEmpty(_paths).PlanBackend!.Value.TypeName);
    }

    /// <summary>
    /// A WRITE TO ONE ROW MOVES THAT ROW AND NOTHING ELSE. The file states every editable row at its own
    /// default (so each reads as the owner's), plus a repo list and a plan backend; after one write, every
    /// other row resolves to exactly what it did, from exactly where it did, and the file minus the one
    /// row is the file it was.
    /// </summary>
    [Fact]
    public void EveryOtherCatalogueKey_IsUntouchedByAWriteToOne()
    {
        var seeded = new JsonObject
        {
            ["repos"] = new JsonArray(new JsonObject { ["name"] = "Arb Studio", ["path"] = "/repos/arb" }),
            ["planBackend"] = new JsonObject { ["kind"] = "plan-md" },
        };

        foreach (var definition in Catalog.ALL.Where(definition => definition.Renderer != SettingRenderers.ReadOnly))
            SettingsJson_Path.Write(seeded, definition.Path, definition.Default_OrNull);

        File.WriteAllText(_paths.ConfigFile, seeded.ToJsonString(JsonWriting.INDENTED));

        var before = Catalog.ALL.ToDictionary(definition => definition.Path, definition => Reading(definition.Path));

        Assert.Equal(SettingsWriteOutcomes.Applied, Settings_Writer.Apply(_paths, INTERVAL_PATH, JsonValue.Create(45), log: null).Outcome);

        foreach (var definition in Catalog.ALL.Where(definition => definition.Path != INTERVAL_PATH))
        {
            var after = Reading(definition.Path);

            Assert.True(before[definition.Path].Origin == after.Origin, $"'{definition.Path}' moved from {before[definition.Path].Origin} to {after.Origin}");
            Assert.True(JsonNode.DeepEquals(before[definition.Path].Value_OrNull, after.Value_OrNull), $"'{definition.Path}' changed value");
        }

        var expected = (JsonObject)seeded.DeepClone();
        var actual = Read_Config();
        SettingsJson_Path.Remove(expected, INTERVAL_PATH);
        SettingsJson_Path.Remove(actual, INTERVAL_PATH);

        Assert.True(JsonNode.DeepEquals(expected, actual), "the file changed somewhere other than the one row written");
    }

    /// <summary>
    /// ATOMIC, AND NEVER A ZERO-LENGTH config.json — Atomic_FileWriter's own reason, which a settings page
    /// reaches far more often than the Settings window ever did. What is observable from outside is the
    /// debris half: a rename-based write leaves no temp file, and the target is a whole document.
    /// </summary>
    [Fact]
    public void TheWrite_GoesThroughTheAtomicWriter_AndLeavesNoTempFileBehind()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");

        Settings_Writer.Apply(_paths, INTERVAL_PATH, JsonValue.Create(45), log: null);
        Settings_Writer.Reset(_paths, INTERVAL_PATH, log: null);
        Settings_Writer.Apply_Many(_paths, [(INTERVAL_PATH, JsonValue.Create(50)), ("telegramInbound", JsonValue.Create("off"))], log: null);

        Assert.Empty(Directory.GetFiles(_tempRoot, $"*{Atomic_FileWriter.TEMP_FILE_SUFFIX}"));
        Assert.Equal(50, Read_Config()["phone"]!["status"]!["intervalMinutes"]!.GetValue<int>());
    }

    // ---------------------------------------------------------------------------------------
    // THE THREE STATES OF config.json (plan 04 Task 2b, ruling P33)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// What an owner's config.json looks like: the keys a wipe would cost them — the repo list, the two chat
    /// ids, a key only a hand put there — beside one row a write will touch.
    /// </summary>
    const string OWNERS_CONFIG = """
        {
          "repos": [ { "name": "Arb Studio", "path": "/repos/arb" } ],
          "telegramSupergroupChatId": -1001234567890,
          "telegramOwnerUserId": 42,
          "phone": { "status": { "intervalMinutes": 30 } },
          "somethingAHandAdded": { "kept": true }
        }
        """;

    /// <summary>
    /// Every way a file can be PRESENT, READ and still not be a tree this writer may edit: half-typed, empty
    /// (an editor that truncates before it writes looks exactly like this for a moment), valid JSON whose top
    /// level is not an object, and a key stated twice — two answers to one question, and a rewrite would have
    /// to pick one of them for the owner.
    /// </summary>
    public static TheoryData<string> UnparsableConfigTexts => new()
    {
        "{not json at all",
        """{ "repos": [ { "name": "half-typed" """,
        "",
        "  \r\n  ",
        "[1, 2]",
        "null",
        """{ "repos": [], "repos": [ { "name": "Arb Studio", "path": "/repos/arb" } ] }""",
    };

    /// <summary>
    /// A config.json THAT DOES NOT PARSE IS NEVER OVERWRITTEN (P33 state 2). Until 2026-09-23 the writer read it
    /// as empty and wrote a file holding only the edited key — a phone tap erased the owner's repos, chat ids
    /// and every hand-edited key. The owner may be mid-edit; a hand fix loses less than a rewrite. Refused as
    /// WriteFailed, with a message that names the file and says it does not parse — the words that tell this
    /// route from the in-use one below (decision 20: a state with two routes to it pins neither) — and ONE
    /// warning line for the log (decision 15: not Telegram).
    /// </summary>
    [Theory]
    [MemberData(nameof(UnparsableConfigTexts))]
    public void AnUnparsableConfigFile_IsNotOverwritten_AndTheWriteSaysWhy(string text)
    {
        File.WriteAllText(_paths.ConfigFile, text);
        var before = File.ReadAllBytes(_paths.ConfigFile);
        var log = new RecordingLog();

        var result = Settings_Writer.Apply(_paths, INTERVAL_PATH, JsonValue.Create(45), log);

        Assert.Equal(SettingsWriteOutcomes.WriteFailed, result.Outcome);
        Assert.Contains(_paths.ConfigFile, result.Message_OrNull);
        Assert.Contains("does not parse", result.Message_OrNull);
        Assert.Equal(before, File.ReadAllBytes(_paths.ConfigFile));

        var warning = Assert.Single(log.Warnings);
        Assert.Contains(_paths.ConfigFile, warning);
    }

    /// <summary>
    /// A config.json ANOTHER PROGRAM HOLDS IS NEVER OVERWRITTEN (P33 state 3), and the edit lands once it is let
    /// go. On Windows a sharing violation is routine — the app's own Save, the repo reorderer, the daemon, an
    /// editor, an antivirus scan — and before 2026-09-23 it read as "empty" and the owner's file was replaced by
    /// the edited key alone. The read goes through <c>Tolerant_FileReader</c>, which outlasts a rename; a holder
    /// that outlasts IT is refused, and nothing is written.
    ///
    /// <para>
    /// THE HOLD IS <see cref="FileShare.None"/> ON THE TEST THREAD. Windows refuses the read through its share
    /// modes; .NET on Linux and macOS emulates FileShare.None with an advisory flock that the reader's own open
    /// runs into, so this is expected to RUN on both CI operating systems — it skips, by name, only where the
    /// probe finds that emulation off (<see cref="RequiresExclusiveOpenEnforcementFactAttribute"/>).
    /// </para>
    /// </summary>
    [RequiresExclusiveOpenEnforcementFact]
    public void AConfigFileHeldOpenExclusively_IsNotOverwritten()
    {
        File.WriteAllText(_paths.ConfigFile, OWNERS_CONFIG);
        var before = File.ReadAllBytes(_paths.ConfigFile);
        var log = new RecordingLog();

        (SettingsWriteOutcomes Outcome, string? Message_OrNull) refused;

        using (new FileStream(_paths.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.None))
            refused = Settings_Writer.Apply(_paths, INTERVAL_PATH, JsonValue.Create(45), log);

        Assert.Equal(SettingsWriteOutcomes.WriteFailed, refused.Outcome);
        Assert.Contains(_paths.ConfigFile, refused.Message_OrNull);
        Assert.Contains("could not be read", refused.Message_OrNull);
        Assert.Equal(before, File.ReadAllBytes(_paths.ConfigFile));
        Assert.Contains(_paths.ConfigFile, Assert.Single(log.Warnings));

        // Let go: the same edit lands, and every other key of the owner's file is still there.
        Assert.Equal((SettingsWriteOutcomes.Applied, (string?)null), Settings_Writer.Apply(_paths, INTERVAL_PATH, JsonValue.Create(45), log));
        Assert.Equal(45, Read_Config()["phone"]!["status"]!["intervalMinutes"]!.GetValue<int>());
        Assert_EveryKeyButTheIntervalIsTheOwners();
        Assert.Single(log.Warnings);
    }

    /// <summary>
    /// THE WIPE ITSELF, reproduced deterministically: a holder that lets go BETWEEN the read and the write — the
    /// ordinary shape of a sharing violation (an antivirus scan, the other host's Save), and the one a hold that
    /// lasts the whole call cannot show, because on Windows the rename then fails too and the write throws.
    /// The counted writer releases the hold just before it writes. Before 2026-09-23 the read failed, was taken
    /// for "empty", the hold was gone by the rename, and config.json was replaced by the one edited key; now the
    /// read's refusal means the writer is never called at all.
    /// </summary>
    [RequiresExclusiveOpenEnforcementFact]
    public void AConfigFileHeldOnlyDuringTheRead_IsNotReplacedByTheEditedKeyAlone()
    {
        File.WriteAllText(_paths.ConfigFile, OWNERS_CONFIG);
        var before = File.ReadAllBytes(_paths.ConfigFile);
        var hold = new FileStream(_paths.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.None);
        var writes = 0;
        IReadOnlyList<(string Path, SettingsWriteOutcomes Outcome, string? Message_OrNull)> results;

        try
        {
            results = Settings_Writer.Apply_Many(
                _paths,
                [(INTERVAL_PATH, JsonValue.Create(45))],
                log: null,
                (file, text) =>
                {
                    writes++;
                    hold.Dispose();
                    Atomic_FileWriter.Write_AllText(file, text);
                });
        }
        finally
        {
            hold.Dispose();
        }

        // The file first, as text, so a regression shows WHAT replaced the owner's config rather than a byte dump.
        Assert.Equal(OWNERS_CONFIG, File.ReadAllText(_paths.ConfigFile));
        Assert.Equal(before, File.ReadAllBytes(_paths.ConfigFile));
        Assert.Equal(0, writes);
        Assert.Equal(SettingsWriteOutcomes.WriteFailed, Assert.Single(results).Outcome);
    }

    /// <summary>
    /// A MISSING config.json IS THE ONE STATE THAT STARTS FROM AN EMPTY TREE (P33 state 1, today's behaviour,
    /// pinned): there is nothing on disk to lose, so the file is created holding the edited key and nothing
    /// else — no materialised default beside it — and there is nothing to warn about.
    /// </summary>
    [Fact]
    public void AMissingConfigFile_IsCreatedWithTheEditedKeyOnly()
    {
        Assert.False(File.Exists(_paths.ConfigFile));
        var log = new RecordingLog();

        var result = Settings_Writer.Apply(_paths, INTERVAL_PATH, JsonValue.Create(45), log);

        Assert.Equal((SettingsWriteOutcomes.Applied, (string?)null), result);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse("""{"phone":{"status":{"intervalMinutes":45}}}"""), Read_Config()),
            $"a missing config.json was created as: {File.ReadAllText(_paths.ConfigFile)}");
        Assert.Empty(log.Warnings);
    }

    /// <summary>A Reset with no config.json at all has nothing to delete: it says so and creates no file.</summary>
    [Fact]
    public void Reset_OfAMissingConfigFile_IsHarmless_AndCreatesNothing()
    {
        var log = new RecordingLog();

        var result = Settings_Writer.Reset(_paths, INTERVAL_PATH, log);

        Assert.Equal(SettingsWriteOutcomes.Reset, result.Outcome);
        Assert.Contains("nothing to reset", result.Message_OrNull);
        Assert.False(File.Exists(_paths.ConfigFile));
        Assert.Empty(log.Warnings);
    }

    /// <summary>
    /// RESET REFUSES AN UNPARSABLE FILE TOO — it rewrites config.json exactly as a write does. Before
    /// 2026-09-23 it read the file as empty, found "nothing to reset" and said the row "already reads its
    /// preset or shipped default": a confident claim about a file it had not understood.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnparsableConfigTexts))]
    public void Reset_OverAnUnparsableConfigFile_IsRefused_AndTheFileIsUntouched(string text)
    {
        File.WriteAllText(_paths.ConfigFile, text);
        var before = File.ReadAllBytes(_paths.ConfigFile);
        var log = new RecordingLog();

        var result = Settings_Writer.Reset(_paths, INTERVAL_PATH, log);

        Assert.Equal(SettingsWriteOutcomes.WriteFailed, result.Outcome);
        Assert.Contains(_paths.ConfigFile, result.Message_OrNull);
        Assert.Contains("does not parse", result.Message_OrNull);
        Assert.Equal(before, File.ReadAllBytes(_paths.ConfigFile));
        Assert.Contains(_paths.ConfigFile, Assert.Single(log.Warnings));
    }

    /// <summary>
    /// RESET REFUSES A FILE ANOTHER PROGRAM HOLDS, and resets once it is let go. The row IS set in the file, so
    /// a Reset that got through would have something to delete — the refusal cannot be the harmless
    /// "nothing to reset" answer by another route.
    /// </summary>
    [RequiresExclusiveOpenEnforcementFact]
    public void Reset_OfAConfigFileHeldOpenExclusively_IsRefused_AndTheFileIsUntouched()
    {
        File.WriteAllText(_paths.ConfigFile, OWNERS_CONFIG);
        var before = File.ReadAllBytes(_paths.ConfigFile);
        var log = new RecordingLog();

        (SettingsWriteOutcomes Outcome, string? Message_OrNull) refused;

        using (new FileStream(_paths.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.None))
            refused = Settings_Writer.Reset(_paths, INTERVAL_PATH, log);

        Assert.Equal(SettingsWriteOutcomes.WriteFailed, refused.Outcome);
        Assert.Contains(_paths.ConfigFile, refused.Message_OrNull);
        Assert.Contains("could not be read", refused.Message_OrNull);
        Assert.Equal(before, File.ReadAllBytes(_paths.ConfigFile));
        Assert.Contains(_paths.ConfigFile, Assert.Single(log.Warnings));

        Assert.Equal((SettingsWriteOutcomes.Reset, (string?)null), Settings_Writer.Reset(_paths, INTERVAL_PATH, log));
        Assert.False(Read_Config()["phone"]!["status"]!.AsObject().ContainsKey("intervalMinutes"));
        Assert_EveryKeyButTheIntervalIsTheOwners();
    }

    /// <summary>
    /// ONE REFUSED READ REFUSES THE WHOLE BODY, AND IS ONE LOG LINE — not one per edit. Every edit the catalogue
    /// accepted answers WriteFailed with the same reason; the edits refused on their own merits keep their own
    /// answer (the handler's "nothing in this request was applied" rests on the writer writing nothing at all,
    /// which the counted writer proves).
    /// </summary>
    [Fact]
    public void ApplyMany_OverAnUnparsableConfigFile_WritesNothing_AndEveryAcceptedEditSaysWhy()
    {
        File.WriteAllText(_paths.ConfigFile, "{not json at all");
        var log = new RecordingLog();
        var writes = 0;

        var results = Settings_Writer.Apply_Many(
            _paths,
            [
                (INTERVAL_PATH, JsonValue.Create(45)),
                ("phone.notARealSetting", JsonValue.Create(true)),
                ("telegramInbound", JsonValue.Create("off")),
            ],
            log,
            (file, text) =>
            {
                writes++;
                Atomic_FileWriter.Write_AllText(file, text);
            });

        Assert.Equal(0, writes);
        Assert.Equal(
            [
                (INTERVAL_PATH, SettingsWriteOutcomes.WriteFailed),
                ("phone.notARealSetting", SettingsWriteOutcomes.RefusedUnknownPath),
                ("telegramInbound", SettingsWriteOutcomes.WriteFailed),
            ],
            results.Select(result => (result.Path, result.Outcome)));
        Assert.Equal(results[0].Message_OrNull, results[2].Message_OrNull);
        Assert.Contains("does not parse", results[0].Message_OrNull);
        Assert.Single(log.Warnings);
    }

    /// <summary>The file now, minus the interval row, is <see cref="OWNERS_CONFIG"/> minus the interval row.</summary>
    void Assert_EveryKeyButTheIntervalIsTheOwners()
    {
        var expected = JsonNode.Parse(OWNERS_CONFIG)!.AsObject();
        var actual = Read_Config();
        SettingsJson_Path.Remove(expected, INTERVAL_PATH);
        SettingsJson_Path.Remove(actual, INTERVAL_PATH);

        Assert.True(JsonNode.DeepEquals(expected, actual), $"the owner's other keys did not survive: {actual.ToJsonString()}");
    }

    /// <summary>
    /// Apply_Many IS ONE READ AND ONE WRITE. A PUT body of twelve settings must not be twelve
    /// read-modify-write cycles: eleven of them would be racing the other ten. The write is counted through
    /// the overload that takes the file writer as a parameter — the only way "once" is observable.
    /// </summary>
    [Fact]
    public void ApplyMany_WritesOnce_AndReportsPerPath()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"supervisorModel":"haiku"}""");
        List<string> writes = [];

        var results = Settings_Writer.Apply_Many(
            _paths,
            [
                (INTERVAL_PATH, JsonValue.Create(45)),
                ("telegramInbound", JsonValue.Create("off")),
                ("supervisorModel", JsonValue.Create("sonnet")),
            ],
            log: null,
            (file, text) =>
            {
                writes.Add(file);
                Atomic_FileWriter.Write_AllText(file, text);
            });

        Assert.Equal([_paths.ConfigFile], writes);

        // PER PATH, AS THE CALLER SPELLED IT — the legacy word included — so a PUT handler can answer each
        // key of its own body without re-resolving it.
        Assert.Equal([INTERVAL_PATH, "telegramInbound", "supervisorModel"], results.Select(result => result.Path));
        Assert.All(results, result => Assert.Equal((SettingsWriteOutcomes.Applied, (string?)null), (result.Outcome, result.Message_OrNull)));

        Assert.Equal(45, Reading(INTERVAL_PATH).Value_OrNull!.GetValue<int>());
        Assert.Equal("off", Reading("telegramInbound").Value_OrNull!.GetValue<string>());
        Assert.Equal("sonnet", Reading("models.supervisor").Value_OrNull!.GetValue<string>());
    }

    [Fact]
    public void ApplyMany_WithOneInvalidPath_AppliesTheRest_AndNamesTheOneItRefused()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");
        var writes = 0;

        var tooShort = JsonValue.Create(0);
        var cataloguesMessage = Definition("highRiskCodeExpiryMinutes").Validate_OrNull(tooShort);
        Assert.NotNull(cataloguesMessage);

        var results = Settings_Writer.Apply_Many(
            _paths,
            [
                (INTERVAL_PATH, JsonValue.Create(45)),
                ("highRiskCodeExpiryMinutes", tooShort),
                ("telegramInbound", JsonValue.Create("off")),
                ("phone.notARealSetting", JsonValue.Create(true)),
            ],
            log: null,
            (file, text) =>
            {
                writes++;
                Atomic_FileWriter.Write_AllText(file, text);
            });

        Assert.Equal(1, writes);
        Assert.Equal(
            [
                (INTERVAL_PATH, SettingsWriteOutcomes.Applied),
                ("highRiskCodeExpiryMinutes", SettingsWriteOutcomes.RefusedInvalid),
                ("telegramInbound", SettingsWriteOutcomes.Applied),
                ("phone.notARealSetting", SettingsWriteOutcomes.RefusedUnknownPath),
            ],
            results.Select(result => (result.Path, result.Outcome)));
        Assert.Equal(cataloguesMessage, results[1].Message_OrNull);

        var written = Read_Config();
        Assert.Equal(45, written["phone"]!["status"]!["intervalMinutes"]!.GetValue<int>());
        Assert.Equal("off", written["telegramInbound"]!.GetValue<string>());
        Assert.False(written.ContainsKey("highRiskCodeExpiryMinutes"));
        Assert.False(written["phone"]!.AsObject().ContainsKey("notARealSetting"));
    }

    /// <summary>Captures what the writer reported, so "one warning line names the file" can be asserted.</summary>
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
