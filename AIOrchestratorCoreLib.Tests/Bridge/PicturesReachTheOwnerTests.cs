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
/// A PICTURE MUST ARRIVE AS A PICTURE.
///
/// The owner, 2026-09-08, four times across one day in a single topic — *"Continui a inviarmi la
/// directory dell'immagine invece dell'immagine stessa"*, then *"You keel sending the path, not the
/// actual image"*. Every one of the 39 `IMAGE:` lines that session wrote was correctly shaped and
/// every file existed; the app broke them in one place, silently: `MirrorText_Formatter` used to
/// glue the speaker glyph onto the first line ("🟠 ") before the marker was extracted, and the
/// marker is anchored at the START of a line — an entry whose body BEGINS with its picture read as
/// "🟠 IMAGE: C:\…", matched nothing, and went out as words.
///
/// RE-HOMED onto the fork's attachment gate (2026-09-11): the fork independently rebuilt this as
/// part of a broader ATTACH:/IMAGE: policy (<see cref="EntryAttachmentPolicyTests"/>,
/// <see cref="AttachmentsReachThePhoneTests"/>). Reading <c>BridgeEngineModel</c>'s mirror path
/// confirms the fix survives structurally: every marker (photo, attachment, question) is extracted
/// from <c>text</c> BEFORE the speaker prefix is glued back on (<c>text = speaker + text</c> runs
/// only after every <c>Extract_MarkerLines</c> call) — so a picture on the very first line of the
/// body can no longer be shadowed by the glyph. What master pinned as defect A is now guaranteed by
/// the shape of the code, but nothing else in the fork's suite exercises a picture-only or
/// picture-first entry through the ENGINE end to end (the fork's own tests exercise ATTACH:
/// end-to-end and IMAGE: only for the REFUSAL path — see
/// <see cref="AttachmentsReachThePhoneTests.AnHtmlFileSentAsAPicture_IsRefusedToTheAgent_NamingTheMarkerThatWouldHaveWorked"/>),
/// so this regression guard stays.
///
/// Two of master's original four cases are retired here, not adapted — see the commit body for
/// the reasons and the named fork test that already pins each:
///   - narration holding a picture is no longer a routing question at all (the narration filter
///     that could hold anything back was removed on 2026-09-09 — everything the supervisor writes
///     is pushed); pinned directly by
///     <see cref="OwnerPushPolicyTests.AnEntryCarryingAFile_IsPushed_BecauseAFileIsADeliveryAndNotNarration"/>,
///     which asserts <c>Should_Push</c> true for the exact shape (an image on the SECOND line of an
///     otherwise narration-only body);
///   - "narration alone still does not reach the phone" is now the OPPOSITE of the shipped contract
///     and would pin a regression against the owner's own 2026-09-09 ruling; pinned (inverted) by
///     <see cref="OwnerPushPolicyTests.NoSubject_ChangesNothing_ForCallersThatDoNotPassOne"/>, which
///     asserts plain narration with no marker at all now pushes.
///
/// A THIRD CASE, initially dropped and then RESTORED (2026-09-12, reviewer correction): the
/// picture-only entry captioning the photo with its SUBJECT rather than sending a bare "🟠 ".
/// This was first misjudged as an optional preservation outside this task's contract. It is not:
/// master's <c>da8f66c</c> added exactly this guard in the engine's mirror loop —
/// <c>if (content.Trim().Length == 0) content = entry.Subject;</c>, placed after marker extraction
/// and before the speaker prefix was glued back on — and merge <c>91d3402</c> dropped it while
/// promising in its own message that master's intent would be re-ported. So this was an
/// UNFINISHED RE-PORT, not a difference to weigh. The guard is re-ported (matching master's
/// condition, now over the fork's <c>text</c> variable) and the case below is back. The earlier,
/// incorrect root-cause note (blaming <c>MirrorText_Formatter.Pick_Content</c>, which is unchanged
/// between master and the fork) is corrected: the fallback that went missing lives in the ENGINE's
/// mirror loop, after all marker extraction, not in the formatter.
/// </summary>
public class PicturesReachTheOwnerTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly string _pictureFile;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly RecordingLog_Fake _log;

    public PicturesReachTheOwnerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-pictures-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        // A REAL FILE: Approve_OwnerFile checks File.Exists and refuses silently-to-the-owner
        // without one, which would make every case here green for the wrong reason.
        _pictureFile = Path.Combine(_tempRepo, "screenshot.png");
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
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// DEFECT A, still a live regression guard: see the class remarks. A failure here now means
    /// someone moved `text = speaker + text` back ABOVE the `Extract_MarkerLines` calls (or added a
    /// new marker extracted only after that line) — the current code makes the original defect
    /// structurally impossible, so this red would point at a reintroduction, not a first discovery.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnEntryThatOpensWithItsPicture_UploadsThePhoto_AndNeverTextsThePath()
    {
        var telegram = new FailableTelegram_Fake();
        var engine = Build_Engine(telegram);
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        Append_SoloEntry(orchId, 1, "the new header", $"IMAGE: {_pictureFile}\nDoes the new header look right to you?");

        Assert.True(
            await Run_Until_Async(engine, () => telegram.Has_SentPhoto(_pictureFile), 15_000),
            "THE DEFECT: the body opened with the picture line, so the speaker glyph was glued to it "
            + $"and the marker never matched — no photo was uploaded.{Environment.NewLine}"
            + Describe_Traffic(telegram));

        Assert_NoTextCarriedThePath(telegram);
    }

    /// <summary>
    /// An entry whose whole body IS the picture has nothing left once the marker is lifted out, and
    /// a bare "🟠 " is not a message. The subject is the caption the owner should read under the
    /// photo. Re-homed from master's <c>da8f66c</c> (see the class remarks) — a straight re-port,
    /// not a new assertion.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APictureOnlyEntry_CaptionsThePhotoWithItsSubject_RatherThanSendingABareGlyph()
    {
        var telegram = new FailableTelegram_Fake();
        var engine = Build_Engine(telegram);
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        Append_SoloEntry(orchId, 1, "main window, new look", $"IMAGE: {_pictureFile}");

        Assert.True(
            await Run_Until_Async(engine, () => telegram.Has_SentPhoto(_pictureFile), 15_000),
            $"the photo was never uploaded.{Environment.NewLine}{Describe_Traffic(telegram)}");

        Assert.True(
            telegram.Has_Sent_Containing("main window, new look"),
            $"the subject never reached the owner, so the photo arrived uncaptioned.{Environment.NewLine}"
            + Describe_Traffic(telegram));

        Assert_NoTextCarriedThePath(telegram);
    }

    IBridgeEngine Build_Engine(FailableTelegram_Fake telegram)
    {
        return BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, _configProvider, _store, _launcher, _log, telegram, BridgeTestTiming.Fast());
    }

    void Assert_NoTextCarriedThePath(FailableTelegram_Fake telegram)
    {
        foreach (var text in telegram.Sent_Texts())
        {
            Assert.DoesNotContain(_pictureFile, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IMAGE:", text, StringComparison.Ordinal);
        }
    }

    string Describe_Traffic(FailableTelegram_Fake telegram)
    {
        var texts = telegram.Sent_Texts();

        return $"Texts sent ({texts.Count}):{Environment.NewLine}"
            + string.Join(Environment.NewLine, texts.Select(text => $"  · {text}"))
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}";
    }

    /// <summary>
    /// The tailer registers an unseen file at its CURRENT END, so an entry appended before the first
    /// poll sits behind the starting offset and is never mirrored at all — a setup mistake that looks
    /// exactly like the defect under test. One short run baselines the file first.
    /// </summary>
    async Task<string> Start_WithChannelAlreadySeen_Async(IBridgeEngine engine)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(3));

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
