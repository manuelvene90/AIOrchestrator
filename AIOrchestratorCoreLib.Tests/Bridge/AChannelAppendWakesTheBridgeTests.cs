using System.Diagnostics;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;
using AIOrchestratorCoreLib.Bridge.BridgeEngineTiming;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE BRIDGE REACTS TO A CHANGED CHANNEL INSTEAD OF DISCOVERING IT ON THE NEXT TICK.
///
/// <para>
/// Measured on the VPS on 2026-09-09: 11–12 s median from the owner's Telegram message to their
/// supervisor's turn starting. One of those seconds was the mirror tick — <c>MIRROR_TICK_MILLISECONDS</c>
/// is 2000, so anything appended to a channel file waits on average a second, and up to two, before the
/// loop looks at it. It is paid TWICE on the owner's path, because the tick that writes their message
/// into the channel is not the tick that hands it to the dispatcher.
/// </para>
/// <para>
/// The owner's decision was to react to the change and keep the tick as the safety net. A
/// <c>ChannelChangeWaker</c> watches the supervision root for <c>*.md</c> writes and the loop
/// waits on the tick OR the watcher, whichever comes first, so the tick is a ceiling rather than a
/// quantum. A wake also carries two short settling polls, so the rest of a burst is read on the same
/// arrival instead of a tick later.
/// </para>
/// <para>
/// WHAT IS TIMED IS AN ENTRY THAT IS PROVABLY COMPLETE, and until 2026-09-09 it was not. The measured
/// append used to be the file's LAST one, whose release the tailer holds until the file has stopped
/// growing — and this test's 583 ms readings were the merged code releasing it after ~350 ms of quiet,
/// which is precisely the torn-entry defect <c>AStalledWriterNeverTearsATrailingEntryTests</c> now
/// pins: a writer stalling 400 ms mid-entry had the rest of it silently dropped. So this test was
/// measuring the bug, and made it look like the feature. A trailing entry waits four seconds again
/// (<c>ChannelTailer_Factory.TRAILING_ENTRY_QUIET_MILLISECONDS</c>) and always did before the waker;
/// what the waker makes fast is an entry a FOLLOWING HEADER proves complete, which the tailer releases
/// on the poll that reads it. That is what is appended and timed below.
/// </para>
/// <para>
/// HOW THE MEASUREMENT IS MADE HONEST. The first append is mirrored and OBSERVED, which is the moment a
/// tick has just finished — so the next tick is a full 2 s away. The pair is appended right there, and
/// the first of them must reach Telegram in well under a second. Without a watcher that is impossible:
/// the loop is asleep for the rest of the tick and nothing else wakes it.
/// </para>
/// </summary>
public class AChannelAppendWakesTheBridgeTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    /// <summary>
    /// THE TICK IS A MINUTE HERE, not production's 2 s (2026-09-30). The claim is "the bridge reacts to
    /// the file, it does not wait for the tick", and with a 2 s tick the test could only tell the two
    /// apart by a margin of a few hundred milliseconds — which a busy 2-core CI runner ate: Windows CI
    /// on ffa2dc4 measured 2201 ms against a 1200 ms ceiling, the shape of a pickup by the next tick
    /// OR of a slow tick after the wake, and nothing in the output could say which. With a 60 s tick
    /// the two are a minute apart: anything under the ceiling below can ONLY be the waker, however
    /// slow the machine, and a regression — the wake lost — puts the number at the full minute.
    /// </summary>
    const int LONG_TICK_MILLISECONDS = 60_000;

    /// <summary>
    /// A quarter of the tick: room for a starved runner many times over, and still unreachable except by
    /// reacting to the file. The entry timed against it is complete the instant it is read — its
    /// successor's header is already on disk — so nothing here waits out a quiet period.
    /// </summary>
    const int WITHOUT_WAITING_A_TICK_MILLISECONDS = LONG_TICK_MILLISECONDS / 4;

    /// <summary>Ends in a question because OwnerPush_Policy keeps pure narration off the phone entirely.</summary>
    const string FIRST_ENTRY = "ALPHA-ENTRY. Shall I proceed?";
    const string SECOND_ENTRY = "BETA-ENTRY. Shall I proceed?";
    const string THIRD_ENTRY = "GAMMA-ENTRY. Shall I proceed?";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly RecordingLog_Fake _log;
    readonly ByMethodTelegram_Fake _telegram;
    readonly IOrchestratorConfigProvider _configProvider;

    public AChannelAppendWakesTheBridgeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-wake-{Guid.NewGuid():N}");
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
    }

    /// <summary>
    /// SKIPPED, NOT FAILED, where the filesystem delivers no watcher events at all — the bridge is
    /// written to fall back to its 2 s tick there, so there would be no reaction to measure. Probed on
    /// the machine rather than read off a platform name: see <see cref="FileSystemWatcherEvents"/>.
    /// </summary>
    [RequiresFileSystemWatcherEventsFact]
    [Trait("Speed", "Slow")]
    public async Task AnAppendedEntry_IsMirroredWithoutWaitingOutTheTick()
    {
        // PRODUCTION TIMING WITH A LONG TICK, unlike every other Bridge test: this one measures that an
        // append is mirrored WITHOUT waiting out the tick, and BridgeTestTiming.Fast() would shrink that
        // tick to 20 ms — the test would pass with no waker at all and prove nothing. Everything but the
        // tick is production's; see LONG_TICK_MILLISECONDS for why the tick is not.
        var timing = BridgeEngineTiming_Factory.Create_Custom_WindowFromSettings(
            LONG_TICK_MILLISECONDS,
            mirrorRetryBackoffSeconds: 30,
            (int)ChannelWrite_Lock.DEFAULT_TICK_ALLOWANCE.TotalMilliseconds,
            ChannelTailer_Factory.TRAILING_ENTRY_QUIET_MILLISECONDS);

        var engine = BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, _configProvider, _store, _launcher, _log, _telegram, timing);

        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, "IS · wake");

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);

        try
        {
            // The tailer registers a file it has never seen at its CURRENT END, so the first append has
            // to come after it has been sighted — otherwise it is behind the offset and never mirrors,
            // which looks exactly like the defect under test.
            await Task.Delay(2_500);

            // WITH A SUCCESSOR, for the same reason BETA has one below: with a minute-long tick, a
            // trailing entry would wait out its quiet period AND the next tick before release. The
            // header behind it proves ALPHA complete, so the waker alone carries it.
            Append_SupervisorEntry(channelFile, 1, "first", FIRST_ENTRY);
            Append_SupervisorEntry(channelFile, 2, "first-successor", "ALPHA-SUCCESSOR. Shall I proceed?");

            Assert.True(
                await Wait_Until_Async(() => _telegram.AnyHtmlSendContains("ALPHA-ENTRY"), 20_000),
                $"the first entry never reached Telegram at all.{Environment.NewLine}{_log.Dump()}");

            // A tick has just this instant finished, so the next one is a full 2 s out, and the pause is
            // longer than the waker's debounce on top of that: what is timed below is a new arrival
            // waking a sleeping loop, not the tail of the burst above it.
            await Task.Delay(400);

            var stopwatch = Stopwatch.StartNew();

            // TWO, and the second one is not incidental: it puts a header behind BETA, which is what
            // proves BETA complete and lets the tailer release it on the poll that reads it. The last
            // entry of a file is a different subject with a different guarantee — four seconds of no
            // growth — and pinning that one here would only re-pin the tear.
            Append_SupervisorEntry(channelFile, 3, "second", SECOND_ENTRY);
            Append_SupervisorEntry(channelFile, 4, "third", THIRD_ENTRY);

            Assert.True(
                await Wait_Until_Async(() => _telegram.AnyHtmlSendContains("BETA-ENTRY"), 20_000),
                $"the second entry never reached Telegram at all.{Environment.NewLine}{_log.Dump()}");

            var waited = stopwatch.Elapsed;

            Assert.True(
                waited.TotalMilliseconds < WITHOUT_WAITING_A_TICK_MILLISECONDS,
                $"the append waited for the next tick: {waited.TotalMilliseconds:F0} ms, and the tick is {LONG_TICK_MILLISECONDS} ms. "
                    + $"The bridge is not reacting to the channel file.{Environment.NewLine}{_log.Dump()}");
        }
        finally
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

    static void Append_SupervisorEntry(string channelFile, int index, string subject, string body)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(channelFile, $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
    }

    static async Task<bool> Wait_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        // 20 ms, not the 100 ms the older bridge probes poll at: this test MEASURES the wait, so the
        // poll interval is part of the number it reports.
        for (var waited = 0; waited < maxMilliseconds; waited += 20)
        {
            if (condition())
                return true;

            await Task.Delay(20);
        }

        return condition();
    }
}
