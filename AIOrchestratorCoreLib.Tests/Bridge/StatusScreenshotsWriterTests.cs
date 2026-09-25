using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The <c>/screens</c> toggle's save, without the engine around it (plan 04 Task 2c, ruling P35): a save
/// config.json refuses is an answer and one warning, never a throw; a save that lands flips the flag.
/// <see cref="StatusScreenshotsCommandTests"/> proves the engine sends that answer.
/// </summary>
public class StatusScreenshotsWriterTests : IDisposable
{
    const string OWNERS_CONFIG = """{"repos":[],"telegramOwnerUserId":42,"somethingAHandAdded":{"kept":true}}""";

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public StatusScreenshotsWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-screens-writer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// HELD: not saved, the reply says the setting is still OFF and carries the loader's reason, one warning line
    /// names the command, and config.json is byte for byte. The warning is the whole of the log — no Info claiming ON.
    /// </summary>
    [RequiresExclusiveOpenEnforcementFact]
    public void ToggleFlag_OverAConfigJsonHeldOpenExclusively_IsNotSaved_AndSaysSo()
    {
        File.WriteAllText(_paths.ConfigFile, OWNERS_CONFIG);
        var before = File.ReadAllBytes(_paths.ConfigFile);
        var current = OrchestratorConfig_Loader.Load_OrEmpty(_paths);
        var log = new RecordingLog_Fake();

        (bool Saved, string OwnerReply) result;

        using (new FileStream(_paths.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.None))
            result = StatusScreenshots_Writer.Toggle_Flag(current, _paths, log);

        Assert.False(result.Saved);
        Assert.Contains("still OFF", result.OwnerReply);
        Assert.Contains("could not be saved", result.OwnerReply);
        Assert.Contains("could not be read", result.OwnerReply);
        Assert.Equal(before, File.ReadAllBytes(_paths.ConfigFile));

        // THE PHONE GETS THE FILE'S NAME, THE LOG GETS ITS PATH (ruling P36): a local path is noise in a chat, and the
        // log line is where someone goes to find the file.
        Assert.DoesNotContain(_tempRoot, result.OwnerReply);
        Assert.Contains("'config.json' could not be read", result.OwnerReply);

        var line = Assert.Single(log.Dump().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.StartsWith("WARN  [] /screens could not turn status screenshots ON", line);
        Assert.Contains(_paths.ConfigFile, line);
    }

    /// <summary>
    /// A REFUSAL FROM secrets.json IS NAMED AS secrets.json: Save reads both files, and a reply that always said
    /// "config.json" would send the owner to the wrong one. Its path stays out of the reply too, and goes to the log.
    /// </summary>
    [Fact]
    public void ToggleFlag_OverAnUnparsableSecretsJson_IsNotSaved_AndNamesThatFile()
    {
        File.WriteAllText(_paths.ConfigFile, OWNERS_CONFIG);
        var current = OrchestratorConfig_Loader.Load_OrEmpty(_paths);
        File.WriteAllText(_paths.SecretsFile, "{not json at all");
        var before = File.ReadAllBytes(_paths.ConfigFile);
        var log = new RecordingLog_Fake();

        var result = StatusScreenshots_Writer.Toggle_Flag(current, _paths, log);

        Assert.False(result.Saved);
        Assert.Contains("'secrets.json' does not parse", result.OwnerReply);
        Assert.DoesNotContain(_tempRoot, result.OwnerReply);
        Assert.Contains(_paths.SecretsFile, log.Dump());
        Assert.Equal(before, File.ReadAllBytes(_paths.ConfigFile));
    }

    /// <summary>A save that lands flips the flag, keeps the owner's other keys, and says ON — to the owner and to the log.</summary>
    [Fact]
    public void ToggleFlag_OverAReadableConfigJson_IsSaved_AndSaysOn()
    {
        File.WriteAllText(_paths.ConfigFile, OWNERS_CONFIG);
        var log = new RecordingLog_Fake();

        var result = StatusScreenshots_Writer.Toggle_Flag(OrchestratorConfig_Loader.Load_OrEmpty(_paths), _paths, log);

        Assert.True(result.Saved);
        Assert.StartsWith("📸 Status screenshots ON", result.OwnerReply);
        Assert.True(OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramStatusScreenshots);
        Assert.Contains("somethingAHandAdded", File.ReadAllText(_paths.ConfigFile));
        Assert.True(log.Has_Info_Containing("Status screenshots ON"));
    }
}
