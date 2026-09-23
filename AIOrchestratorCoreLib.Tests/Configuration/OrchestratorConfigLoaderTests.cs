using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration;

public class OrchestratorConfigLoaderTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public OrchestratorConfigLoaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-loader-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// Screenshots are OPT-IN: taking one raises and maximises a real window on the owner's desk,
    /// so every config.json written before the flag existed — and every one where the owner never
    /// asked — must read as OFF. An absent key defaulting the other way would start raising windows
    /// on machines whose owner never enabled anything.
    /// </summary>
    [Fact]
    public void Load_StatusScreenshotsKeyAbsent_ReadsAsFalse()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");

        Assert.False(OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramStatusScreenshots);
    }

    /// <summary>And with no config file at all — the empty config is the same OFF answer.</summary>
    [Fact]
    public void Load_NoConfigFileAtAll_StatusScreenshotsIsFalse()
    {
        Assert.False(OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramStatusScreenshots);
    }

    /// <summary>
    /// The /screenshots command persists through Save, and the next launch has to see it — a flag
    /// that is written but not parsed back reads as "the toggle never worked".
    ///
    /// <para>
    /// THE FOUR MODELS NO LONGER MAKE THE TRIP, AND THIS TEST NOW PINS THAT (plan 04 D1, 2026-09-23). It
    /// used to assert they round-tripped — and its first model assertion was green only because "opus" is
    /// also the shipped default, so it could not have told a written model from an unwritten one. Save now
    /// writes no model key: the config below carries four models that are NONE of the shipped defaults
    /// (asserted, so a default that later moved onto one of them could not turn this green by coincidence),
    /// and the reload reads the defaults, because a model is written by <c>Settings_Writer</c> when its
    /// row is edited and by nothing else. The neighbours Save still owns keep the round trip they had.
    /// This is the /screenshots path exactly: <c>BridgeEngineModel.Set_StatusScreenshots</c> saves a
    /// config rebuilt from the resolved one, which until D1 wrote those resolved models back.
    /// </para>
    /// </summary>
    [Fact]
    public void Save_ThenLoad_ExplicitTrueRoundTrips()
    {
        var config = OrchestratorConfig_Factory.Create(
            [RepoEntry_Factory.Create("Arb Studio", @"C:\repos\arb")],
            "haiku",
            "fable",
            null,
            null,
            "haiku",
            "fable",
            -1001234567890,
            42,
            "bot-token",
            telegramStatusScreenshots: true,
            "whisper --file",
            5_000_000);

        Assert.NotEqual(OrchestratorConfig_Factory.DEFAULT_SUPERVISOR_MODEL, config.SupervisorModel);
        Assert.NotEqual(OrchestratorConfig_Factory.DEFAULT_IMPLEMENTER_MODEL, config.ImplementerModel);
        Assert.NotEqual(OrchestratorConfig_Factory.DEFAULT_GENERAL_SUPERVISOR_MODEL, config.GeneralSupervisorModel);
        Assert.NotEqual(OrchestratorConfig_Factory.DEFAULT_COMMUNICATOR_MODEL, config.CommunicatorModel);

        OrchestratorConfig_Loader.Save(config, _paths);
        var reloaded = OrchestratorConfig_Loader.Load_OrEmpty(_paths);

        Assert.True(reloaded.TelegramStatusScreenshots);

        // D1: the four models the config carried were not written, so the reload is a machine that
        // states none — the shipped defaults.
        Assert.Equal(OrchestratorConfig_Factory.DEFAULT_SUPERVISOR_MODEL, reloaded.SupervisorModel);
        Assert.Equal(OrchestratorConfig_Factory.DEFAULT_IMPLEMENTER_MODEL, reloaded.ImplementerModel);
        Assert.Equal(OrchestratorConfig_Factory.DEFAULT_GENERAL_SUPERVISOR_MODEL, reloaded.GeneralSupervisorModel);
        Assert.Equal(OrchestratorConfig_Factory.DEFAULT_COMMUNICATOR_MODEL, reloaded.CommunicatorModel);

        // The neighbours survived the trip too — a new key must not disturb the existing ones.
        Assert.Single(reloaded.Repos);
        Assert.Equal("Arb Studio", reloaded.Repos[0].Name);
        Assert.Equal(-1001234567890, reloaded.TelegramSupergroupChatId);
        Assert.Equal(42, reloaded.TelegramOwnerUserId);
        Assert.Equal("bot-token", reloaded.TelegramBotToken);
        Assert.Equal("whisper --file", reloaded.VoiceTranscribeCommand);
        Assert.Equal(5_000_000, reloaded.OrchestrationTokenBudget);
    }

    /// <summary>An explicit false stays false — the round trip is not just "true sticks".</summary>
    [Fact]
    public void Save_ThenLoad_ExplicitFalseRoundTrips()
    {
        var config = OrchestratorConfig_Factory.Create_WithStatusScreenshots(
            OrchestratorConfig_Factory.Create_Empty(), false);

        OrchestratorConfig_Loader.Save(config, _paths);

        Assert.False(OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramStatusScreenshots);
    }

    /// <summary>
    /// THE BOT TOKEN HAS ITS OWN DOOR, AND IT OPENS ON secrets.json ALONE (plan 04 ruling P11). The full
    /// <see cref="OrchestratorConfig_Loader.Save"/> also rewrites <c>runners.*</c>, <c>limits.*</c> and five Kernel scalars
    /// at whatever they RESOLVED to, which turns their origin into "set here" and undoes a Reset made a
    /// minute earlier from the phone. The token is the one thing the settings renderers may not write
    /// (the catalogue leaves it out on purpose), so the window's Connection tab needs a save that touches
    /// nothing else: config.json byte for byte, and every other key of secrets.json carried through.
    /// </summary>
    [Fact]
    public void SaveBotToken_WritesSecretsJsonOnly_AndCarriesItsOtherKeys()
    {
        const string configText = """{"repos":[],"runners":{"supervisor":{"runner":"print"}},"telegramInbound":"off"}""";
        File.WriteAllText(_paths.ConfigFile, configText);
        File.WriteAllText(_paths.SecretsFile, """{"telegramBotToken":"old-token","somethingElse":7}""");

        OrchestratorConfig_Loader.Save_BotToken(_paths, "new-token");

        Assert.Equal(configText, File.ReadAllText(_paths.ConfigFile));

        var secrets = JsonNode.Parse(File.ReadAllText(_paths.SecretsFile))!.AsObject();
        Assert.Equal("new-token", secrets["telegramBotToken"]!.GetValue<string>());
        Assert.Equal(7, secrets["somethingElse"]!.GetValue<int>());

        Assert.Equal("new-token", OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramBotToken);
    }

    /// <summary>
    /// THE TOKEN'S DOOR TIDIES WHAT A BOX HANDS IT (plan 04 Task 9): the WPF window may hold no logic, so the
    /// trim of a pasted token and "blank is no token" — what the old window's own Save did — live here.
    /// </summary>
    [Fact]
    public void SaveBotToken_TrimsAPastedToken_AndABlankOneIsNoToken()
    {
        Assert.Equal("123:abc", OrchestratorConfig_Loader.Save_BotToken(_paths, "  123:abc \t"));
        Assert.Equal("123:abc", OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramBotToken);

        Assert.Null(OrchestratorConfig_Loader.Save_BotToken(_paths, "   "));
        var secrets = JsonNode.Parse(File.ReadAllText(_paths.SecretsFile))!.AsObject();

        Assert.True(secrets.ContainsKey("telegramBotToken"));
        Assert.Null(secrets["telegramBotToken"]);
        Assert.Null(OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramBotToken);
    }

    /// <summary>
    /// A config.json carrying every kind of key <see cref="OrchestratorConfig_Loader.Save"/> does NOT own — the
    /// ones a lenient read of a held file used to erase (plan 04 Task 2c, ruling P35): <c>planBackend</c>, the
    /// <c>effort</c>/<c>phone</c>/<c>pulse</c>/<c>web</c> blocks, a model key, a guardrail and a key no build knows.
    /// </summary>
    const string HAND_EDITED_CONFIG = """
        {
          "repos": [ { "name": "Arb Studio", "path": "/repos/arb" } ],
          "telegramSupergroupChatId": -1001234567890,
          "telegramOwnerUserId": 42,
          "planBackend": { "kind": "external", "assembly": "Plans.dll", "type": "Plans.Backend" },
          "effort": { "supervisor": "high" },
          "phone": { "status": { "intervalMinutes": 30 } },
          "pulse": { "fields": [ "cost" ] },
          "web": { "listen": "127.0.0.1:7391" },
          "supervisorModel": "haiku",
          "highRiskPatterns": [ "deploy" ],
          "somethingAHandAdded": { "kept": true }
        }
        """;

    static readonly string[] KEYS_SAVE_DOES_NOT_OWN =
        ["planBackend", "effort", "phone", "pulse", "web", "supervisorModel", "highRiskPatterns", "somethingAHandAdded"];

    /// <summary>
    /// A config.json ANOTHER PROGRAM HOLDS IS NEVER REWRITTEN BY SAVE (plan 04 Task 2c, ruling P35). Save read it
    /// through the lenient read — a plain <c>File.ReadAllText</c> underneath, not even the retrying reader — so a
    /// sharing violation became an empty tree and the file was written holding only Save's own keys: the Settings
    /// window's Save, or one <c>/screens</c> from the phone, erasing <c>planBackend</c>, the effort/phone/pulse/web
    /// blocks, the model keys and the guardrails. Now: an <see cref="IOException"/> naming the file, config.json
    /// byte for byte, and secrets.json not even created — "nothing was saved" covers both files. Released, the same
    /// save lands and every key Save does not own is exactly the owner's.
    ///
    /// <para>
    /// AN EXACT <see cref="IOException"/>, because on Windows the old code threw too — after the swallowed read, the
    /// rename onto the held file fails as <see cref="UnauthorizedAccessException"/> — so the type and the words are
    /// what separate "refused before writing" from "tried to write an empty tree and lost the race to the holder".
    /// On Linux the flock hold does not stop a rename, and there the old code wiped the file outright.
    /// </para>
    /// </summary>
    [RequiresExclusiveOpenEnforcementFact]
    public void Save_OverAConfigJsonHeldOpenExclusively_Throws_WritesNothing_AndSavesOnceReleased()
    {
        File.WriteAllText(_paths.ConfigFile, HAND_EDITED_CONFIG);
        var before = File.ReadAllBytes(_paths.ConfigFile);
        var toggled = OrchestratorConfig_Factory.Create_WithStatusScreenshots(OrchestratorConfig_Loader.Load_OrEmpty(_paths), true);

        IOException refused;

        using (new FileStream(_paths.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.None))
            refused = Assert.Throws<IOException>(() => OrchestratorConfig_Loader.Save(toggled, _paths));

        Assert.Contains(_paths.ConfigFile, refused.Message);
        Assert.Contains("could not be read", refused.Message);
        Assert.Equal(before, File.ReadAllBytes(_paths.ConfigFile));
        Assert.False(File.Exists(_paths.SecretsFile), "secrets.json was written by a save that refused");

        OrchestratorConfig_Loader.Save(toggled, _paths);

        var saved = JsonNode.Parse(File.ReadAllText(_paths.ConfigFile))!.AsObject();
        var owners = JsonNode.Parse(HAND_EDITED_CONFIG)!.AsObject();

        Assert.True(saved["telegramStatusScreenshots"]!.GetValue<bool>());

        foreach (var key in KEYS_SAVE_DOES_NOT_OWN)
            Assert.True(JsonNode.DeepEquals(owners[key], saved[key]), $"'{key}' did not survive the save: {saved.ToJsonString()}");
    }

    /// <summary>
    /// A HELD secrets.json REFUSES THE WHOLE SAVE, config.json included. Save used to write config.json first and
    /// only then read secrets.json, so a refusal there was a HALF save — config.json rewritten, the token not, and an
    /// exception saying the save had failed. Both files are now read before either is written.
    /// </summary>
    [RequiresExclusiveOpenEnforcementFact]
    public void Save_OverASecretsJsonHeldOpenExclusively_Throws_AndWritesNeitherFile()
    {
        File.WriteAllText(_paths.ConfigFile, HAND_EDITED_CONFIG);
        File.WriteAllText(_paths.SecretsFile, """{"telegramBotToken":"old-token","somethingElse":7}""");
        var configBefore = File.ReadAllBytes(_paths.ConfigFile);
        var secretsBefore = File.ReadAllBytes(_paths.SecretsFile);
        var toggled = OrchestratorConfig_Factory.Create_WithStatusScreenshots(OrchestratorConfig_Loader.Load_OrEmpty(_paths), true);

        IOException refused;

        using (new FileStream(_paths.SecretsFile, FileMode.Open, FileAccess.Read, FileShare.None))
            refused = Assert.Throws<IOException>(() => OrchestratorConfig_Loader.Save(toggled, _paths));

        Assert.Contains(_paths.SecretsFile, refused.Message);
        Assert.Contains("could not be read", refused.Message);
        Assert.Equal(configBefore, File.ReadAllBytes(_paths.ConfigFile));
        Assert.Equal(secretsBefore, File.ReadAllBytes(_paths.SecretsFile));
    }

    /// <summary>
    /// SAVE REFUSES AN UNPARSABLE secrets.json, exactly as the token's own door does (ruling P36, 2026-09-23). "Save
    /// keeps its corrupt-file behaviour" is config.json's rule only: secrets.json holds only what a human put there, so
    /// a rewrite is never the smaller loss, whichever of its two writers would make it. Nothing is written — config.json
    /// included, since both files are read before either is written.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnparsableSecretsTexts))]
    public void Save_OverAnUnparsableSecretsJson_Throws_AndWritesNeitherFile(string text)
    {
        File.WriteAllText(_paths.ConfigFile, HAND_EDITED_CONFIG);
        File.WriteAllText(_paths.SecretsFile, text);
        var configBefore = File.ReadAllBytes(_paths.ConfigFile);
        var secretsBefore = File.ReadAllBytes(_paths.SecretsFile);

        var refused = Assert.Throws<IOException>(() => OrchestratorConfig_Loader.Save(
            OrchestratorConfig_Factory.Create([], null, null, null, null, null, null, null, null, "fresh-token", null, null, null),
            _paths));

        Assert.Contains(_paths.SecretsFile, refused.Message);
        Assert.Contains("does not parse", refused.Message);
        Assert.Equal(configBefore, File.ReadAllBytes(_paths.ConfigFile));
        Assert.Equal(secretsBefore, File.ReadAllBytes(_paths.SecretsFile));
    }

    /// <summary>
    /// A config.json WITH A KEY STATED TWICE IS NOT NEWLY OVERWRITABLE BY SAVE (ruling P36). Before Task 2c the lenient
    /// read handed back a tree that threw <see cref="ArgumentException"/> on Save's first write into it, so nothing was
    /// written; that safer outcome stays, as a refusal naming the file. The corrupt-file leniency is for a file that
    /// genuinely does not parse — a duplicated key parses, holds every other key the owner wrote, and a rewrite would
    /// both pick one of the two answers for them and erase the rest.
    /// </summary>
    [Fact]
    public void Save_OverAConfigJsonWithADuplicatedKey_Throws_AndWritesNeitherFile()
    {
        const string duplicated = """{ "repos": [], "planBackend": { "kind": "external" }, "repos": [ { "name": "Arb Studio", "path": "/repos/arb" } ] }""";
        File.WriteAllText(_paths.ConfigFile, duplicated);
        File.WriteAllText(_paths.SecretsFile, """{"telegramBotToken":"old-token"}""");
        var configBefore = File.ReadAllBytes(_paths.ConfigFile);
        var secretsBefore = File.ReadAllBytes(_paths.SecretsFile);

        var refused = Assert.Throws<IOException>(() => OrchestratorConfig_Loader.Save(OrchestratorConfig_Factory.Create_Empty(), _paths));

        Assert.Contains(_paths.ConfigFile, refused.Message);
        Assert.Contains("stated twice", refused.Message);
        Assert.Equal(configBefore, File.ReadAllBytes(_paths.ConfigFile));
        Assert.Equal(secretsBefore, File.ReadAllBytes(_paths.SecretsFile));
    }

    /// <summary>The unparsable secrets.json texts both of its writers refuse — shared so the two doors are held to one list.</summary>
    public static TheoryData<string> UnparsableSecretsTexts => new()
    {
        "{not json at all",
        "",
        "[1, 2]",
        """{ "telegramBotToken": "a", "telegramBotToken": "b" }""",
    };

    /// <summary>
    /// secrets.json holds only what a human put there — the token and whatever they added beside it — so for the
    /// token's own door a rewrite is never the smaller loss (ruling P35): a file that does not parse is REFUSED, byte
    /// for byte, with an <see cref="IOException"/> that names it and says so. Empty is refused too, for the
    /// classifier's reason: it is what an editor that truncates before it writes looks like for a moment. A key
    /// stated twice is two answers to one question, and a rewrite would pick one for the owner.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnparsableSecretsTexts))]
    public void SaveBotToken_OverAnUnparsableSecretsJson_Throws_AndLeavesItByteForByte(string text)
    {
        File.WriteAllText(_paths.SecretsFile, text);
        var before = File.ReadAllBytes(_paths.SecretsFile);

        var refused = Assert.Throws<IOException>(() => OrchestratorConfig_Loader.Save_BotToken(_paths, "new-token"));

        Assert.Contains(_paths.SecretsFile, refused.Message);
        Assert.Contains("does not parse", refused.Message);
        Assert.Equal(before, File.ReadAllBytes(_paths.SecretsFile));
        Assert.False(File.Exists(_paths.ConfigFile));
    }

    /// <summary>A HELD secrets.json is refused by the token's door the same way, and the token lands once it is let go.</summary>
    [RequiresExclusiveOpenEnforcementFact]
    public void SaveBotToken_OverASecretsJsonHeldOpenExclusively_Throws_AndSavesOnceReleased()
    {
        File.WriteAllText(_paths.SecretsFile, """{"telegramBotToken":"old-token","somethingElse":7}""");
        var before = File.ReadAllBytes(_paths.SecretsFile);

        IOException refused;

        using (new FileStream(_paths.SecretsFile, FileMode.Open, FileAccess.Read, FileShare.None))
            refused = Assert.Throws<IOException>(() => OrchestratorConfig_Loader.Save_BotToken(_paths, "new-token"));

        Assert.Contains(_paths.SecretsFile, refused.Message);
        Assert.Contains("could not be read", refused.Message);
        Assert.Equal(before, File.ReadAllBytes(_paths.SecretsFile));

        OrchestratorConfig_Loader.Save_BotToken(_paths, "new-token");

        var secrets = JsonNode.Parse(File.ReadAllText(_paths.SecretsFile))!.AsObject();
        Assert.Equal("new-token", secrets["telegramBotToken"]!.GetValue<string>());
        Assert.Equal(7, secrets["somethingElse"]!.GetValue<int>());
    }

    /// <summary>ABSENT IS STILL NOT A REFUSAL: with no secrets.json the token's door creates one holding the token alone.</summary>
    [Fact]
    public void SaveBotToken_WithNoSecretsJson_CreatesItWithTheTokenOnly()
    {
        OrchestratorConfig_Loader.Save_BotToken(_paths, "new-token");

        Assert.Equal("""{"telegramBotToken":"new-token"}""", JsonNode.Parse(File.ReadAllText(_paths.SecretsFile))!.ToJsonString());
    }
}
