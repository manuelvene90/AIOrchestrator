using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A PICTURE MUST ARRIVE AS A PICTURE.
///
/// The owner, 2026-09-08, four times across one day in a single topic — *"Continui a inviarmi la
/// directory dell'immagine invece dell'immagine stessa"*, then *"You keel sending the path, not the
/// actual image"*. Every one of the 39 `IMAGE:` lines that session wrote was correctly shaped and
/// every file existed; the app broke them, in two independent places, both silently:
///
///   A — `MirrorText_Formatter` glues the speaker glyph onto the first line ("🟠 "), and the marker
///       is anchored at the START of a line. An entry whose body BEGINS with its picture — the shape
///       every role command invites with `Pictures: IMAGE: &lt;full path&gt;` — read as "🟠 IMAGE: C:\…",
///       matched nothing, and went out as words.
///   B — an image inside ordinary narration was classified as narration and HELD. The held entry is
///       remembered as formatted text, and the two routes that release it later (the silent-deadlock
///       net and the turn-ended receipt) send text. So the picture could not survive the trip even in
///       principle: the owner got the path, minutes late, and never the photo.
///
/// The suite had nothing on any of this — no test touched the marker parsing or the photo dispatch,
/// and the Telegram fake's `Send_Photo_Async` was a bare `Task.CompletedTask`, so a picture that was
/// never uploaded looked identical to one that was.
///
/// EACH CASE ISOLATES ONE DEFECT. The first carries a prose question, so it pushed to the phone under
/// the OLD policy too — which leaves the mangled text as the only thing that can fail it. The second
/// carries nothing ask-shaped and puts the picture on a LATER line, where the glyph cannot reach it —
/// which leaves the push decision as the only thing that can fail it. Neither has two routes to green.
/// </summary>
public class PicturesReachTheOwnerTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    /// <summary>Ends in '?', so `Asks_InProse` pushes this entry with or without the image rule.</summary>
    const string ASKING_CAPTION = "Does the new header look right to you?";

    /// <summary>Nothing ask-shaped, no marker: under the old policy this was narration and was held.</summary>
    const string NARRATION_CAPTION = "The report renders and the build is green.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly string _pictureFile;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;

    public PicturesReachTheOwnerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-pictures-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        // A REAL FILE: Send_EntryPhoto_BestEffort_Async checks File.Exists and drops silently without
        // one, which would make every case here green for the wrong reason.
        _pictureFile = Path.Combine(_tempRoot, "screenshot.png");
        File.WriteAllBytes(_pictureFile, [0x89, 0x50, 0x4E, 0x47]);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"telegramItalianLayer\":false}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _log = new RecordingLog_Fake();
        _telegram = new FailableTelegram_Fake();

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>DEFECT A: the speaker glyph reached the marker before the parser did.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnEntryThatOpensWithItsPicture_UploadsThePhoto_AndNeverTextsThePath()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        Append_SoloEntry(orchId, 1, "the new header", $"IMAGE: {_pictureFile}\n{ASKING_CAPTION}");

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_SentPhoto(_pictureFile), 15_000),
            "THE DEFECT: the body opened with the picture line, so the speaker glyph was glued to it "
            + $"and the marker never matched — no photo was uploaded.{Environment.NewLine}"
            + Describe_Traffic());

        Assert_NoTextCarriedThePath();
    }

    /// <summary>DEFECT B: a picture was classified as narration and released later as text.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APictureInsideOrdinaryNarration_IsNotHeldBackAsNarration()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        // The picture is on the SECOND line, out of the glyph's reach, so defect A cannot be what
        // fails this case.
        Append_SoloEntry(orchId, 1, "report renders", $"{NARRATION_CAPTION}\nIMAGE: {_pictureFile}");

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_SentPhoto(_pictureFile), 15_000),
            "THE DEFECT: an entry carrying a picture was judged ordinary narration and held back, and "
            + "every route that releases a held entry sends TEXT — so the photo could never arrive."
            + $"{Environment.NewLine}{Describe_Traffic()}");

        Assert_NoTextCarriedThePath();
    }

    /// <summary>
    /// An entry whose whole body IS the picture has nothing left once the marker is lifted out, and a
    /// bare "🟠 " is not a message. The subject is the caption the owner should read under the photo.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APictureOnlyEntry_CaptionsThePhotoWithItsSubject_RatherThanSendingABareGlyph()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        Append_SoloEntry(orchId, 1, "main window, new look", $"IMAGE: {_pictureFile}");

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_SentPhoto(_pictureFile), 15_000),
            $"the photo was never uploaded.{Environment.NewLine}{Describe_Traffic()}");

        Assert.True(
            _telegram.Has_Sent_Containing("main window, new look"),
            $"the subject never reached the owner, so the photo arrived uncaptioned.{Environment.NewLine}"
            + Describe_Traffic());

        Assert_NoTextCarriedThePath();
    }

    /// <summary>
    /// THE OPPOSITE REGRESSION. "A picture always pushes" must not become "everything pushes": delete
    /// the rest of OwnerPush_Policy and the three cases above stay green, while the waterfall the
    /// policy exists to stop comes back.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task NarrationWithoutAPicture_StillDoesNotReachThePhone()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        Append_SoloEntry(orchId, 1, "progress", NARRATION_CAPTION);

        Assert.False(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(NARRATION_CAPTION), 12_000),
            $"ordinary progress narration is being pushed to the owner's phone again.{Environment.NewLine}"
            + Describe_Traffic());
    }

    void Assert_NoTextCarriedThePath()
    {
        foreach (var text in _telegram.Sent_Texts())
        {
            Assert.DoesNotContain(_pictureFile, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IMAGE:", text, StringComparison.Ordinal);
        }
    }

    string Describe_Traffic()
    {
        var texts = _telegram.Sent_Texts();

        return $"Texts sent ({texts.Count}):{Environment.NewLine}"
            + string.Join(Environment.NewLine, texts.Select(text => $"  · {text}"))
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}";
    }

    /// <summary>
    /// The tailer registers an unseen file at its CURRENT END, so an entry appended before the first
    /// poll sits behind the starting offset and is never mirrored at all — a setup mistake that looks
    /// exactly like the defect under test. One short run baselines the file first.
    /// </summary>
    async Task<string> Start_WithChannelAlreadySeen_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        await Run_For_Async(4_000);

        return session.OrchId;
    }

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    /// <summary>
    /// FROM solo, because that is the voice the owner was reading when this broke, and its prefix
    /// ("🟠 ") is the one with no colon — the shape that also defeats a prefix read back off the
    /// formatted string.
    /// </summary>
    void Append_SoloEntry(string orchId, int index, string subject, string body)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(channelFile, $"\n## [{index}] FROM solo — {stamp} — {subject}\n{body}\n");
    }

    async Task Run_For_Async(int milliseconds)
    {
        await Run_Until_Async(() => false, milliseconds);
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
