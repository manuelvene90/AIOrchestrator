using System.Globalization;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Web;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Web;

/// <summary>
/// THE WEB PAGE'S WHOLE API, WITH NO SOCKET IN SIGHT (plan 04 Task 6). Everything that decides anything
/// about a request happens in <see cref="SettingsRequest_Handler"/>; the listener of Task 7 only moves bytes,
/// so this is where the web renderer is covered as well as the Telegram one.
///
/// <para>
/// EVERY EXPECTATION IS READ OFF THE SHARED READING (<see cref="SettingsSnapshot_Reader"/>) or the catalogue
/// itself, never retyped: the handler serialises the reading every renderer draws, and a test holding its own
/// idea of what "from preset classic" says would be the second copy decision 12 forbids.
/// </para>
/// <para>
/// NO COUNT IS A LITERAL: the catalogue grows on a sibling branch, so "every row" walks <see cref="Catalog.ALL"/>.
/// </para>
/// </summary>
public class SettingsRequestHandlerTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public SettingsRequestHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-settings-handler-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
    }

    const string INTERVAL_PATH = "phone.status.intervalMinutes";
    const string SCREENSHOTS_PATH = "telegramStatusScreenshots";
    const string SUPERVISOR_EFFORT_PATH = "effort.supervisor";
    const string BUTTON_EXPIRY_PATH = "buttonExpiryMinutes";
    const string CHAT_ID_PATH = "telegramSupergroupChatId";
    const string JSON_CONTENT_TYPE = "application/json; charset=utf-8";

    static readonly Func<string, string?> NO_HEADERS = _ => null;

    static Func<string, string?> Header_Carrying(string token)
    {
        return name => string.Equals(name, SettingsRequest_Handler.TOKEN_HEADER, StringComparison.OrdinalIgnoreCase) ? token : null;
    }

    (int Status, string ContentType, string Body) Get(string configuredToken = "")
    {
        return SettingsRequest_Handler.Handle("GET", SettingsRequest_Handler.SETTINGS_PATH, NO_HEADERS, "", _paths, configuredToken, log: null);
    }

    (int Status, string ContentType, string Body) Put(string body, string configuredToken = "", Func<string, string?>? header = null, IOrchestrationLog? log = null)
    {
        return SettingsRequest_Handler.Handle("PUT", SettingsRequest_Handler.SETTINGS_PATH, header ?? NO_HEADERS, body, _paths, configuredToken, log);
    }

    (int Status, string ContentType, string Body) Delete(string target, string configuredToken = "", Func<string, string?>? header = null)
    {
        return SettingsRequest_Handler.Handle("DELETE", target, header ?? NO_HEADERS, "", _paths, configuredToken, log: null);
    }

    static string Reset_Target(string settingPath)
    {
        return $"{SettingsRequest_Handler.SETTINGS_PATH}?path={Uri.EscapeDataString(settingPath)}";
    }

    static JsonObject Json(string body)
    {
        return JsonNode.Parse(body) as JsonObject
            ?? throw new InvalidOperationException($"the body is not a JSON object — the test measures nothing: {body}");
    }

    string Config_Text()
    {
        return File.ReadAllText(_paths.ConfigFile);
    }

    void Write_Config(string json)
    {
        File.WriteAllText(_paths.ConfigFile, json);
    }

    /// <summary>What every renderer would show for <paramref name="path"/> now — missing throws, so an assertion after it always measures something.</summary>
    ISettingReading Reading(string path)
    {
        return SettingsSnapshot_Reader.Read_One_FromDisk_OrNull(path, _paths, session: null, log: null)
            ?? throw new InvalidOperationException($"no reading for '{path}' — the test measures nothing");
    }

    static ISettingDefinition Definition(string path)
    {
        return Catalog.Find_OrNull(path)
            ?? throw new InvalidOperationException($"no catalogue row '{path}' — the test measures nothing");
    }

    static IReadOnlyList<JsonObject> Rows(JsonObject getBody)
    {
        return getBody["sections"]!.AsArray()
            .SelectMany(section => section!["rows"]!.AsArray())
            .Select(row => row!.AsObject())
            .ToArray();
    }

    static JsonObject Row(JsonObject getBody, string path)
    {
        return Rows(getBody).SingleOrDefault(row => (string?)row["path"] == path)
            ?? throw new InvalidOperationException($"no row '{path}' in the GET body — the test measures nothing");
    }

    /// <summary>The one result of a single-path PUT or DELETE.</summary>
    static JsonObject Single_Result(string body)
    {
        return Json(body)["results"]!.AsArray().Single()!.AsObject();
    }

    static IEnumerable<string> All_Strings(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    yield return key;

                    foreach (var text in All_Strings(value))
                        yield return text;
                }

                break;

            case JsonArray array:
                foreach (var element in array)
                {
                    foreach (var text in All_Strings(element))
                        yield return text;
                }

                break;

            case JsonValue value when value.TryGetValue<string>(out var text):
                yield return text;
                break;
        }
    }

    // ---------------------------------------------------------------------------------------
    // GET /settings
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// GET /settings HANDS THE BROWSER THE CATALOGUE ITSELF (spec §8.3, and SettingValidators' own doc says
    /// this is why a validator is a NAME rather than a delegate). The page then renders from data, which is
    /// what makes "a new catalogue entry appears in all three renderers with no UI code" true rather than
    /// aspirational.
    /// </summary>
    [Fact]
    public void Get_CarriesEveryCatalogueEntry_WithItsValueOriginLabelDescriptionRendererAndRestart()
    {
        Write_Config("""{"phone":{"status":{"intervalMinutes":45}}}""");

        var (status, contentType, body) = Get();

        Assert.Equal(200, status);
        Assert.Equal(JSON_CONTENT_TYPE, contentType);

        var json = Json(body);
        var rows = Rows(json);

        // Every row once, and only rows — walked off the catalogue, never counted by hand.
        Assert.Equal(Catalog.ALL.Count, rows.Count);
        Assert.Equal(
            Catalog.ALL.Select(definition => definition.Path).OrderBy(path => path, StringComparer.Ordinal),
            rows.Select(row => (string)row["path"]!).OrderBy(path => path, StringComparer.Ordinal));

        var (readings, _) = SettingsSnapshot_Reader.Read_All_FromDisk(_paths, session: null, log: null);

        foreach (var reading in readings)
        {
            var definition = reading.Definition;
            var row = Row(json, definition.Path);

            Assert.Equal(definition.Label, (string?)row["label"]);
            Assert.Equal(definition.Description, (string?)row["description"]);
            Assert.Equal(definition.Kind.ToString(), (string?)row["kind"]);
            Assert.Equal(definition.Renderer.ToString(), (string?)row["renderer"]);
            Assert.Equal(definition.Scope.ToString(), (string?)row["scope"]);
            Assert.Equal(definition.Restart.ToString(), (string?)row["restart"]);
            Assert.Equal(definition.Minimum, (int?)row["minimum"]);
            Assert.Equal(definition.Maximum, (int?)row["maximum"]);

            Assert.Equal(reading.RestartLabel, (string?)row["restartLabel"]);
            Assert.Equal(reading.DisplayValue, (string?)row["displayValue"]);
            Assert.Equal(reading.Origin.ToString(), (string?)row["origin"]);
            Assert.Equal(reading.OriginLabel, (string?)row["originLabel"]);
            Assert.Equal(reading.IsEditable, (bool)row["editable"]!);
            Assert.Equal(reading.SessionNote_OrNull, (string?)row["sessionNote"]);
            Assert.True(JsonNode.DeepEquals(reading.Value_OrNull, row["value"]), $"'{definition.Path}': the value is not the reading's");
            Assert.Equal(reading.OfferedValues, row["offers"]!.AsArray().Select(offer => (string)offer!["caption"]!));
        }

        // And the one row this test set is read as set, so the loop above is not comparing defaults with defaults.
        var interval = Row(json, INTERVAL_PATH);
        Assert.Equal(45, (int)interval["value"]!);
        Assert.Equal(SettingOrigin_Labels.CONFIG_FILE, (string?)interval["originLabel"]);
    }

    /// <summary>
    /// AN OFFER CARRIES THE VALUE TO PUT, not only its caption, so the page never learns a phrase. A Toggle's
    /// "on" is <c>true</c> and a nullable Choice's "not set" is JSON <c>null</c> — read back through
    /// <see cref="SettingValue_Parser"/>, the formatter's inverse for every offer (ruling P13). Without the value
    /// the page's script would have to know that "not set" means null, which is the formatter's knowledge
    /// copied into JavaScript where no test can reach it (decision 12). A picker list's offers are WORDS to add,
    /// each the JSON string itself.
    /// </summary>
    [Fact]
    public void Get_EveryOffer_CarriesTheValueToPut_ReadThroughTheOneParser()
    {
        var json = Json(Get().Body);

        foreach (var definition in Catalog.ALL)
        {
            foreach (var offer in Row(json, definition.Path)["offers"]!.AsArray())
            {
                var caption = (string)offer!["caption"]!;
                var expected = definition.Renderer == SettingRenderers.OrderedList
                    ? JsonValue.Create(caption)
                    : SettingValue_Parser.Parse(definition, caption);

                Assert.True(JsonNode.DeepEquals(expected, offer["value"]), $"'{definition.Path}' offer '{caption}' carries {offer["value"]?.ToJsonString() ?? "null"}");
            }
        }

        // The two shapes the page would otherwise have to know, pinned by name.
        var toggle = Row(json, SCREENSHOTS_PATH)["offers"]!.AsArray();
        Assert.Contains(toggle, offer => (string)offer!["caption"]! == SettingValue_Formatter.ON && (bool)offer["value"]! == true);

        var effort = Row(json, SUPERVISOR_EFFORT_PATH)["offers"]!.AsArray();
        var nothing = effort.Single(offer => (string)offer!["caption"]! == SettingValue_Formatter.NOT_SET)!.AsObject();
        Assert.True(nothing.ContainsKey("value"));
        Assert.Null(nothing["value"]);
    }

    [Fact]
    public void Get_CarriesTheCategorySections_InCatalogueOrder_IncludingTheEmptyKitOne()
    {
        var json = Json(Get().Body);
        var (readings, _) = SettingsSnapshot_Reader.Read_All_FromDisk(_paths, session: null, log: null);
        var expected = SettingsRow_Builder.Build_Sections(readings);

        var sections = json["sections"]!.AsArray().Select(section => section!.AsObject()).ToArray();

        Assert.Equal(expected.Select(section => section.Category.ToString()), sections.Select(section => (string)section["category"]!));
        Assert.Equal(expected.Select(section => section.Title), sections.Select(section => (string)section["title"]!));

        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(
                expected[index].Rows.Select(reading => reading.Definition.Path),
                sections[index]["rows"]!.AsArray().Select(row => (string)row!["path"]!));
        }

        var kit = sections.Single(section => (string)section["category"]! == nameof(SettingCategories.Kit));
        Assert.Empty(kit["rows"]!.AsArray());
    }

    /// <summary>The page SHOWS the preset in its header and never offers it as a row (D13) — so the GET must carry it.</summary>
    [Fact]
    public void Get_CarriesTheActivePresetName()
    {
        Assert.Equal(Presets_Loader.CLASSIC, (string?)Json(Get().Body)["preset"]);

        Write_Config($$"""{"preset":"{{Presets_Loader.QUIET}}"}""");

        Assert.Equal(Presets_Loader.QUIET, (string?)Json(Get().Body)["preset"]);
    }

    /// <summary>
    /// THE ASSERTION, NOT THE COMMENT. The bot token is absent from the catalogue by design, and the GET is open
    /// on loopback (D4) — so this checks the BODY: no bot token, nothing from secrets.json, and the web.token's
    /// VALUE absent even though its row is present (ruling P2), including when the host hands the handler that
    /// very token to check a PUT against (ruling P30). A GET that echoed it would make "a set token is still
    /// enforced" enforce nothing.
    /// </summary>
    [Fact]
    public void Get_CarriesNoBotToken_AndNoSecretsAtAll()
    {
        const string botToken = "123456789-botsecretvalue";
        const string otherSecret = "anothersecretinsecretsjson";
        const string webToken = "webtokens3cretvalue";

        File.WriteAllText(_paths.SecretsFile, $$"""{"telegramBotToken":"{{botToken}}","somethingElse":"{{otherSecret}}"}""");
        Write_Config($$$"""{"web":{"token":"{{{webToken}}}"}}""");

        var (status, _, body) = Get(configuredToken: webToken);

        Assert.Equal(200, status);

        var strings = All_Strings(JsonNode.Parse(body)).ToArray();

        foreach (var secret in new[] { botToken, otherSecret, webToken, "telegramBotToken", "somethingElse" })
        {
            Assert.DoesNotContain(secret, body);
            Assert.DoesNotContain(strings, text => text.Contains(secret, StringComparison.Ordinal));
        }

        // The row is still there, and says only that a token is set.
        var tokenRow = Row(Json(body), SettingsSnapshot_Reader.MASKED_SECRET_PATH);
        Assert.Null(tokenRow["value"]);
        Assert.Equal(SettingsSnapshot_Reader.SECRET_SET, (string?)tokenRow["displayValue"]);
    }

    /// <summary>GET is open on loopback whatever the token (D4): reading needs nothing, and the page can say editing does.</summary>
    [Fact]
    public void Get_WithNoToken_StillAnswers()
    {
        var (status, _, body) = Get(configuredToken: "a-set-token");

        Assert.Equal(200, status);
        Assert.NotEmpty(Rows(Json(body)));

        var editing = Json(body)["editing"]!.AsObject();
        Assert.True((bool)editing["tokenRequired"]!);
        Assert.Equal(SettingsRequest_Handler.TOKEN_HEADER, (string?)editing["tokenHeader"]);

        Assert.False((bool)Json(Get(configuredToken: "").Body)["editing"]!["tokenRequired"]!);
    }

    // ---------------------------------------------------------------------------------------
    // PUT /settings
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Put_APartialObject_AppliesEachPath_AndReportsPerPath()
    {
        Write_Config("""{"someAgentKey":"kept"}""");

        var (status, contentType, body) = Put($$"""{"{{INTERVAL_PATH}}":45,"{{SCREENSHOTS_PATH}}":true}""");

        Assert.Equal(200, status);
        Assert.Equal(JSON_CONTENT_TYPE, contentType);

        var results = Json(body)["results"]!.AsArray().Select(result => result!.AsObject()).ToArray();

        Assert.Equal(new[] { INTERVAL_PATH, SCREENSHOTS_PATH }, results.Select(result => (string)result["path"]!));
        Assert.All(results, result =>
        {
            Assert.Equal("Applied", (string?)result["outcome"]);
            Assert.Null(result["message"]);
        });

        Assert.Equal(45, Reading(INTERVAL_PATH).Value_OrNull!.GetValue<int>());
        Assert.Equal(SettingOrigins.ConfigFile, Reading(INTERVAL_PATH).Origin);
        Assert.True(Reading(SCREENSHOTS_PATH).Value_OrNull!.GetValue<bool>());
        Assert.Equal("kept", (string?)Json(Config_Text())["someAgentKey"]);
    }

    [Fact]
    public void Put_AnUnknownPath_IsRefused_AndNothingIsWritten()
    {
        const string original = """{"phone":{"status":{"intervalMinutes":45}}}""";
        Write_Config(original);

        var (status, _, body) = Put("""{"no.such.setting":1}""");

        Assert.Equal(422, status);

        var result = Single_Result(body);
        Assert.Equal("no.such.setting", (string?)result["path"]);
        Assert.Equal("RefusedUnknownPath", (string?)result["outcome"]);
        Assert.Contains("no.such.setting", (string?)result["message"]);
        Assert.Equal(original, Config_Text());
    }

    /// <summary>
    /// THE REFUSAL IS THE DEFINITION'S OWN WORDS (decision 21). The handler never judges a value; the catalogue
    /// already knows 240 is too long an interval, and its sentence is the one the page shows beside the box.
    /// </summary>
    [Fact]
    public void Put_AnInvalidValue_IsRefusedWithTheCataloguesOwnMessage()
    {
        const string original = """{"phone":{"status":{"intervalMinutes":45}}}""";
        Write_Config(original);

        var cataloguesMessage = Definition(INTERVAL_PATH).Validate_OrNull(JsonValue.Create(240));
        Assert.NotNull(cataloguesMessage);

        var (status, _, body) = Put($$"""{"{{INTERVAL_PATH}}":240}""");

        Assert.Equal(422, status);

        var result = Single_Result(body);
        Assert.Equal("RefusedInvalid", (string?)result["outcome"]);
        Assert.Equal(cataloguesMessage, (string?)result["message"]);
        Assert.Equal(original, Config_Text());
    }

    /// <summary>
    /// JSON null IS A VALUE, FOR THE DEFINITION TO JUDGE (ruling P3) — never a Reset in disguise. A nullable row
    /// takes it as a stated nothing: <c>effort.supervisor: null</c> is written as null and BEATS classic's xhigh
    /// (no --effort flag), which a deleted key would not. A row that does not accept null refuses it in its own
    /// words. Reset is <c>DELETE</c>, below.
    /// </summary>
    [Fact]
    public void Put_ANullValue_IsAValue_AcceptedOnlyForANullableRow()
    {
        Assert.Null(Definition(SUPERVISOR_EFFORT_PATH).Validate_OrNull(null));

        var (status, _, body) = Put($$"""{"{{SUPERVISOR_EFFORT_PATH}}":null}""");

        Assert.Equal(200, status);
        Assert.Equal("Applied", (string?)Single_Result(body)["outcome"]);

        var effort = Json(Config_Text())["effort"]!.AsObject();
        Assert.True(effort.ContainsKey("supervisor"));
        Assert.Null(effort["supervisor"]);
        Assert.Equal(SettingOrigins.ConfigFile, Reading(SUPERVISOR_EFFORT_PATH).Origin);
        Assert.Null(Reading(SUPERVISOR_EFFORT_PATH).Value_OrNull);

        var before = Config_Text();
        var cataloguesMessage = Definition(INTERVAL_PATH).Validate_OrNull(null);
        Assert.NotNull(cataloguesMessage);

        var (refusedStatus, _, refusedBody) = Put($$"""{"{{INTERVAL_PATH}}":null}""");

        Assert.Equal(422, refusedStatus);
        Assert.Equal("RefusedInvalid", (string?)Single_Result(refusedBody)["outcome"]);
        Assert.Equal(cataloguesMessage, (string?)Single_Result(refusedBody)["message"]);
        Assert.Equal(before, Config_Text());
    }

    /// <summary>
    /// A ReadOnly row is changed elsewhere — <c>repos</c> in the main window — and a PUT of it would replace the
    /// repository list from a web form. The refusal is the writer's, and names where the row IS changed.
    /// </summary>
    [Fact]
    public void Put_AReadOnlyPath_IsRefused()
    {
        const string original = """{"repos":[{"name":"one","path":"C:/one"}]}""";
        Write_Config(original);

        var readOnly = Definition("repos");
        Assert.Equal(SettingRenderers.ReadOnly, readOnly.Renderer);

        var (status, _, body) = Put("""{"repos":[]}""");

        Assert.Equal(422, status);

        var result = Single_Result(body);
        Assert.Equal("RefusedReadOnly", (string?)result["outcome"]);
        Assert.Contains(readOnly.Description, (string?)result["message"]);
        Assert.Equal(original, Config_Text());
    }

    /// <summary>
    /// A BODY THE HANDLER CANNOT READ IS A 400 THAT SAYS SO, NEVER A THROW: the listener would otherwise answer
    /// with a dropped connection and the page would show nothing. A repeated key is refused rather than
    /// last-wins, because which of two values the owner meant is not the handler's to guess; an empty object
    /// names nothing to do and is a page bug worth hearing about.
    /// </summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[1,2]")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"phone.status.intervalMinutes":45,"phone.status.intervalMinutes":30}""")]
    public void Put_ABodyThatIsNotJson_IsFourHundred_NotAThrow(string body)
    {
        const string original = """{"phone":{"status":{"intervalMinutes":45}}}""";
        Write_Config(original);

        var (status, contentType, answer) = Put(body);

        Assert.Equal(400, status);
        Assert.Equal(JSON_CONTENT_TYPE, contentType);
        Assert.False(string.IsNullOrWhiteSpace((string?)Json(answer)["error"]));
        Assert.Equal(original, Config_Text());
    }

    /// <summary>
    /// A WRITE THAT DID NOT HAPPEN IS NEVER REPORTED AS ONE (<c>Atomic_FileWriter</c>'s rule, which
    /// <c>Settings_Writer</c> keeps by throwing). The handler turns the throw into a 500 that says nothing was
    /// applied, and one line for the log — the owner caused it, so the page answers too (decision 15).
    /// </summary>
    [Fact]
    public void Put_WhenTheWriteFails_IsFiveHundred_AndClaimsNothingApplied()
    {
        // A FOLDER where config.json should be: the read finds no file, and the rename over it fails on every OS.
        Directory.CreateDirectory(_paths.ConfigFile);
        var log = new RecordingLog();

        var (status, _, body) = Put($$"""{"{{INTERVAL_PATH}}":45}""", log: log);

        Assert.Equal(500, status);
        Assert.False(string.IsNullOrWhiteSpace((string?)Json(body)["error"]));
        Assert.DoesNotContain("Applied", body);
        Assert.Single(log.Errors);
    }

    /// <summary>
    /// D4 (owner, 2026-09-14), ruling P1: editing is open on loopback until <c>web.token</c> is set. The owner's
    /// brother reaches this page through an SSH tunnel, and a blank token reads "not set" on the page — so a
    /// whitespace-only token is not set here either, or the page would say editing is open and then refuse it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Put_WithNoConfiguredToken_Applies(string configuredToken)
    {
        var (status, _, body) = Put($$"""{"{{INTERVAL_PATH}}":45}""", configuredToken);

        Assert.Equal(200, status);
        Assert.Equal("Applied", (string?)Single_Result(body)["outcome"]);
        Assert.Equal(45, Reading(INTERVAL_PATH).Value_OrNull!.GetValue<int>());
    }

    /// <summary>A SET token is still enforced (D4): no header and a wrong header are both 401, and neither writes.</summary>
    [Fact]
    public void Put_WithTheWrongToken_IsFourOhOne()
    {
        const string original = """{"phone":{"status":{"intervalMinutes":45}}}""";
        const string configured = "the-right-token";
        Write_Config(original);

        foreach (var header in new[] { NO_HEADERS, Header_Carrying("the-wrong-token"), Header_Carrying("the-right-toke"), Header_Carrying("") })
        {
            var (status, _, body) = Put($$"""{"{{INTERVAL_PATH}}":30}""", configured, header);

            Assert.Equal(401, status);
            Assert.Contains(SettingsRequest_Handler.TOKEN_HEADER, (string?)Json(body)["error"]);
            Assert.DoesNotContain(configured, body);
            Assert.Equal(original, Config_Text());
        }
    }

    [Fact]
    public void Put_WithTheRightToken_Applies()
    {
        const string configured = "the-right-token";

        var (status, _, body) = Put($$"""{"{{INTERVAL_PATH}}":30}""", configured, Header_Carrying(configured));

        Assert.Equal(200, status);
        Assert.Equal("Applied", (string?)Single_Result(body)["outcome"]);
        Assert.Equal(30, Reading(INTERVAL_PATH).Value_OrNull!.GetValue<int>());
    }

    // ---------------------------------------------------------------------------------------
    // DELETE /settings?path=<p> — Reset (ruling P3)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// RESET DELETES THE KEY (spec §6.2) — it does not write the default. A materialised default is a default
    /// that can never move again. Every other key of config.json survives.
    /// </summary>
    [Fact]
    public void Delete_WithAPath_ResetsIt_AndDeletesTheKey()
    {
        Write_Config("""{"phone":{"status":{"intervalMinutes":45}},"someAgentKey":"kept"}""");

        var (status, contentType, body) = Delete(Reset_Target(INTERVAL_PATH));

        Assert.Equal(200, status);
        Assert.Equal(JSON_CONTENT_TYPE, contentType);

        var result = Single_Result(body);
        Assert.Equal(INTERVAL_PATH, (string?)result["path"]);
        Assert.Equal("Reset", (string?)result["outcome"]);

        var config = Json(Config_Text());
        Assert.Null(config["phone"]?["status"]?["intervalMinutes"]);
        Assert.Equal("kept", (string?)config["someAgentKey"]);
        Assert.NotEqual(SettingOrigins.ConfigFile, Reading(INTERVAL_PATH).Origin);
    }

    /// <summary>The same rule as a PUT: a Reset of <c>repos</c> would delete the repository list.</summary>
    [Fact]
    public void Delete_AReadOnlyPath_IsRefused_LikeAPut()
    {
        const string original = """{"repos":[{"name":"one","path":"C:/one"}]}""";
        Write_Config(original);

        var (status, _, body) = Delete(Reset_Target("repos"));

        Assert.Equal(422, status);
        Assert.Equal("RefusedReadOnly", (string?)Single_Result(body)["outcome"]);
        Assert.Equal(original, Config_Text());
    }

    [Theory]
    [InlineData("/settings")]
    [InlineData("/settings?")]
    [InlineData("/settings?path=")]
    [InlineData("/settings?other=phone.status.intervalMinutes")]
    public void Delete_WithNoPath_IsFourHundred(string target)
    {
        const string original = """{"phone":{"status":{"intervalMinutes":45}}}""";
        Write_Config(original);

        var (status, _, body) = Delete(target);

        Assert.Equal(400, status);
        Assert.Contains("?path=", (string?)Json(body)["error"]);
        Assert.Equal(original, Config_Text());
    }

    /// <summary>A Reset rewrites config.json like a PUT does, so a set token guards it the same way.</summary>
    [Fact]
    public void Delete_WithTheWrongToken_IsFourOhOne()
    {
        const string original = """{"phone":{"status":{"intervalMinutes":45}}}""";
        const string configured = "the-right-token";
        Write_Config(original);

        var (status, _, _) = Delete(Reset_Target(INTERVAL_PATH), configured, Header_Carrying("the-wrong-token"));

        Assert.Equal(401, status);
        Assert.Equal(original, Config_Text());

        var (allowed, _, _) = Delete(Reset_Target(INTERVAL_PATH), configured, Header_Carrying(configured));

        Assert.Equal(200, allowed);
    }

    // ---------------------------------------------------------------------------------------
    // Routing
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The root is Task 7's — it serves the embedded page before it ever asks this handler — so from here
    /// <c>/</c> is as unknown as any other path. Paths are wire spellings: ordinal, no trailing-slash guess.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/nope")]
    [InlineData("/settings/extra")]
    [InlineData("/Settings")]
    [InlineData("/settings/")]
    public void AnUnknownPath_IsFourOhFour(string target)
    {
        var (status, contentType, body) = SettingsRequest_Handler.Handle("GET", target, NO_HEADERS, "", _paths, "", log: null);

        Assert.Equal(404, status);
        Assert.Equal(JSON_CONTENT_TYPE, contentType);
        Assert.False(string.IsNullOrWhiteSpace((string?)Json(body)["error"]));
    }

    /// <summary>
    /// OPTIONS IS REFUSED ON PURPOSE, not merely unimplemented. A PUT or DELETE from a page on another origin is
    /// never a "simple" request, so the browser sends a CORS preflight first — and a 405 with no
    /// <c>Access-Control-Allow-*</c> answer is what stops a website in the owner's browser from rewriting
    /// config.json over loopback while editing is open (D4). Nothing is written by a method this handler refuses.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("OPTIONS")]
    [InlineData("HEAD")]
    [InlineData("get")]
    public void AnUnsupportedMethod_IsFourOhFive_AndNamesWhatIsAllowed(string method)
    {
        const string original = """{"phone":{"status":{"intervalMinutes":45}}}""";
        Write_Config(original);

        var (status, _, body) = SettingsRequest_Handler.Handle(
            method, SettingsRequest_Handler.SETTINGS_PATH, NO_HEADERS, $$"""{"{{INTERVAL_PATH}}":30}""", _paths, "", log: null);

        Assert.Equal(405, status);

        var error = (string?)Json(body)["error"];
        Assert.Contains("GET", error);
        Assert.Contains("PUT", error);
        Assert.Contains("DELETE", error);
        Assert.Equal(original, Config_Text());
    }

    /// <summary>
    /// INVARIANT CULTURE, EXPLICITLY. The daemon sets InvariantGlobalization=true and the WPF host does not,
    /// so the same PUT body must parse the same way in both hosts — a number formatted or parsed under the
    /// machine's culture is a setting that means one thing on one host and another on the other. it-IT is the
    /// owner's own: "1.5" there is fifteen to a culture-aware parse, and 1440 renders "1.440" under "N0".
    /// </summary>
    [Fact]
    public void ANumber_IsParsedAndRenderedInvariantly_InEitherHost()
    {
        var italian = CultureInfo.GetCultureInfo("it-IT");

        // The culture must actually differ, or this test compares invariant with invariant and pins nothing.
        Assert.Equal(",", italian.NumberFormat.NumberDecimalSeparator);
        Assert.Equal(".", italian.NumberFormat.NumberGroupSeparator);

        var daemon = Run_Under(CultureInfo.InvariantCulture);
        var desktop = Run_Under(italian);

        Assert.Equal(daemon, desktop);

        var (put, get, refused, config) = daemon;
        Assert.Contains("\"Applied\"", put);
        Assert.Equal("1440", (string?)Row(Json(get), BUTTON_EXPIRY_PATH)["displayValue"]);
        Assert.Equal(1440L, (long)Row(Json(get), BUTTON_EXPIRY_PATH)["value"]!);
        Assert.Equal("-1001234567890", (string?)Row(Json(get), CHAT_ID_PATH)["displayValue"]);
        Assert.Equal(-1001234567890L, (long)Row(Json(get), CHAT_ID_PATH)["value"]!);
        Assert.Equal("RefusedInvalid", (string?)Single_Result(refused)["outcome"]);
        Assert.Equal(1440L, (long)Json(config)[BUTTON_EXPIRY_PATH]!);
    }

    /// <summary>One PUT, one GET and one refused PUT under <paramref name="culture"/>, over a fresh config.json.</summary>
    (string Put, string Get, string Refused, string Config) Run_Under(CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            Write_Config("{}");

            var put = Put($$"""{"{{BUTTON_EXPIRY_PATH}}":1440,"{{CHAT_ID_PATH}}":-1001234567890}""").Body;
            var get = Get().Body;
            var refused = Put($$"""{"{{BUTTON_EXPIRY_PATH}}":1.5}""").Body;

            return (put, get, refused, Config_Text());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    sealed class RecordingLog : IOrchestrationLog
    {
        public List<string> Errors { get; } = [];

        public void Log_Info(string orchId, string message)
        {
        }

        public void Log_Warning(string orchId, string message)
        {
        }

        public void Log_Error(string orchId, string message, Exception? exception)
        {
            Errors.Add(message);
        }

        public event Action<IOrchestrationLogEntry>? EntryLogged
        {
            add { }
            remove { }
        }
    }
}
