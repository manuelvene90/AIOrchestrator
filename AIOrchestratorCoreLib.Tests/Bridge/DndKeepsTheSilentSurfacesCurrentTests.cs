using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// DND HOLDS WHAT RINGS AND NOTHING ELSE — the owner's ruling of 2026-09-09 (brief C): *"🌙 holds
/// only what rings (supervisor entries, actionable alerts); PULSE and the dashboard keep updating
/// silently."*
///
/// <para>
/// WHAT IT WAS, AND WHY IT MATTERED. One `return` in the tick skipped everything under it once
/// app-wide DND was on: the tailer, every alert, PULSE and the General dashboard. Holding the alerts
/// and the mirrored entries is the whole point of the mode. Holding the other two was a defect that
/// only showed at the moment they were used — the owner's check-in ritual is "show me where
/// everything stands", and it was reading a PULSE and a dashboard frozen at the instant the mute
/// went on. A status surface that stops updating while the owner is away is not quiet, it is WRONG.
/// </para>
/// <para>
/// PROBED AGAINST THE ENGINE'S REAL LOOP, not against the planner, because the planner never saw
/// this: its own gates were already correct for a Deferred topic (it edits, silently) and the
/// engine returned before calling it. A unit test of the decision would have been green throughout.
/// </para>
/// <para>
/// AND THE SAME FIXTURE PINS THE OTHER HALF: the dashboard message carries General's command bar.
/// That bar was built and unit-tested on 2026-09-09 and never rendered — `Build_ForGeneral` had no
/// production caller — so a test that only asked "is the bar correct?" passed for eight months of
/// nothing being drawn. This asks what reached the client.
/// </para>
/// </summary>
public class DndKeepsTheSilentSurfacesCurrentTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 5151;

    /// <summary>Distinctive, and it is a plain report — nothing about it is an alert.</summary>
    const string SUPERVISOR_TEXT = "The migration finished and the suite is green.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly SurfaceRecordingTelegram_Fake _telegram;

    public DndKeepsTheSilentSurfacesCurrentTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-dnd-surfaces-tests-{Guid.NewGuid():N}");
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
        _telegram = new SurfaceRecordingTelegram_Fake();

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, log, _telegram, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
    }

    /// <summary>
    /// THE TWO CLAIMS TOGETHER, because either alone would pass for the wrong reason. "PULSE updated"
    /// alone is satisfied by a build that ignores DND entirely; "the entry was held" alone is
    /// satisfied by the old blanket return. The pair is the rule.
    ///
    /// The supervisor entry is a PLAIN REPORT — no question, no marker — so nothing about it is an
    /// actionable alert, and under 🌙 it is exactly the thing that must wait for the unmute.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderDnd_PulseKeepsUpdating_WhileTheSupervisorsWordsWait()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        // THE MUTE ITSELF CHANGES PULSE ONCE, and that one write must not be what this test measures.
        // Turning DND on puts 🌙 in the header, which is a text change like any other — so the mute
        // goes on FIRST and the surface is allowed to settle before the baseline is taken. A review
        // caught the earlier version of this test being carried entirely by that glyph: deleting the
        // channel append below left it green.
        _engine.Set_TelegramMuted(true);

        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(3));

        var pulseWritesBeforeTheEvent = _telegram.Count_Sent_Containing("PULSE");

        // AND THE EVENT HAS TO BE ONE PULSE CAN SEE. The decider answers None to identical text, so a
        // mute over a still orchestration produces no write however open the gate is — and a plain
        // supervisor entry is NOT visible to this surface: field 4 reads the member SPOKES, never
        // owner-channel.md. `STATE:` is the one thing a supervisor writes on the owner channel that
        // PULSE renders (field 2, the declared state), which is why the fixture declares one.
        Append_SupervisorEntry(
            orchId, 2, "migration done",
            $"{SUPERVISOR_TEXT}\nSTATE: waiting for imp-2's review, then I hand you the merge");

        var pulseUpdated = await Run_Until_Async(
            () => _telegram.Count_Sent_Containing("PULSE") > pulseWritesBeforeTheEvent, 25_000);

        Assert.True(
            pulseUpdated,
            $"PULSE never reported an event that happened during the mute — the owner's check-in reads a line frozen at the moment they left.\n{_telegram.Dump()}");

        // And it says the thing that happened, not merely something.
        Assert.True(
            _telegram.Has_Sent_Containing("waiting for imp-2's review"),
            $"PULSE was rewritten but never carried the state the supervisor declared during the mute.\n{_telegram.Dump()}");

        // The other half of the rule, and the half that must NOT change: the words themselves wait.
        Assert.False(
            _telegram.Has_Sent_Containing(SUPERVISOR_TEXT),
            $"a supervisor's entry reached the phone under DND — that is the traffic the mode exists to hold.\n{_telegram.Dump()}");
    }

    /// <summary>
    /// EVERY WRITE ON THIS SURFACE IS SILENT — the PREMISE the whole change rests on, not evidence
    /// for it. The gates could be narrowed only because a post here no longer wakes anybody; if one
    /// of these ever rings again, letting them through DND becomes the wrong call, and this is the
    /// test that says so first.
    ///
    /// IT IS A GUARD, AND IT PASSES WITH THE FIX REVERTED — deliberately. A guard whose job is "this
    /// never happens" cannot be mutation-checked against the change it guards, because removing the
    /// change removes the traffic too. Said plainly here so nobody counts it as proof of the DND
    /// behaviour: the probe above is what proves that.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task NothingTheseSurfacesSendEverMakesASound()
    {
        await Start_WithChannelAlreadySeen_Async();

        _engine.Set_TelegramMuted(true);

        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(3));

        // NOT-EMPTY FIRST, and a review is why. `DoesNotContain(Rings, …)` on its own passes both
        // because nothing rang and because nothing was SENT — decision 20's second clause, a state
        // with two routes to it, which pins neither. This makes the guard able to fail.
        Assert.NotEmpty(_telegram.Sounds_Sent());
        Assert.DoesNotContain(TelegramSendSounds.Rings, _telegram.Sounds_Sent());
    }

    /// <summary>
    /// THE GENERAL DASHBOARD, under the same mute, and WITH the owner's five buttons on it.
    ///
    /// Asserted on the callback DATA rather than the labels: the label is what the owner reads and
    /// the data is what a tap sends back, so a bar with the right captions and the wrong payloads
    /// looks perfect and does nothing. The payloads are also what the tap handler's switch matches.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderDnd_TheDashboardKeepsUpdating()
    {
        await Start_WithChannelAlreadySeen_Async();

        var dashboardWritesBeforeTheMute = _telegram.Count_Sent_Containing(GeneralDashboard_Composer.HEADING);

        _engine.Set_TelegramMuted(true);

        // A SECOND ORCHESTRATION IS THE CONTENT CHANGE. The dashboard's body is the progress report
        // across every open orchestration, and the decider writes only when the text moves — so, as
        // with PULSE, a mute over an unchanged machine produces no write whatever the gate says.
        // Starting one is the smallest thing that genuinely changes what the dashboard reads.
        _launcher.Start_Orchestration("Second", _tempRepo);

        var dashboardUpdated = await Run_Until_Async(
            () => _telegram.Count_Sent_Containing(GeneralDashboard_Composer.HEADING) > dashboardWritesBeforeTheMute, 25_000);

        Assert.True(
            dashboardUpdated,
            $"the General dashboard never updated after DND went on, so the check-in ritual reads a stale machine.\n{_telegram.Dump()}");
    }

    /// <summary>
    /// GENERAL'S BAR IS ACTUALLY ON THE DASHBOARD MESSAGE — nothing to do with DND, which is why it
    /// is its own test. The bar was built and unit-tested on 2026-09-09 and never rendered:
    /// `Build_ForGeneral` had no production caller, so a test that only asked "is the bar correct?"
    /// was green over eight months of nothing being drawn. This asks what reached the client.
    ///
    /// ASSERTED ON THE CALLBACK DATA, not the labels: the label is what the owner reads and the data
    /// is what a tap sends back, so a bar with the right captions and the wrong payloads looks
    /// perfect and does nothing. The payloads are also what the tap handler's switch matches on.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheDashboardCarriesGeneralsOwnBar()
    {
        await Start_WithChannelAlreadySeen_Async();

        var barArrived = await Run_Until_Async(
            () => _telegram.Find_ButtonDataFor(GeneralDashboard_Composer.HEADING) != null, 25_000);

        Assert.True(barArrived, $"the General dashboard reached the client with no command bar on it.\n{_telegram.Dump()}");

        Assert.Equal(
            TopicCommandButtons.GeneralCommands.Select(command => $"cmd:{command}:0"),
            _telegram.Find_ButtonDataFor(GeneralDashboard_Composer.HEADING)!);
    }

    async Task<string> Start_WithChannelAlreadySeen_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(3));

        return session.OrchId;
    }

    void Append_SupervisorEntry(string orchId, int index, string subject, string body)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(
            _paths.Get_OwnerChannelFile(orchId),
            $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
    }

    async Task<bool> Run_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);
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

