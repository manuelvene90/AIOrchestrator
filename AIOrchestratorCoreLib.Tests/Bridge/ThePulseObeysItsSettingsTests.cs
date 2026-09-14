using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Usage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE ENGINE HANDS PULSE WHAT THE OWNER CONFIGURED (plan 03 Task 4). The builder and the planner are
/// pure and pinned in <c>PulseFieldsAreConfigurableTests</c>; what only an engine test can see is the
/// ONE call site that resolves <c>pulse.fields</c> and <c>pulse.stepMinutes</c> from the provider and
/// reads every session's reported model. <c>BridgeEngineModel</c> is internal sealed, so an argument
/// dropped there — the list, the step, the supervisor's reading, a member's — would leave every pure
/// test green while the owner's phone ignored the setting.
///
/// <para>
/// EACH VALUE HAS ONE ROUTE TO THE ASSERTED TEXT (decision 20). The step is 15, and the member was
/// briefed forty minutes ago: fifteen floors that to "30 min", the shipped five to "40 min". The list
/// leaves out `updated`, which every list the engine could fall back to (classic or the catalogue's)
/// includes. The model readings exist only in the probe files this fixture writes.
/// </para>
/// </summary>
public class ThePulseObeysItsSettingsTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    const string TASK = "wiring the context field";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    public ThePulseObeysItsSettingsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-pulse-settings-engine-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _log = new RecordingLog_Fake();
        _telegram = new FailableTelegram_Fake();
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheEngineHandsThePulseTheConfiguredFieldsAndStep_AndEverySessionsReportedModel()
    {
        var engine = Build_Engine("\"pulse\":{\"fields\":[\"supervisor\",\"members\",\"modelEffort\"],\"stepMinutes\":15}");

        var pulse = await Run_UntilThePulseIsPosted_Async(engine);

        Assert.Equal(
            $"PULSE\nsup · Fable 5.1 xhigh\n• imp-1 · {TASK} · working · 30 min · Opus 5 high\n• rev-1 · standing by",
            pulse);
    }

    /// <summary>
    /// A MACHINE THAT STATES NOTHING GETS CLASSIC'S PULSE — master's, with the model readings on — and
    /// the shipped five-minute step. The merged row is the launcher's seeded PLAN.md (one open line).
    /// The heartbeat's clock is the wall clock, so that line is asserted by its opening only.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WithNoPulseKeys_TheEngineDrawsClassicsList()
    {
        var engine = Build_Engine(extraConfigJson: null);

        var lines = (await Run_UntilThePulseIsPosted_Async(engine)).Split('\n');

        Assert.Equal(6, lines.Length);
        Assert.Equal("PULSE", lines[0]);
        Assert.Equal("sup · Fable 5.1 xhigh", lines[1]);
        Assert.Equal($"• imp-1 · {TASK} · working · 40 min · Opus 5 high", lines[2]);
        Assert.Equal("• rev-1 · standing by", lines[3]);
        Assert.Equal("0/1 merged · 0 %", lines[4]);
        Assert.StartsWith("updated ", lines[5], StringComparison.Ordinal);
    }

    /// <summary>
    /// A NEW PROVIDER PER FACT, with the keys written before it exists — the provider caches on the
    /// file's write stamp, so a rewrite under an old one could measure the previous file.
    /// </summary>
    IBridgeEngine Build_Engine(string? extraConfigJson)
    {
        var extra = extraConfigJson == null ? "" : $",{extraConfigJson}";

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}{extra}}}");

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);

        Seed_SupervisedCrew(launcher);

        return BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    /// <summary>
    /// A supervisor and imp-1, each with a probe file naming its model, and imp-1's brief stamped forty
    /// minutes ago. Everything is on disk before the engine's first tick, so the first PULSE it
    /// posts is the one that describes this crew.
    /// </summary>
    void Seed_SupervisedCrew(IOrchestrationLauncher launcher)
    {
        var session = launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        // A new orchestration starts with imp-1 and rev-1 (the launcher's owner directive). Only imp-1
        // is briefed and reports a model, so rev-1's row is the control: standing by, and no reading.
        const string memberId = "imp-1";

        Assert.Contains(session.Members, member => member.MemberId == memberId);

        Write_ModelProbe(_paths.Get_OrchestrationFolder(session.OrchId), "Fable 5.1", "xhigh");
        Write_ModelProbe(_paths.Get_ImplementerFolder(session.OrchId, memberId), "Opus 5", "high");

        var briefedAt = DateTime.Now.AddMinutes(-40).ToString("yyyy-MM-dd HH:mm");

        File.WriteAllText(
            MemberChannel_Locator.Get_ChannelFile(_paths, session.OrchId, memberId),
            $"## [1] FROM supervisor — {briefedAt} — {TASK}\n\ngo\n");
    }

    static void Write_ModelProbe(string sessionFolder, string modelDisplayName, string effortLevel)
    {
        Directory.CreateDirectory(sessionFolder);

        File.WriteAllText(
            Path.Combine(sessionFolder, UsageTotals_Reader.SESSION_USAGE_FILE),
            $"{{\"model\":{{\"display_name\":\"{modelDisplayName}\"}},\"effort\":{{\"level\":\"{effortLevel}\"}}}}");
    }

    async Task<string> Run_UntilThePulseIsPosted_Async(IBridgeEngine engine)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);
        string? pulse = null;

        for (var waited = 0; waited < 20_000 && pulse == null; waited += 100)
        {
            pulse = _telegram.Sent_Texts().FirstOrDefault(text => text.StartsWith("PULSE", StringComparison.Ordinal));

            if (pulse == null)
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

        return pulse
            ?? throw new Exception($"no PULSE was ever posted.{Environment.NewLine}{string.Join(Environment.NewLine, _telegram.Sent_Texts())}{Environment.NewLine}{_log.Dump()}");
    }
}
