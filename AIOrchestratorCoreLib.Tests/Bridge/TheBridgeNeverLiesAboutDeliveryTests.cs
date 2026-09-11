using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Hosting.HostWindowing;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE OWNER IS NEVER TOLD SOMETHING ARRIVED WHEN IT DID NOT — their own words for this brief.
///
/// <para>
/// Four ways the bridge used to say "✓" over nothing, or say nothing at all:
/// an unknown topic (dropped with a warning, ticked anyway); a CLOSED orchestration (written into a
/// channel whose tailers are stopped, ticked anyway); a DOCUMENT (never parsed — a file with no
/// caption produced no owner message at all); and thirty minutes of failing sends (the append
/// CONFIRMED, the entries gone, one Error line on a machine the owner does not read).
/// </para>
/// <para>
/// Plus the fifth, which is not about a receipt but about the same honesty: a command this host
/// cannot carry out is refused in one line instead of throwing a Windows P/Invoke into the inbound
/// loop, where it took a whole batch of the owner's messages down with it (2026-09-08).
/// </para>
/// </summary>
public class TheBridgeNeverLiesAboutDeliveryTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 9191;
    const long UNKNOWN_TOPIC_ID = 424242;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly ScriptedInbound_Fake _telegram = new();
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);

    CancellationTokenSource? _runCancellation;
    Task? _runLoop;

    public TheBridgeNeverLiesAboutDeliveryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-honest-delivery-{Guid.NewGuid():N}");
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
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);
    }

    public void Dispose()
    {
        if (_runCancellation != null && _runLoop != null)
        {
            Stop_Async(_runCancellation, _runLoop).GetAwaiter().GetResult();
            _runCancellation.Dispose();
        }

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

    /// <summary>
    /// A CLOSED ORCHESTRATION'S CHANNEL IS AN ARCHIVE: its tailers are stopped and its terminals are
    /// gone, so an append there is a write nobody will read. The topic can outlive the close because
    /// the delete that should remove it is fire-and-forget (brief E1), which is how the owner can
    /// still be looking at it.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMessageIntoAClosedOrchestrationsTopic_IsNotTicked_NotAppended_AndExplained()
    {
        var engine = Build_Engine();
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        var before = File.ReadAllText(channelFile);

        _store.Close_Orchestration(session.OrchId);

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("are we still on this?", 9001, 201, TOPIC_ID)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Count_Sent_Containing("this orchestration is closed") == 1, 20_000),
                $"the owner was never told the orchestration is closed.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");
        });

        // NOT ACKNOWLEDGED — the receipt is the claim this brief is about, in BOTH its spellings.
        // Since brief D the ✓ is not the receipt, so asserting only its absence would pass
        // vacuously and a regression that reacted 👀 to a message the bridge refused to route
        // would go straight through.
        Assert.Equal(0, _telegram.Count_Sent_Containing("✓"));
        Assert.Empty(_telegram.Reactions);

        // AND NOTHING WAS WRITTEN INTO THE DEAD CHANNEL.
        Assert.Equal(before, File.ReadAllText(channelFile));
        Assert.DoesNotContain("are we still on this?", File.ReadAllText(channelFile), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMessageIntoATopicNobodyOwns_IsNotTicked_AndExplained()
    {
        var engine = Build_Engine();

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("hello?", 9101, 202, UNKNOWN_TOPIC_ID)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Count_Sent_Containing("belongs to no orchestration") == 1, 20_000),
                $"the owner was never told the topic is unknown.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");
        });

        // Neither spelling of the receipt — see the closed-orchestration case above.
        Assert.Equal(0, _telegram.Count_Sent_Containing("✓"));
        Assert.Empty(_telegram.Reactions);
    }

    /// <summary>
    /// A DOCUMENT WITH NO CAPTION used to produce NOTHING: the parser knew text, photo, voice and
    /// callback, so the whole update parsed to null and the offset advanced over it. The owner had
    /// sent a file and the bridge had never heard of it.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ADocumentWithNoCaption_LandsInMedia_IsNamedInTheChannel_AndIsAcknowledged()
    {
        var engine = Build_Engine();
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        _telegram.Set_DownloadBytes(System.Text.Encoding.UTF8.GetBytes("id,name\n1,first\n"));

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Document_Json("rows.csv", "text/csv", 42, 9201, 203, TOPIC_ID)));

            Assert.True(
                await Wait_Until_Async(() => File.ReadAllText(channelFile).Contains("FILE:", StringComparison.Ordinal), 20_000),
                $"the document never reached the channel.{Environment.NewLine}{File.ReadAllText(channelFile)}{Environment.NewLine}{_log.Dump()}");

            // ACKNOWLEDGED — this one DID arrive, so the receipt is honest. Since brief D that
            // receipt is a REACTION on the owner's own message, not a ✓ line in the topic; the
            // property the case was written for is unchanged, only its spelling.
            Assert.True(
                await Wait_Until_Async(() => _telegram.Reactions.Any(reaction => reaction.Emoji == OwnerReaction_Emoji.RECEIVED), 20_000),
                $"an owner document that arrived was never acknowledged.{Environment.NewLine}{_telegram.Dump_Sent()}");

            Assert.Equal(0, _telegram.Count_Sent_Containing("✓"));
        });

        var mediaFolder = Path.Combine(Path.GetDirectoryName(channelFile)!, "media");
        var saved = Directory.GetFiles(mediaFolder, "tg-doc-*");

        var savedFile = Assert.Single(saved);
        Assert.Contains("rows.csv", savedFile, StringComparison.Ordinal);
        Assert.Equal("id,name\n1,first\n", File.ReadAllText(savedFile));

        var channel = File.ReadAllText(channelFile);
        Assert.Contains(savedFile, channel, StringComparison.Ordinal);
        Assert.Contains("no caption", channel, StringComparison.Ordinal);
    }

    /// <summary>
    /// PAST THE RETRY WINDOW THE ENTRIES ARE PARKED, NOT DROPPED, and the first send that works
    /// carries them as ONE document — never as the burst that replaying them as messages would be.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task EntriesTheMirrorGaveUpOn_ArriveAsOneDigestWhenTelegramAnswersAgain()
    {
        var engine = Build_Engine(retryBackoffSeconds: 1);
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        // Every send of the supervisor's words fails — the outage.
        _telegram.Fail_Sends_Containing("BLOCKED ON OWNER");

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("what is happening", 9301, 204, TOPIC_ID)));

            Assert.True(
                await Wait_Until_Async(() => _log.Has_Info_Containing("Owner message buffered"), 15_000),
                $"the engine never made its first pass.{Environment.NewLine}{_log.Dump()}");

            Append_Supervisor(session.OrchId, 3, "BLOCKED ON OWNER — the migration needs your word before it runs.");
            Append_Supervisor(session.OrchId, 4, "A second entry, so the one above it is not the trailing entry.");

            Assert.True(
                await Wait_Until_Async(() => _log.Has_Line_Containing("Telegram mirror send failed"), 20_000),
                $"the mirror never even tried.{Environment.NewLine}{_log.Dump()}");

            // Past the window: the clock is what the give-up reads.
            _clock.Advance(TimeSpan.FromMinutes(31));

            Assert.True(
                await Wait_Until_Async(() => _log.Has_Line_Containing("are PARKED"), 25_000),
                $"the give-up never parked the entries.{Environment.NewLine}{_log.Dump()}");

            // Telegram answers again, and something new comes through.
            _telegram.Fail_Sends_Containing("nothing will ever contain this");

            Append_Supervisor(session.OrchId, 5, "BLOCKED ON OWNER — still waiting on you.");
            Append_Supervisor(session.OrchId, 6, "And one more, to terminate the entry above.");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Documents.Count >= 1, 25_000),
                $"the parked entries were never delivered as a digest.{Environment.NewLine}{_log.Dump()}");
        });

        var digest = Assert.Single(_telegram.Documents);
        Assert.Equal(UndeliveredDigest_Builder.FILE_NAME, digest.FileName);
        Assert.Contains("did not reach you", digest.Caption, StringComparison.Ordinal);
        Assert.Contains("the migration needs your word", System.Text.Encoding.UTF8.GetString(digest.Content), StringComparison.Ordinal);
    }

    /// <summary>
    /// A COMMAND THIS HOST CANNOT CARRY OUT IS REFUSED IN ONE LINE. `/show` reached three static
    /// classes of unguarded user32/dwmapi/gdi32 P/Invoke; on the Linux daemon it threw
    /// DllNotFoundException out of the command dispatch and out of the whole inbound batch, and
    /// Telegram re-served every update in that batch four times (2026-09-08 01:24-01:26Z).
    ///
    /// <para>
    /// THE HOST IS INJECTED, not detected, so this probe means the same thing on every machine the
    /// suite runs on — including a Windows one, where a detected capability would make it vacuous.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ACommandThisHostCannotDo_IsRefusedInOneLine_AndThrowsNothing()
    {
        var engine = Build_Engine(HostWindowing_Factory.Create_Unsupported());
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("/show", 9401, 205, TOPIC_ID)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Count_Sent_Containing("not available on this host yet") == 1, 20_000),
                $"the owner was never told the command is unavailable.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");

            // A second /show is answered again — they typed it, so they are owed an answer — while
            // the LOG says it once, because the tenth copy teaches a reader nothing.
            _telegram.Queue_Updates(Updates_Json(Message_Json("/show", 9402, 206, TOPIC_ID)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Count_Sent_Containing("not available on this host yet") == 2, 20_000),
                $"the second /show went unanswered.{Environment.NewLine}{_telegram.Dump_Sent()}");
        });

        Assert.Equal(1, Count_Occurrences(_log.Dump(), "needs a desktop this host does not have"));

        // AND NOTHING THREW into the loop: no failure line for either update.
        Assert.DoesNotContain("Handling update 9401 failed", _log.Dump(), StringComparison.Ordinal);
        Assert.DoesNotContain("Handling update 9402 failed", _log.Dump(), StringComparison.Ordinal);
        Assert.DoesNotContain("DllNotFound", _log.Dump(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <paramref name="retryBackoffSeconds"/> is named by the give-up test only. The give-up is
    /// reached on the SECOND failed attempt, and Fast() keeps the shipped 30-second pause between
    /// attempts — so with the default the second attempt never happens inside a test's lifetime and
    /// the probe would time out having proved nothing about parking.
    /// </summary>
    IBridgeEngine Build_Engine(IHostWindowing? hostWindowing = null, int? retryBackoffSeconds = null)
    {
        return BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, _telegram,
            _engineState, _clock,
            retryBackoffSeconds == null ? BridgeTestTiming.Fast() : BridgeTestTiming.Fast_WithRetryBackoff(retryBackoffSeconds.Value),
            hostWindowing);
    }

    async Task Run_WhileAsync(IBridgeEngine engine, Func<Task> body)
    {
        _runCancellation = new CancellationTokenSource();
        _runLoop = engine.Run_Async(_runCancellation.Token);

        try
        {
            await body();
        }
        finally
        {
            await Stop_Async(_runCancellation, _runLoop);
            _runCancellation.Dispose();
            _runCancellation = null;
            _runLoop = null;
        }
    }

    void Append_Supervisor(string orchId, int entryNumber, string body)
    {
        File.AppendAllText(
            _paths.Get_OwnerChannelFile(orchId),
            $"\n## [{entryNumber}] FROM supervisor — {DateTime.Now:yyyy-MM-dd HH:mm} — a note\n{body}\n");
    }

    static string Updates_Json(string update) => "{\"ok\":true,\"result\":[" + update + "]}";

    static string Message_Json(string text, long updateId, long messageId, long threadId)
    {
        return $"{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{threadId},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}";
    }

    /// <summary>
    /// A FAILED PHOTO TELLS THE OWNER — AND TELLING THEM MUST NOT COST THEM THE MESSAGE.
    ///
    /// <para>
    /// The photo path used to fail silently as far as the owner was concerned: the log got a line
    /// and the CHANNEL got a sentence, which the agent reads and the owner never does. So they
    /// watched a picture leave their phone, saw the ✓, and the session it was meant for never had
    /// it. The document path had told them all along; the asymmetry was nobody's decision.
    /// </para>
    /// <para>
    /// THE SECOND HALF IS THE ONE THAT NEEDS A PROBE. That reply is sent from INSIDE the catch
    /// block, and it is sent precisely when Telegram is already degraded — so if the reply itself
    /// times out and the sender rethrows it as a shutdown, the exception escapes the whole photo
    /// handler and the CAPTION never reaches the channel either. The fix (filtering the rethrow on
    /// the token, this file's canonical rule) is invisible to every other test: reverting it leaves
    /// the suite green. So the timeout is injected here deliberately — an HttpClient timeout is a
    /// TaskCanceledException raised while the token is NOT cancelled, which is exactly the shape
    /// that used to be mistaken for a shutdown.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APhotoThatFailsToDownload_TellsTheOwner_AndStillReachesTheChannelIfThatReplyTimesOut()
    {
        var engine = Build_Engine();
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        _telegram.Fail_Downloads_With(new Exception("Telegram file download failed with HTTP 400"));

        // The reply the fix introduced is the one that times out. If its rethrow is unfiltered,
        // this kills the caption with it.
        _telegram.Timeout_Sends_Containing("could not download that image");

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Photo_Json("look at this", 9301, 301, TOPIC_ID)));

            Assert.True(
                await Wait_Until_Async(() => File.ReadAllText(channelFile).Contains("look at this", StringComparison.Ordinal), 20_000),
                "the owner's caption never reached the channel — the failed-photo reply took the whole message down with it."
                    + $"{Environment.NewLine}{File.ReadAllText(channelFile)}{Environment.NewLine}{_log.Dump()}");
        });

        var channel = File.ReadAllText(channelFile);

        Assert.Contains("downloading it FAILED", channel, StringComparison.Ordinal);
        Assert.DoesNotContain("IMAGE:", channel, StringComparison.Ordinal);
    }

    /// <summary>The same, with the reply going through: the owner is told in one line.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APhotoThatFailsToDownload_IsSaidToTheOwner_NotOnlyToTheAgent()
    {
        var engine = Build_Engine();
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        _telegram.Fail_Downloads_With(new Exception("Telegram file download failed with HTTP 400"));

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Photo_Json("look at this", 9302, 302, TOPIC_ID)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Count_Sent_Containing("could not download that image") >= 1, 20_000),
                $"the owner was never told their picture did not arrive.{Environment.NewLine}{_telegram.Dump_Sent()}");
        });
    }

    /// <summary>
    /// THE RECEIPT IS A REACTION, AND THERE IS NO MESSAGE — brief D. 👀 the moment the bridge has
    /// the owner's message, on the owner's OWN bubble.
    ///
    /// <para>
    /// After brief C the ✓ was already silent, but it was still a line in the topic for every line
    /// the owner wrote. The assertion that matters is therefore the NEGATIVE one: not merely that
    /// a reaction was set, but that nothing was sent.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnOwnerMessage_IsAcknowledgedWithAReaction_AndNoMessageIsSent()
    {
        var engine = Build_Engine();
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        const long OWNER_MESSAGE_ID = 555;

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("look at this", 9401, OWNER_MESSAGE_ID, TOPIC_ID)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Reactions.Count >= 1, 20_000),
                $"the owner's message was never reacted to.{Environment.NewLine}{_log.Dump()}");
        });

        Assert.Equal((OWNER_MESSAGE_ID, OwnerReaction_Emoji.RECEIVED), _telegram.Reactions[0]);

        // THE POINT OF THE WHOLE BRIEF: no ✓ line in the topic.
        Assert.Equal(0, _telegram.Count_Sent_Containing("✓"));
    }

    /// <summary>
    /// 👀 BECOMES 👌 WHEN A SESSION PICKS THE MESSAGE UP — the second half of brief D, and the
    /// half that tells the owner the difference between "the bridge has it" and "someone is acting
    /// on it". It was unprobed: the reaction went on and never changed.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AReceivedMessage_TurnsFromEyesToOkWhenASessionPicksItUp()
    {
        var engine = Build_Engine();
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        const long OWNER_MESSAGE_ID = 601;

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("please look at the diff", 9501, OWNER_MESSAGE_ID, TOPIC_ID)));

            Assert.True(
                await Wait_Until_Async(
                    () => _telegram.Reactions.Any(reaction => reaction.Emoji == OwnerReaction_Emoji.PICKED_UP),
                    30_000),
                "the reaction never became 👌 — the owner is left at 'seen' for a message that was picked up."
                    + $"{Environment.NewLine}reactions: {string.Join(", ", _telegram.Reactions.Select(r => $"{r.MessageId}:{r.Emoji}"))}"
                    + $"{Environment.NewLine}{_log.Dump()}");
        });

        // BOTH marks, in order, on the OWNER'S OWN message — never on a bot message.
        var mine = _telegram.Reactions.Where(reaction => reaction.MessageId == OWNER_MESSAGE_ID).ToList();

        Assert.Equal(OwnerReaction_Emoji.RECEIVED, mine[0].Emoji);
        Assert.Equal(OwnerReaction_Emoji.PICKED_UP, mine[^1].Emoji);

        // And still no line in the topic for either of them.
        Assert.Equal(0, _telegram.Count_Sent_Containing("✓"));
    }

    /// <summary>
    /// A REFUSED REACTION FALLS BACK TO THE TICK — never to silence. Telegram answers 400 for a
    /// message it will not let a bot react to and 429 under load, and an acknowledgement that
    /// silently did not happen is the owner watching their message vanish.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AReactionTelegramRefuses_FallsBackToTheSilentTick()
    {
        var engine = Build_Engine();
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        _telegram.Refuse_Reactions("Bad Request: REACTION_INVALID");

        await Run_WhileAsync(engine, async () =>
        {
            _telegram.Queue_Updates(Updates_Json(Message_Json("look at this", 9402, 556, TOPIC_ID)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Count_Sent_Containing("✓") >= 1, 20_000),
                "the reaction was refused and nothing took its place — the owner's message was acknowledged by nothing at all."
                    + $"{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");
        });

        Assert.Empty(_telegram.Reactions);
    }

    /// <summary>
    /// TAPPING THE HOLD TOGGLE ON PULSE MUST NOT DESTROY THE BAR — brief D, and the defect the
    /// first cut shipped.
    ///
    /// <para>
    /// The toggle reuses the hold payload family, and that handler's job had always been "rewrite
    /// the message the button was tapped on" — safe while that message was a ✓ receipt that
    /// existed to be rewritten. On the status line it replaced the whole PULSE text with
    /// "⏸ holding…" and the four button rows with a single ▶ GO, so /pending, /left, /tail sup,
    /// /limits, /merge and /close vanished from the topic on one tap. It did not heal, either: the
    /// status refresh repaints only when its own rendering changes.
    /// </para>
    /// <para>
    /// Asserted on the KEYBOARD, not on the text, because the text is the half that reads plausibly
    /// while the bar is gone.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TappingTheHoldToggleOnTheStatusLine_LeavesTheCommandBarIntact()
    {
        var engine = Build_Engine();
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        await Run_WhileAsync(engine, async () =>
        {
            // Let the status line be posted, so there is a real id to tap on.
            Assert.True(
                await Wait_Until_Async(() => _store.Get_Session(session.OrchId).StatusLineMessageId != null, 30_000),
                $"the status line was never posted, so this probe taps nothing.{Environment.NewLine}{_log.Dump()}");

            var statusLineId = _store.Get_Session(session.OrchId).StatusLineMessageId!.Value;

            _telegram.Queue_Updates(Updates_Json(HoldTap_Json(statusLineId, TOPIC_ID, 9601)));

            Assert.True(
                await Wait_Until_Async(() => _telegram.Answered_Callbacks >= 1, 20_000),
                $"the tap was never answered.{Environment.NewLine}{_log.Dump()}");

            // Give the handler every chance to do the damage.
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(10));

            var wrecked = _telegram.ButtonEdits
                .Where(edit => edit.MessageId == statusLineId && edit.ButtonCount <= 1)
                .ToList();

            Assert.True(
                wrecked.Count == 0,
                "a tap on the PULSE bar rewrote the status line down to a single button — the owner's whole command bar is gone: "
                    + string.Join(" | ", wrecked.Select(edit => $"{edit.ButtonCount} button(s): {edit.Text}")));

            // THE REPAINT IS DELIBERATELY NOT ASSERTED HERE, and that is a finding rather than an
            // omission. The obvious assertion — "an edit carrying ▶ GO reaches the status line" —
            // passes with the render-key fix REMOVED, because a tap also moves the status TEXT and
            // the ordinary text comparison then repaints the bar as a side effect. Two routes to
            // one green is what CLAUDE.md decision 20 forbids, and a case that cannot fail for the
            // reason it claims is worse than no case.
            //
            // The render key's own behaviour is pinned in TopicStatusLineRenderKeyTests. What stays
            // unpinned is the one line in Refresh_TopicStatusLines_Async that consults it, for the
            // same reason TelegramAttempt_Gate states about its own caller: a button-only change
            // with the text held constant is not producible from this seam.
        });
    }

    static string HoldTap_Json(long tappedMessageId, long threadId, long updateId)
    {
        var data = HoldButton_Data.Build(HoldButtonActions.Hold, threadId);

        return $"{{\"update_id\":{updateId},\"callback_query\":{{\"id\":\"cbq-{updateId}\","
            + $"\"data\":\"{data}\",\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"message\":{{\"message_id\":{tappedMessageId},\"message_thread_id\":{threadId},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}}}}}}}}";
    }

    static string Photo_Json(string caption, long updateId, long messageId, long threadId)
    {
        return $"{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{threadId},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"caption\":\"{caption}\","
            + $"\"photo\":[{{\"file_id\":\"photo-{updateId}\",\"width\":90,\"height\":90}},"
            + $"{{\"file_id\":\"photo-{updateId}-big\",\"width\":900,\"height\":900}}]}}}}";
    }

    static string Document_Json(string fileName, string mimeType, long size, long updateId, long messageId, long threadId)
    {
        return $"{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{threadId},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},"
            + $"\"document\":{{\"file_id\":\"doc-{updateId}\",\"file_name\":\"{fileName}\","
            + $"\"mime_type\":\"{mimeType}\",\"file_size\":{size}}}}}}}";
    }

    static int Count_Occurrences(string text, string fragment)
    {
        var count = 0;
        var at = text.IndexOf(fragment, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(fragment, at + fragment.Length, StringComparison.Ordinal);
        }

        return count;
    }

    static async Task<bool> Wait_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
                return true;

            await Task.Delay(100);
        }

        return condition();
    }

    static async Task Stop_Async(CancellationTokenSource cancellation, Task loop)
    {
        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }
    }
}