/// <summary>
/// Records the three things these probes are about and keeps them apart: the TEXT that reached the
/// client, the SOUND each send chose, and the BUTTON PAYLOADS that rode along. Edits are recorded
/// too — the status surfaces edit far more often than they post, so a fake that watched only sends
/// would report both of them frozen no matter what the engine did.
/// </summary>
internal sealed class SurfaceRecordingTelegram_Fake : ITelegramApiClient
{
    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    readonly object _lock = new();
    readonly List<(string Text, TelegramSendSounds? Sound, IReadOnlyList<string> ButtonData)> _written = [];
    long _nextMessageId = 6100;

    public Task<string> Get_BotUsername_Async(CancellationToken cancellationToken) => Task.FromResult("test_bot");

    public Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken) => Task.CompletedTask;

    public bool Has_Sent_Containing(string fragment)
    {
        lock (_lock)
            return _written.Any(written => written.Text.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// HOW MANY writes carried the fragment, not whether any did — and that distinction is the whole
    /// difference between these probes proving something and proving nothing. The first version of
    /// this fixture asked `Has_Sent_Containing("PULSE")` after turning DND on, and PULSE had already
    /// been written three ticks earlier while the topic was still Normal: the assertion was satisfied
    /// by traffic that predated the mute, and it passed with the fix reverted.
    /// </summary>
    public int Count_Sent_Containing(string fragment)
    {
        lock (_lock)
            return _written.Count(written => written.Text.Contains(fragment, StringComparison.Ordinal));
    }

    public IReadOnlyList<TelegramSendSounds> Sounds_Sent()
    {
        lock (_lock)
            return [.. _written.Where(written => written.Sound != null).Select(written => written.Sound!.Value)];
    }

    /// <summary>The payloads on the LAST write whose text contains the fragment, or null if none did.</summary>
    public IReadOnlyList<string>? Find_ButtonDataFor(string fragment)
    {
        lock (_lock)
        {
            for (var index = _written.Count - 1; index >= 0; index--)
            {
                if (_written[index].Text.Contains(fragment, StringComparison.Ordinal))
                    return _written[index].ButtonData;
            }

            return null;
        }
    }

    public string Dump()
    {
        lock (_lock)
            return string.Join(
                Environment.NewLine,
                _written.Select(written => $"  [{written.Sound?.ToString() ?? "edit"}] {written.Text.Replace("\n", " ⏎ ")} {{{string.Join(", ", written.ButtonData)}}}"));
    }

    long Record(string text, TelegramSendSounds? sound, IReadOnlyList<string>? buttonData = null)
    {
        lock (_lock)
        {
            _written.Add((text, sound, buttonData ?? []));
            return _nextMessageId++;
        }
    }

    static IReadOnlyList<string> Flatten(IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows)
    {
        return [.. buttonRows.SelectMany(row => row).Select(button => button.Data)];
    }

    static IReadOnlyList<string> Flatten(IReadOnlyList<(string Data, string Label)> buttons)
    {
        return [.. buttons.Select(button => button.Data)];
    }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult<long?>(Record(text, sound));

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult<long?>(Record(html, sound));

    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult<long?>(Record(text, sound, Flatten(buttons)));

    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult<long?>(Record(text, sound, Flatten(buttonRows)));

    public Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
        => Task.FromResult<long?>(Record(html, sound, Flatten(buttons)));

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
    {
        Record(text, null);
        return Task.CompletedTask;
    }

    public Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken)
    {
        Record(html, null);
        return Task.CompletedTask;
    }

    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken)
    {
        Record(text, null, Flatten(buttons));
        return Task.CompletedTask;
    }

    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
    {
        Record(text, null, Flatten(buttonRows));
        return Task.CompletedTask;
    }

    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        // THE DELAY IS NOT OPTIONAL. A fake that answers instantly spins the inbound loop as fast as
        // the scheduler allows and starves the machine — it cost 95 minutes of a hung suite once.
        await Task.Delay(50, cancellationToken);

        return EMPTY_UPDATES;
    }

    public Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken) => Task.FromResult(7878L);

    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken) => Task.CompletedTask;


    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken) => Task.FromResult<byte[]>([]);
}
