using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// An <c>ATTACH: &lt;path&gt;</c> line in an agent entry lands on the phone as a DOCUMENT, and a
/// path the policy refuses is refused out loud to the agent and never to the owner.
///
/// <para>
/// WHY (2026-09-07). A supervisor produced HTML mockups and could not deliver them: agents could
/// attach only images, and the bridge's document upload served only its own over-long entries. The
/// owner was told "I can't attach a file to Telegram" — which was true of the agent and false of
/// the app. Harness copied from <see cref="LongEntriesFoldOnThePhoneTests"/>, whose
/// <see cref="ByMethodTelegram_Fake"/> is the one fake that records documents.
/// </para>
/// </summary>
public class AttachmentsReachThePhoneTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4545;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly RecordingLog_Fake _log;
    readonly ByMethodTelegram_Fake _telegram;
    readonly IOrchestratorConfigProvider _configProvider;

    public AttachmentsReachThePhoneTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-attach-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},\"telegramOwnerUserId\":{OWNER_USER_ID}}}");
        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");
        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _log = new RecordingLog_Fake();
        _telegram = new ByMethodTelegram_Fake();
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AFileUnderTheRepo_ArrivesAsADocument_AndTheLineNeverReachesTheOwner()
    {
        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);
        var mockup = Path.Combine(_tempRepo, "mockups", "plan-card.html");
        Directory.CreateDirectory(Path.GetDirectoryName(mockup)!);
        File.WriteAllText(mockup, "<title>Plan card</title><p>before / after</p>");

        Append_SupervisorEntry(orchId, 1, "mockups", $"The plan-card mockup is ready.\nATTACH: {mockup}\nOpen it in a browser.");

        Assert.True(
            await Run_Until_Async(engine, () => _telegram.Documents().Count > 0, 20_000),
            $"no document arrived.{Environment.NewLine}{_telegram.Dump_HtmlSends()}{Environment.NewLine}{_log.Dump()}");

        var document = Assert.Single(_telegram.Documents());
        Assert.Equal("plan-card.html", document.FileName);
        Assert.Equal(File.ReadAllBytes(mockup), document.Content);
        Assert.Equal(TOPIC_ID, document.ThreadId);
        Assert.Contains("plan-card.html", document.CaptionHtml, StringComparison.Ordinal);

        var body = Assert.Single(_telegram.Html_Sends().Where(html => html.Contains("mockup is ready", StringComparison.Ordinal)));
        Assert.DoesNotContain("ATTACH:", body, StringComparison.Ordinal);
        Assert.Contains("Open it in a browser", body, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AFileOutsideTheAllowedRoots_IsRefusedToTheAgent_AndNothingIsUploaded()
    {
        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);
        var outside = Path.Combine(_tempRoot, "elsewhere.html");
        File.WriteAllText(outside, "<p>not from the repo</p>");

        Append_SupervisorEntry(orchId, 1, "elsewhere", $"Here it is.\nATTACH: {outside}");

        Assert.True(
            await Run_Until_Async(engine, () => Channel_Text(orchId).Contains("ATTACH refused", StringComparison.Ordinal), 20_000),
            $"the refusal never reached the channel.{Environment.NewLine}{_log.Dump()}");

        Assert.Empty(_telegram.Documents());
        Assert.True(_telegram.AnyHtmlSendContains("Here it is"), "the body must still reach the owner");

        var channel = Channel_Text(orchId);
        Assert.Contains("FROM app", channel, StringComparison.Ordinal);
        Assert.Contains(outside, channel, StringComparison.Ordinal);
        Assert.Contains("ATTACH refused", _log.Dump(), StringComparison.Ordinal);

        // The refusal is for the agent, never the phone.
        Assert.DoesNotContain(_telegram.Html_Sends(), html => html.Contains("ATTACH refused", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMissingFile_IsAWarningAndACoaching_NeverAThrow()
    {
        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);
        var missing = Path.Combine(_tempRepo, "never-written.csv");

        Append_SupervisorEntry(orchId, 1, "missing", $"Data attached.\nATTACH: {missing}\nNext step follows.");

        Assert.True(
            await Run_Until_Async(engine, () => Channel_Text(orchId).Contains("ATTACH refused", StringComparison.Ordinal), 20_000),
            _log.Dump());
        Assert.True(_telegram.AnyHtmlSendContains("Next step follows"), "the body must still reach the owner");
        Assert.Empty(_telegram.Documents());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnHtmlFileSentAsAPicture_IsRefusedToTheAgent_NamingTheMarkerThatWouldHaveWorked()
    {
        // THE NIGHT THIS WAS WRITTEN FOR. Four HTML mockups the owner had asked for went out as
        // IMAGE: lines; Telegram answered 400 IMAGE_PROCESS_FAILED to each; the only trace was a log
        // line, so the supervisor told the owner in good faith that it had sent them.
        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);
        var mockup = Path.Combine(_tempRepo, "mockups", "plan-card.html");
        Directory.CreateDirectory(Path.GetDirectoryName(mockup)!);
        File.WriteAllText(mockup, "<title>Plan card</title>");

        Append_SupervisorEntry(orchId, 1, "mockups", $"The mockups are ready.\nIMAGE: {mockup}");

        Assert.True(
            await Run_Until_Async(engine, () => Channel_Text(orchId).Contains("IMAGE refused", StringComparison.Ordinal), 20_000),
            $"the agent was never told.{Environment.NewLine}{_log.Dump()}");

        var channel = Channel_Text(orchId);
        Assert.Contains($"ATTACH: {mockup}", channel, StringComparison.Ordinal);
        Assert.Contains("IMAGE_PROCESS_FAILED", channel, StringComparison.Ordinal);

        // Nothing was handed to Telegram to reject, and the owner never saw the refusal.
        Assert.Empty(_telegram.Documents());
        Assert.DoesNotContain(_telegram.Html_Sends(), html => html.Contains("IMAGE refused", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AFileUnderTheOwnersMockupsFolder_IsSent_BecauseThatIsWhereAgentsPutThem()
    {
        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        // The real workflow writes owner-facing artefacts to ~/mockups/<row>/ — neither the
        // repository nor the channel folder, and refused by the first version of this policy.
        var mockups = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "mockups");
        Directory.CreateDirectory(mockups);
        var file = Path.Combine(mockups, $"aiorch-test-{Guid.NewGuid():N}.html");
        File.WriteAllText(file, "<title>from the owner's mockups folder</title>");

        try
        {
            Append_SupervisorEntry(orchId, 1, "mockups", $"Ready.\nATTACH: {file}");

            Assert.True(
                await Run_Until_Async(engine, () => _telegram.Documents().Count > 0, 20_000),
                $"the file under ~/mockups was refused.{Environment.NewLine}{Channel_Text(orchId)}{Environment.NewLine}{_log.Dump()}");

            Assert.Equal(Path.GetFileName(file), Assert.Single(_telegram.Documents()).FileName);
        }
        finally
        {
            File.Delete(file);
        }
    }

    string Channel_Text(string orchId)
    {
        return File.ReadAllText(_paths.Get_OwnerChannelFile(orchId));
    }

    IBridgeEngine Build_Engine()
    {
        return BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, _configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    async Task<string> Start_WithChannelAlreadySeen_Async(IBridgeEngine engine)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, "IS · attach");
        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(3));
        _telegram.Forget_Everything();
        return session.OrchId;
    }

    void Append_SupervisorEntry(string orchId, int index, string subject, string body)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        File.AppendAllText(channelFile, $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
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
