using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE OWNER ASKED, THE SUPERVISOR ANSWERED AT LENGTH, AND THE OWNER NEVER SAW IT (VPS,
/// 2026-09-07).
///
/// <para>
/// An owner-facing entry being long is a SPLITTING problem. It is never a reason for the owner to
/// receive nothing, and it is never a reason for the bridge to say so instead of delivering: the
/// "that message was too long for a phone" note is coaching addressed to the supervisor, and if it
/// ever becomes the thing that happens INSTEAD of the message, this system has failed at the one
/// job it exists for.
/// </para>
/// <para>
/// Driven end to end rather than poked, because "nothing was dropped" is only observable as the
/// sequence of calls the Telegram client received.
/// </para>
/// </summary>
public class ALongAnswerIsSplitNotDroppedTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4343;

    /// <summary>Lines the renderer leaves alone, so "every line arrived" is an exact assertion rather than a rendering question.</summary>
    const int LINE_COUNT = 150;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly RecordingLog_Fake _log;
    readonly ByMethodTelegram_Fake _telegram;
    readonly IOrchestratorConfigProvider _configProvider;

    public ALongAnswerIsSplitNotDroppedTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-long-answer-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

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
    public async Task ANineThousandCharacterAnswer_ArrivesWhole_InSeveralNumberedMessages_InOrder()
    {
        var lines = Enumerable.Range(1, LINE_COUNT)
            .Select(i => $"finding {i:D3} — the measurement, what it means, and what it costs to leave alone")
            .ToList();

        // The closing question is what makes OwnerPush_Policy push it at all; pure narration is kept
        // off the phone by design and the assertions below would then pass or fail for the wrong reason.
        var body = string.Join('\n', lines) + "\nShall I proceed?";

        Assert.True(body.Length > 9_000, $"the fixture is only {body.Length} characters — it would not split");

        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        Append_SupervisorEntry(orchId, 1, "the sweep", body);

        Assert.True(
            await Run_Until_Async(engine, () => Delivered(lines), 25_000),
            "THE ANSWER WAS LOST OR TRUNCATED. Every line the supervisor wrote has to reach the phone; "
                + "an entry being long is a splitting problem, never a reason the owner gets nothing."
                + $"{Environment.NewLine}html sends ({_telegram.Html_Sends().Count}): {_telegram.Dump_HtmlSends()}"
                + $"{Environment.NewLine}{_log.Dump()}");

        var sends = _telegram.Html_Sends().Where(html => html.Contains("finding ", StringComparison.Ordinal)).ToList();

        Assert.True(sends.Count >= 3, $"a {body.Length}-character answer became {sends.Count} message(s)");

        // NUMBERED, and in order: several unlabelled messages in a row read as several separate
        // statements, and the second one starting mid-sentence reads as a glitch — which is how a
        // delivered message still gets reported as lost.
        for (var i = 0; i < sends.Count; i++)
        {
            Assert.StartsWith(
                OwnerMessage_Chunker.Build_Marker(i + 1, sends.Count).Trim(),
                sends[i],
                StringComparison.Ordinal);
        }

        // IN ORDER, on the content and not only on the markers: the pieces must reconstruct the
        // entry, not merely announce that they are pieces.
        var arrivalOrder = lines.Select(line => sends.FindIndex(html => html.Contains(line, StringComparison.Ordinal))).ToList();

        Assert.Equal(arrivalOrder.OrderBy(index => index).ToList(), arrivalOrder);
    }

    /// <summary>
    /// AND THE NOTE IS NOT A SUBSTITUTE FOR THE MESSAGE. It stays — it is the brevity feedback loop
    /// the owner asked for — but it is addressed to the supervisor, never texted, and it now says in
    /// its own words that the message was delivered, because on 2026-09-07 it was read as a refusal
    /// and the supervisor stopped re-sending an answer the owner already had.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheTooLongNote_GoesToTheSupervisor_AndSaysTheMessageWasDelivered()
    {
        var body = string.Join('\n', Enumerable.Range(1, LINE_COUNT).Select(i => $"finding {i:D3} — a line")) + "\nShall I proceed?";

        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        Append_SupervisorEntry(orchId, 1, "the sweep", body);

        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        Assert.True(
            await Run_Until_Async(engine, () => File.ReadAllText(channelFile).Contains("too long for a phone", StringComparison.Ordinal), 25_000),
            $"the brevity note never arrived.{Environment.NewLine}{_log.Dump()}");

        var channel = File.ReadAllText(channelFile);

        Assert.Contains("nothing was dropped", channel);
        Assert.DoesNotContain("finding 001", _telegram.Dump_PlainSends());
        Assert.True(_telegram.AnyHtmlSendContains("finding 001"), $"the entry itself never went out: {_telegram.Dump_HtmlSends()}");
    }

    bool Delivered(IReadOnlyList<string> lines)
    {
        var sent = string.Concat(_telegram.Html_Sends());

        foreach (var line in lines)
        {
            if (!sent.Contains(line, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    IBridgeEngine Build_Engine()
    {
        return BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, _configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    /// <summary>
    /// The tailer registers a file it has never seen at its CURRENT END, so an entry appended before
    /// the first poll is behind the starting offset and never mirrors — a setup mistake that looks
    /// exactly like the defect under test.
    /// </summary>
    async Task<string> Start_WithChannelAlreadySeen_Async(IBridgeEngine engine)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, "IS · long answer");

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(3));

        _telegram.Forget_Everything();

        return session.OrchId;
    }

    void Append_SupervisorEntry(string orchId, int index, string subject, string body)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(
            _paths.Get_OwnerChannelFile(orchId),
            $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
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
