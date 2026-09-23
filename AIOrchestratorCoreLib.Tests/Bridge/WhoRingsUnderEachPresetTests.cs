using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// WHAT RINGS THE OWNER'S PHONE UNDER EACH SHIPPED PRESET — <c>phone.push</c> decides what is SENT
/// (Task 2), <c>phone.appMessagesRing</c> decides what RINGS (plan 03 Task 3). Driven through the real
/// engine: the sound lives only in the argument beside each send, and the fake records it.
///
/// <para>
/// §7.1: *"Sound is separate."* Two questions, two keys, and the two presets answer both differently.
/// <b>classic</b> (Manu's, and the machine that names no preset) is <c>filtered</c> + <c>true</c>: only
/// what matters is sent, and it rings. <b>quiet</b> (Nathan's) is <c>everything</c> + <c>false</c>: every
/// supervisor entry is sent, and under D7's answer (b) — recorded 2026-09-14 — only a question, a
/// <c>BLOCKED ON OWNER</c>, a file or the answer rings; the rest of the narration arrives silently.
/// </para>
/// <para>
/// WHAT THE APP WRITES ABOUT ITSELF RINGS UNDER NEITHER, and it was already so before this task: the
/// receipt, the busy narration and the turn-ended line. Pinned here so a later task cannot read
/// <c>appMessagesRing = true</c> literally ("the app's own messages ring") and quietly make classic's
/// phone louder. ONE STATED EXCEPTION, ruling R8: under <c>filtered</c> a turn-end completion that
/// CARRIES held words rings — those are the supervisor's words reaching the owner for the first time.
/// A completion with nothing held sends nothing at all.
/// </para>
/// <para>
/// Step 1's measurement, 2026-09-14: no app-written receipt, narration or turn-ended line rang on this
/// tree, so D7's reading (a) would have been a no-op. The sites that DO ring and are not agent entries
/// are alerts (loop abandoned, crash loop, stall, budget, usage limit, the undelivered digest), the
/// close confirmation and the button question — none is governed by this key.
/// </para>
/// </summary>
public class WhoRingsUnderEachPresetTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445577;
    const long OWNER_USER_ID = 555000177;
    const long TOPIC_ID = 5353;

    const string OWNER_TEXT = "is the rebuild done";

    /// <summary>Not a question, no marker — rung only because the owner is waiting for it.</summary>
    const string ANSWER_TEXT = "Answering you: the rebuild finished clean on the merged tree.";

    /// <summary>Progress, nothing asked, sent after the answer spent the credit.</summary>
    const string NARRATION_TEXT = "Running the integration suite against the rebuilt images now.";

    const string SECOND_NARRATION_TEXT = "Integration suite is halfway through, nothing red so far.";

    /// <summary>A question in prose. Deliberately NOT QUESTION:/OPTION: — a button question raises the question hold.</summary>
    const string QUESTION_TEXT = "Should the retry cap be three or five?";

    const string BLOCKED_TEXT = "BLOCKED ON OWNER: the deploy key is missing from the vault.";

    const string FILE_BODY_TEXT = "The latency report you asked for is attached.";
    const string FILE_NAME = "latency-report.csv";

    const string TURN_ENDED = "turn ended";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly SoundRecordingTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    // NOT READONLY for one reason: each fact names its preset, and Use_Preset builds these once, before
    // the engine has ever run. Nothing else assigns them.
    IOrchestrationLauncher? _launcher;
    IBridgeEngine? _engine;

    /// <summary>Which phone the fixture is under — it decides what "the receipt landed" looks like (plan 03 Task 6).</summary>
    string? _preset;

    public WhoRingsUnderEachPresetTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-who-rings-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _log = new RecordingLog_Fake();
        _telegram = new SoundRecordingTelegram_Fake();
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// CLASSIC: THE ANSWER AND A QUESTION RING; NARRATION IS NOT SENT AT ALL — and reaches the owner
    /// once, inside the turn-end completion, which rings (R8, the stated exception).
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderClassic_AQuestionRings_AndNarrationIsNotSentAtAll()
    {
        Use_Preset(Presets_Loader.CLASSIC);

        var orchId = await Start_WithTheOwnerWaiting_Async();

        Append_SupervisorEntry(orchId, 2, "the rebuild", ANSWER_TEXT);
        await Wait_Sent_Async(ANSWER_TEXT);

        Append_SupervisorEntry(orchId, 3, "retry cap", QUESTION_TEXT);
        await Wait_Sent_Async(QUESTION_TEXT);

        Append_SupervisorEntry(orchId, 4, "integration suite", NARRATION_TEXT);

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("entry #4 FROM Supervisor"), 20_000),
            $"the narration was never tailed, so its absence below proves nothing.{Environment.NewLine}{_log.Dump()}");

        Mark_SessionIdle(orchId);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(NARRATION_TEXT), 30_000),
            $"the turn ended and the held narration never reached the owner.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");

        // NOT SENT AT ALL on its own: the first and only send carrying it is the completion.
        Assert.Equal(1, _telegram.Count_Sent_Containing(NARRATION_TEXT));
        var completion = _telegram.Find_Sent_Containing(NARRATION_TEXT)!.Value;
        Assert.Contains(TURN_ENDED, completion.Text, StringComparison.Ordinal);

        Assert.Equal(TelegramSendSounds.Rings, Sound_Of(ANSWER_TEXT));
        Assert.Equal(TelegramSendSounds.Rings, Sound_Of(QUESTION_TEXT));

        // R8 — THE STATED EXCEPTION: a completion carrying held words rings.
        Assert.Equal(TelegramSendSounds.Rings, completion.Sound);

        Assert_NothingElseRang(ANSWER_TEXT, QUESTION_TEXT, NARRATION_TEXT);
    }

    /// <summary>
    /// QUIET: EVERYTHING IS SENT, AND ONLY THE ANSWER, A QUESTION, A BLOCKED OR A FILE RINGS (D7 b).
    /// The turn then ends with nothing held, and the turn-ended line is not sent — so it cannot ring.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderQuiet_EverythingIsSent_AndOnlyTheAnswerAQuestionABlockedOrAFileRings()
    {
        Use_Preset(Presets_Loader.QUIET);

        var orchId = await Start_WithTheOwnerWaiting_Async();

        var reportPath = Path.Combine(_tempRepo, FILE_NAME);
        File.WriteAllText(reportPath, "endpoint,p50,p99\n/orders,12,48\n");

        Append_SupervisorEntry(orchId, 2, "the rebuild", ANSWER_TEXT);
        await Wait_Sent_Async(ANSWER_TEXT);

        Append_SupervisorEntry(orchId, 3, "integration suite", NARRATION_TEXT);
        await Wait_Sent_Async(NARRATION_TEXT);

        Append_SupervisorEntry(orchId, 4, "retry cap", QUESTION_TEXT);
        await Wait_Sent_Async(QUESTION_TEXT);

        Append_SupervisorEntry(orchId, 5, "deploy key", BLOCKED_TEXT);
        await Wait_Sent_Async(BLOCKED_TEXT);

        Append_SupervisorEntry(orchId, 6, "latency report", $"{FILE_BODY_TEXT}\nATTACH: {reportPath}");
        await Wait_Sent_Async(FILE_NAME);

        Append_SupervisorEntry(orchId, 7, "integration suite", SECOND_NARRATION_TEXT);
        await Wait_Sent_Async(SECOND_NARRATION_TEXT);

        Assert.Equal(TelegramSendSounds.Rings, Sound_Of(ANSWER_TEXT));
        Assert.Equal(TelegramSendSounds.Silent, Sound_Of(NARRATION_TEXT));
        Assert.Equal(TelegramSendSounds.Rings, Sound_Of(QUESTION_TEXT));
        Assert.Equal(TelegramSendSounds.Rings, Sound_Of("the deploy key is missing"));
        Assert.Equal(TelegramSendSounds.Rings, Sound_Of(FILE_BODY_TEXT));
        Assert.Equal(TelegramSendSounds.Rings, Sound_Of(FILE_NAME));
        Assert.Equal(TelegramSendSounds.Silent, Sound_Of(SECOND_NARRATION_TEXT));

        var ringsBeforeTheTurnEnd = _telegram.Count_Sent_WithSound(TelegramSendSounds.Rings);

        Mark_SessionIdle(orchId);

        // The engine's own account of the branch taken, so the silence after it is not a missed tick.
        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("nothing further to say, nothing sent"), 30_000),
            $"the turn end was never announced, so its silence proves nothing.{Environment.NewLine}{_log.Dump()}");

        Assert.False(_telegram.Has_Sent_Containing(TURN_ENDED), _telegram.Dump_Sent());
        Assert.Equal(ringsBeforeTheTurnEnd, _telegram.Count_Sent_WithSound(TelegramSendSounds.Rings));

        Assert_NothingElseRang(ANSWER_TEXT, QUESTION_TEXT, "the deploy key is missing", FILE_BODY_TEXT, FILE_NAME);
    }

    /// <summary>
    /// THE BUSY NARRATION AND THE RECEIPT TICK ARE SILENT BY CONSTRUCTION, and read no setting.
    ///
    /// <para>
    /// The narration is not reachable from an engine test: the first busy narration waits
    /// <c>NARRATION_FIRST_DELAY_SECONDS = 180</c>, a compiled constant outside the injected timing. So this
    /// reads the source, with the honesty of <see cref="BusyNoticeRespectsAnAnswerScanTests"/>: it proves
    /// the send is written silent, not that it fires. The ✓ IS reachable since plan 03 Task 6 made
    /// <c>phone.receipts</c> choose — classic's receipt is that message — and
    /// <see cref="Start_WithTheOwnerWaiting_Async"/> measures its silence through the engine; its scan
    /// stays as the structural half.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("async Task<long?> Send_NarrationMessage_OrNull_Async(")]
    [InlineData("async Task Send_ReceivedAck_Async(")]
    public void UnderEveryPreset_AnAppSend_IsWrittenSilent_AndReadsNoRingSetting(string signatureMark)
    {
        var body = Extract_EngineMethod(signatureMark);

        // The scan proves it found a send before judging its sound.
        Assert.Contains("TelegramSendSounds.Silent", body);
        Assert.DoesNotContain("TelegramSendSounds.Rings", body);
        Assert.DoesNotContain("AppMessagesRing", body);
    }

    /// <summary>
    /// THE TURN-ENDED LINE IS SILENT EXCEPT WHERE IT CARRIES HELD WORDS (R8). The unanswered line needs a
    /// busy narration first, which is the 180-second constant above, so its sound is pinned here: the
    /// builder rings in exactly one place, and that place returns the held words.
    /// </summary>
    [Fact]
    public void UnderEveryPreset_TheTurnEndedLine_RingsOnlyWhenItCarriesHeldWords()
    {
        var body = Extract_EngineMethod("(string? Text, bool IsCompletion, TelegramSendSounds Sound) Build_TurnEndedText(");

        var lines = body.Split('\n');
        var ringing = lines.Where(line => line.Contains("TelegramSendSounds.Rings", StringComparison.Ordinal)).ToList();

        Assert.Contains(lines, line => line.Contains("turn ended — free now", StringComparison.Ordinal) && line.Contains("TelegramSendSounds.Silent", StringComparison.Ordinal));
        Assert.Single(ringing);
        Assert.Contains("{lastWords}", ringing[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes the preset into config.json and builds the engine on it. QUIET NAMES BRIDGE-DRIVEN RUNNERS
    /// (print/stream), and a registered print session makes the engine's dispatcher reach for a LIVE
    /// <c>claude</c> — so every role is pinned back to the terminal runner here, config.json outranking
    /// the preset, and the fixture refuses to run if that did not take (decision 20).
    /// </summary>
    void Use_Preset(string preset)
    {
        _preset = preset;

        var terminalRunners = string.Join(
            ",",
            SessionRole_Names.ALL.Select(role => $"\"{SessionRole_Names.Get_ConfigKey(role)}\":{{\"runner\":\"terminal\"}}"));

        // The inbound loop reads the chat and owner ids and throws without them.
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"preset\":\"{preset}\",\"runners\":{{{terminalRunners}}}}}");

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var config = configProvider.Get_Current();

        foreach (var role in SessionRole_Names.ALL)
            Assert.Equal(SessionRunners.Terminal, config.Runners.Get_ForRole(role).Runner);

        // THE FIXTURE IS UNDER THE PRESET IT NAMES, or every assertion below measures the wrong phone.
        if (preset == Presets_Loader.QUIET)
        {
            Assert.Equal(PhonePushModes.Everything, config.Phone.Push);
            Assert.False(config.Phone.AppMessagesRing);
        }
        else
        {
            Assert.Equal(PhonePushModes.Filtered, config.Phone.Push);
            Assert.True(config.Phone.AppMessagesRing);
        }

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    /// <summary>
    /// Channel seen, session mid-turn, the owner's message DELIVERED — which raises their credit — and its
    /// receipt landed. Nothing has rung: the receipt is the first thing the app writes, and it is silent.
    /// The receipt is each preset's own (plan 03 Task 6): quiet's is a reaction on the owner's message,
    /// classic's is the ✓ message — a send, so its silence is measured here rather than assumed.
    /// </summary>
    async Task<string> Start_WithTheOwnerWaiting_Async()
    {
        var session = (_launcher ?? throw new Exception("Use_Preset was not called")).Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(3));

        Mark_SessionMidTurn(session.OrchId);

        _telegram.Queue_Updates(Build_OwnerMessageJson(OWNER_TEXT));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Owner message delivered") && Has_ReceiptLanded(), BridgeTestTiming.Window_ForAggregation(60)),
            $"the owner's message was never delivered and acknowledged.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");

        if (_preset != Presets_Loader.QUIET)
            Assert.Equal(TelegramSendSounds.Silent, Sound_Of(RECEIPT_TICK));

        Assert.True(
            _telegram.Count_Sent_WithSound(TelegramSendSounds.Rings) == 0,
            $"the app rang the phone before any supervisor entry existed:{Environment.NewLine}{_telegram.Dump_Sent()}");

        return session.OrchId;
    }

    const string RECEIPT_TICK = "✓";

    bool Has_ReceiptLanded()
    {
        return _preset == Presets_Loader.QUIET
            ? _telegram.Reactions.Count >= 1
            : _telegram.Has_Sent_Containing(RECEIPT_TICK);
    }

    async Task Wait_Sent_Async(string fragment)
    {
        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(fragment), 20_000),
            $"'{fragment}' never reached the phone.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");
    }

    TelegramSendSounds Sound_Of(string fragment)
    {
        return (_telegram.Find_Sent_Containing(fragment) ?? throw new Exception($"'{fragment}' was never sent")).Sound;
    }

    /// <summary>Every send that rang carries one of the fragments that were entitled to — so nothing the app wrote rang.</summary>
    void Assert_NothingElseRang(params string[] entitled)
    {
        var loud = _telegram.Sent
            .ToList()
            .Where(sent => sent.Sound == TelegramSendSounds.Rings)
            .Where(sent => !entitled.Any(fragment => sent.Text.Contains(fragment, StringComparison.Ordinal)))
            .ToList();

        Assert.True(
            loud.Count == 0,
            $"{loud.Count} send(s) rang that were not entitled to: "
            + string.Join(" | ", loud.Select(sent => sent.Text.Length > 80 ? sent.Text[..80] : sent.Text)));
    }

    void Mark_SessionMidTurn(string orchId)
    {
        Write_SessionActivity(orchId, DateTime.UtcNow);
    }

    void Mark_SessionIdle(string orchId)
    {
        Write_SessionActivity(orchId, DateTime.UtcNow.AddMinutes(-10));
    }

    void Write_SessionActivity(string orchId, DateTime lastActivityUtc)
    {
        var usageFile = OwnerFacingSession_Locator.Get_UsageFile(_paths, orchId, _store.Get_Session_OrNull(orchId));
        var sessionFolder = Path.GetDirectoryName(usageFile) ?? throw new Exception($"usage file '{usageFile}' has no folder");

        Directory.CreateDirectory(sessionFolder);

        var transcriptPath = Path.Combine(sessionFolder, "transcript.jsonl");
        var stamp = lastActivityUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        File.WriteAllText(transcriptPath, $$"""{"type":"assistant","timestamp":"{{stamp}}","uuid":"u"}""");
        File.WriteAllText(usageFile, $$"""{"transcript_path":"{{transcriptPath.Replace("\\", "\\\\")}}","version":"2.1.229"}""");
    }

    void Append_SupervisorEntry(string orchId, int index, string subject, string body)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(_paths.Get_OwnerChannelFile(orchId), $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
    }

    static string Build_OwnerMessageJson(string text)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":7101,\"message\":{\"message_id\":151,"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    async Task<bool> Run_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        var engine = _engine ?? throw new Exception("Use_Preset was not called");

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

    /// <summary>The body of one engine method, found by its signature — refusing loudly when it is not there.</summary>
    static string Extract_EngineMethod(string signatureMark)
    {
        var source = Read_EngineSource();
        var at = source.IndexOf(signatureMark, StringComparison.Ordinal);

        Assert.True(at >= 0, $"'{signatureMark}' is not in BridgeEngineModel.cs — this scan cannot prove anything about a method it cannot find");

        var depth = 0;

        for (var i = source.IndexOf('{', at); i >= 0 && i < source.Length; i++)
        {
            // Line comments are skipped: the engine's prose quotes braces.
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                    i++;

                continue;
            }

            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(at, i - at + 1);
        }

        throw new Exception($"the body of '{signatureMark}' never closed — BridgeEngineModel.cs is not what this scan expects");
    }

    static string Read_EngineSource()
    {
        for (var folder = AppContext.BaseDirectory; folder != null; folder = Path.GetDirectoryName(folder))
        {
            var candidate = Path.Combine(folder, "AIOrchestratorCoreLib", "Bridge", "BridgeEngine", "BridgeEngineModel.cs");

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new Exception($"BridgeEngineModel.cs was not found above '{AppContext.BaseDirectory}' — this scan needs the repository source");
    }
}
