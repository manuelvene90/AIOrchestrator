using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A NOTIFICATION CARRYING NOTHING IS WORSE THAN SILENCE.
///
/// <para>
/// Observed 2026-09-15, seven times in one Telegram export the owner supplied: a message arrived
/// that was only the speaker prefix — "🔴 Sup:" with no text — and the real question followed
/// immediately after it. The prefix is app chrome, so what rang the owner's phone was the app
/// announcing that it had nothing to say.
/// </para>
/// <para>
/// THE MECHANISM, in three steps. The mirror strips every marker line out of the body before
/// sending (the question, its options, the recommendation — the app turns those into buttons), so an
/// entry whose body is ALL markers has no prose left. It then falls back to the entry's SUBJECT,
/// which is the right instinct and the reason a picture-only entry captions its photo. And when the
/// subject is empty too, the fallback has nothing to give and the prefix went out alone.
/// </para>
/// <para>
/// Two routes produced the empty subject, and both are closed at their source now — a header
/// carrying one em dash (<c>ChannelEntry_Parser</c> used to read its whole tail as the date) and
/// <c>ChannelAppender</c> writing a blank subject between two em dashes. This guard is the third
/// line: it makes the whole class unreachable from the send path, whatever a future writer does to a
/// header. The QUESTION IS STILL SENT — an entry that is all markers has nothing to narrate and
/// everything to ask, so the buttons are exactly what the owner wanted.
/// </para>
/// </summary>
public class ABareSpeakerPrefixIsNeverTextedTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4343;

    /// <summary>The solo voice, whose prefix carries no colon — the hardest shape to spot as "empty".</summary>
    const string SOLO_PREFIX = "🟠";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly RecordingLog_Fake _log;

    public ABareSpeakerPrefixIsNeverTextedTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-bare-prefix-tests-{Guid.NewGuid():N}");
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
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnEntryWithNoProseAndNoSubject_SendsTheQuestion_AndNeverABareGlyph()
    {
        var telegram = new FailableTelegram_Fake();
        var engine = Build_Engine(telegram);
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        // Every line is a marker, so nothing survives extraction — and the subject is empty, so the
        // fallback has nothing either. This is the exact shape behind the seven blank notifications.
        Append_SoloEntry(
            orchId,
            1,
            subject: string.Empty,
            body: "ROW: FIN-D-043\nRISK: low\nQUESTION: pulisco il disco di staging?\nOPTION: sì, pulisci\nOPTION: no, lascia stare\nRECOMMEND: sì, altrimenti ogni merge chiede un secondo deploy");

        Assert.True(
            await Run_Until_Async(engine, () => telegram.Has_Sent_Containing("pulisco il disco di staging"), 15_000),
            $"the question never reached the owner.{Environment.NewLine}{Describe_Traffic(telegram)}");

        foreach (var text in telegram.Sent_Texts())
        {
            Assert.NotEqual(SOLO_PREFIX, text.Trim());

            Assert.False(
                text.Trim().Length == 0,
                $"an empty message was sent.{Environment.NewLine}{Describe_Traffic(telegram)}");
        }
    }

    /// <summary>
    /// THE FALLBACK ITSELF IS NOT WHAT WAS REMOVED. A marker-only entry that HAS a subject still
    /// announces itself with it — that is the behaviour a picture-only entry depends on, and the
    /// guard above must not be mistaken for a licence to drop it.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMarkerOnlyEntryWithASubject_StillAnnouncesItselfWithThatSubject()
    {
        var telegram = new FailableTelegram_Fake();
        var engine = Build_Engine(telegram);
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        Append_SoloEntry(
            orchId,
            1,
            subject: "disco di staging pieno",
            body: "ROW: FIN-D-043\nRISK: low\nQUESTION: pulisco?\nOPTION: sì\nOPTION: no\nRECOMMEND: sì");

        Assert.True(
            await Run_Until_Async(engine, () => telegram.Has_Sent_Containing("disco di staging pieno"), 15_000),
            $"the subject never reached the owner.{Environment.NewLine}{Describe_Traffic(telegram)}");
    }

    IBridgeEngine Build_Engine(FailableTelegram_Fake telegram)
    {
        return BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, _configProvider, _store, _launcher, _log, telegram, BridgeTestTiming.Fast());
    }

    string Describe_Traffic(FailableTelegram_Fake telegram)
    {
        var texts = telegram.Sent_Texts();

        return $"Texts sent ({texts.Count}):{Environment.NewLine}"
            + string.Join(Environment.NewLine, texts.Select(text => $"  · [{text}]"))
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}";
    }

    async Task<string> Start_WithChannelAlreadySeen_Async(IBridgeEngine engine)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(3));

        return session.OrchId;
    }

    /// <summary>
    /// Written by hand rather than through <c>ChannelAppender</c> ON PURPOSE: the appender now
    /// borrows a subject from the body, so going through it could never produce the empty one this
    /// guard is about. A channel file is agent-written and the send path must hold whatever it finds.
    /// </summary>
    void Append_SoloEntry(string orchId, int index, string subject, string body)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(channelFile, $"\n## [{index}] FROM solo — {stamp} — {subject}\n{body}\n");
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
