using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Time.Clock;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// WHERE THE SILENCE BECOMES A SHAPE. A request that names no <c>mode</c> gets the one
/// <c>config.json</c> names, and a request that DOES name one gets that — in both directions, always.
///
/// <para>
/// This is the half the reader can no longer decide. Basic has been the shape of an unstated request
/// since the owner's directive of 2026-08-13, and it was a constant in the parser: getting a crew
/// meant writing "full" into the message every single time, and the owner who set the rule had no way
/// to change it. Moving the rule into config.json is only meaningful if the request's own silence
/// survives that far, which is why <c>IStartOrchestrationRequest.IsBasic</c> is now nullable.
/// </para>
/// <para>
/// Asserted through the launcher rather than off the session afterwards: <c>Start_Orchestration</c>
/// and <c>Start_BasicOrchestration</c> ARE the decision, and reading the shape back out of the store
/// would be asserting the store's opinion of what happened.
/// </para>
/// </summary>
public class StartOrchestrationDefaultShapeTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingLog_Fake _log = new();

    public StartOrchestrationDefaultShapeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-defaultshape-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>The shape a modeless request gets under each configured default, and under none at all.</summary>
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(null, OrchestrationModes.BASIC)]
    [InlineData(OrchestrationModes.BASIC, OrchestrationModes.BASIC)]
    [InlineData(OrchestrationModes.FULL, OrchestrationModes.FULL)]
    public async Task ARequestThatNamesNoMode_TakesTheConfiguredDefault(string? configured, string expectedShape)
    {
        Write_Config(configured);

        var launcher = await Start_Async("""{"action":"start-orchestration","repo":"Repo"}""");

        Assert.Equal(expectedShape, launcher.Shape);
    }

    /// <summary>
    /// THE REQUEST ALWAYS WINS, and both directions are asserted because a default that only loses one
    /// way is a default that overrules the owner the other way — the failure being "I asked for a solo
    /// and it spawned a crew", which is the expensive one.
    /// </summary>
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(OrchestrationModes.FULL, OrchestrationModes.BASIC)]
    [InlineData(OrchestrationModes.BASIC, OrchestrationModes.FULL)]
    public async Task AnExplicitMode_BeatsTheConfiguredDefault(string configured, string asked)
    {
        Write_Config(configured);

        var launcher = await Start_Async($$"""{"action":"start-orchestration","repo":"Repo","mode":"{{asked}}"}""");

        Assert.Equal(asked, launcher.Shape);
    }

    /// <summary>
    /// AND THE OWNER IS TOLD WHO CHOSE. The configuration layer has no log of its own, so a mistyped
    /// <c>orchestrationMode</c> falls back silently there; this line in the entry the owner already
    /// reads is the only place that fallback becomes visible. Said only when the request was silent —
    /// a request that named its shape needs no explanation of where the shape came from.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheConfirmationSaysTheDefaultChose_OnlyWhenTheRequestDidNot()
    {
        Write_Config(OrchestrationModes.FULL);

        await Start_Async("""{"action":"start-orchestration","repo":"Repo"}""");
        Assert.Contains($"configured default ({OrchestrationModes.FULL})", Read_GeneralChannel(), StringComparison.Ordinal);

        File.Delete(_paths.GeneralChannelFile);

        await Start_Async($$"""{"action":"start-orchestration","repo":"Repo","mode":"{{OrchestrationModes.BASIC}}"}""");
        Assert.DoesNotContain("configured default", Read_GeneralChannel(), StringComparison.Ordinal);
    }

    async Task<LaunchWitness_Fake> Start_Async(string requestJson)
    {
        // Built per call rather than per test: the provider caches on the file's write stamp, and the
        // theories above rewrite config.json between runs.
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var launcher = new LaunchWitness_Fake(
            OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log),
            _paths);

        var engine = BridgeEngine_Factory.Create_WithDecisionState(
            _paths, configProvider, _store, launcher, _log, new CapturingTelegram_Fake(),
            EngineStateStore_Factory.Create_InMemory(),
            Clock_Factory.Create_System(),
            BridgeTestTiming.Fast());

        File.WriteAllText(Path.Combine(_paths.RequestsFolder, $"start-{Guid.NewGuid():N}.json"), requestJson);

        Assert.True(
            await Run_Until_Async(engine, () => launcher.Shape != null, 20_000),
            "the start request was never processed, so this test never reached the code it is about."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        return launcher;
    }

    void Write_Config(string? orchestrationMode)
    {
        var defaults = orchestrationMode == null ? string.Empty : $$""","defaults":{"orchestrationMode":"{{orchestrationMode}}"}""";

        File.WriteAllText(
            _paths.ConfigFile,
            $$"""{"repos":[{"name":"Repo","path":{{System.Text.Json.JsonSerializer.Serialize(_tempRepo)}}}],"telegramSupergroupChatId":{{SUPERGROUP_CHAT_ID}},"telegramOwnerUserId":{{OWNER_USER_ID}}{{defaults}}}""");

        // The provider reloads on the write stamp, and two writes inside one filesystem tick would
        // otherwise serve the stale config — the same guard PrintRunnerTestHarness needs.
        File.SetLastWriteTimeUtc(_paths.ConfigFile, DateTime.UtcNow.AddSeconds(1));
    }

    string Read_GeneralChannel()
    {
        return File.Exists(_paths.GeneralChannelFile)
            ? string.Join("\n", ChannelEntry_Parser.Parse_All(File.ReadAllText(_paths.GeneralChannelFile)).Where(entry => entry.Author == ChannelAuthors.App).Select(entry => entry.RawText))
            : string.Empty;
    }

    static async Task<bool> Run_Until_Async(IBridgeEngine engine, Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);
        var satisfied = false;

        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
            {
                satisfied = true;
                break;
            }

            await Task.Delay(100);
        }

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }

        return satisfied || condition();
    }
}
