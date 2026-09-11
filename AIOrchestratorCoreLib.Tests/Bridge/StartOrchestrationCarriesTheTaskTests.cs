using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Time.Clock;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE APP IS THE ONE THAT WRITES THE TASK, and it writes it AFTER the launch.
///
/// <para>
/// Both halves are load-bearing. The first keeps the general supervisor's read-only rule intact: it
/// files a request, it does not touch another orchestration's channel. The second is the whole
/// mechanism — a bridge-driven session baselines its channels at REGISTRATION, so a task written
/// before <c>Start_Orchestration</c> returns would be absorbed as HISTORY, start no turn, and leave
/// the crew sitting exactly where the live round of 2026-09-06 left it: up, complete, and idle.
/// </para>
/// <para>
/// The ordering is asserted through a launcher that snapshots the channel at the instant it hands the
/// session back, because the alternatives all pass for the wrong reason: an index, a count and a
/// "contains" are equally true whichever side of the launch the entry was written on.
/// </para>
/// </summary>
public class StartOrchestrationCarriesTheTaskTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const string TASK = "Create and commit HELLO.md";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly RecordingLog_Fake _log = new();
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();

    public StartOrchestrationCarriesTheTaskTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-starttask-engine-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // The inbound loop needs the chat and owner ids or it throws. The runners are
        // left at their default — TERMINAL — deliberately: a bridge-driven role would have the engine's
        // own dispatcher reach for a real `claude`, which is a second subject and a second way to fail.
        File.WriteAllText(
            _paths.ConfigFile,
            $$"""{"repos":[{"name":"Repo","path":{{System.Text.Json.JsonSerializer.Serialize(_tempRepo)}}}],"telegramSupergroupChatId":{{SUPERGROUP_CHAT_ID}},"telegramOwnerUserId":{{OWNER_USER_ID}}}""");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheTaskLandsInTheNEWOrchestrationsOwnerChannel_AsTheOwnersOwnFirstEntry_AFTERTheLaunch()
    {
        var launcher = new LaunchWitness_Fake(Build_RealLauncher(), _paths);
        var engine = Build_Engine(launcher);

        Write_Request($$"""{"action":"start-orchestration","repo":"Repo","mode":"full","task":{{System.Text.Json.JsonSerializer.Serialize(TASK)}}}""");

        Assert.True(
            await Run_Until_Async(engine, () => launcher.StartedOrchId != null && Owner_Entries(launcher.StartedOrchId).Count > 0, 20_000),
            "the start request was never processed, so this test never reached the code it is about."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        var orchId = launcher.StartedOrchId!;
        var entry = Assert.Single(Owner_Entries(orchId));

        // FROM owner — the author the app already uses for everything the owner types into a topic.
        // Nothing new was invented to carry this, which is why the supervisor needs no new rule to
        // recognise it: its first task arrives in exactly the shape every later one does.
        Assert.Equal(ChannelAuthors.Owner, entry.Author);
        Assert.Contains(TASK, entry.RawText, StringComparison.Ordinal);

        // THE ORDER, pinned where it is decided. At the moment the launcher handed the session back —
        // after the supervisor was registered and its channels baselined — the task was not there yet.
        Assert.DoesNotContain(TASK, launcher.OwnerChannelWhenTheLaunchReturned, StringComparison.Ordinal);

        // AND THE OWNER IS RECORDED AS WAITING FOR THE ANSWER. Without this the crew's first reply is
        // filtered as narration and never reaches the phone: the flag is normally raised by the owner
        // typing into a TOPIC, and this owner spoke to the concierge instead. Asserted through the
        // persisted snapshot rather than a field, because surviving a restart is half the claim.
        Assert.Contains(orchId, _engineState.Load_OrEmpty().OwnerAwaitingAnswer);
    }

    /// <summary>
    /// A request with no task still starts an orchestration and writes NOTHING into its channel — the
    /// old shape, unchanged. Worth its own test because "always append" would file an empty entry and
    /// wake a session with a message the owner never sent.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ARequestWithoutATask_LeavesTheOwnerChannelEmpty()
    {
        var launcher = new LaunchWitness_Fake(Build_RealLauncher(), _paths);
        var engine = Build_Engine(launcher);

        Write_Request("""{"action":"start-orchestration","repo":"Repo","mode":"full"}""");

        Assert.True(
            await Run_Until_Async(engine, () => launcher.StartedOrchId != null, 20_000),
            $"the start request was never processed.{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        // Given a moment in which it COULD have appeared — asserting an absence the instant after the
        // launch would pass before the code that writes it had run at all.
        await Task.Delay(500);

        Assert.Empty(Owner_Entries(launcher.StartedOrchId!));

        // And no wait is recorded either: nothing was asked, so nothing is owed an answer. A flag
        // raised here would push the next piece of narration the crew wrote, for ever.
        Assert.DoesNotContain(launcher.StartedOrchId!, _engineState.Load_OrEmpty().OwnerAwaitingAnswer);
    }

    IOrchestrationLauncher Build_RealLauncher()
    {
        return OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);
    }

    IBridgeEngine Build_Engine(IOrchestrationLauncher launcher)
    {
        return BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, launcher, _log, new CapturingTelegram_Fake(),
            _engineState,
            Clock_Factory.Create_System(),
            BridgeTestTiming.Fast());
    }

    IReadOnlyList<IChannelEntry> Owner_Entries(string orchId)
    {
        var file = _paths.Get_OwnerChannelFile(orchId);

        return File.Exists(file) ? ChannelEntry_Parser.Parse_All(File.ReadAllText(file)) : [];
    }

    void Write_Request(string json)
    {
        File.WriteAllText(Path.Combine(_paths.RequestsFolder, "start.json"), json);
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
