using System.Text;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Time.Clock;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Tailing;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;
using AIOrchestratorCoreLib.Tests.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Tailing;

/// <summary>
/// THE COMPACTION GUARD IS ASKED INSIDE THE GATE — the 2026-09-10 incident, entry 137.
///
/// The compactor takes the channel's write gate, and a session's append helper takes the same one.
/// So a compaction that starts while a session is mid-append QUEUES behind it; the guard that says
/// "nothing unread, safe to rewrite" had already answered before the wait. The compactor then read
/// the file WITH the new entry in it, kept it (it was recent), and re-anchored the tailer's cursor
/// to the rewritten file's end — past the entry. It survived in the file and was never mirrored: the
/// owner's question was answered on the channel and never on the phone.
///
/// The gate is held here the way the helper holds it, the compaction is started underneath the
/// hold, and the append lands while it waits. Asked inside the gate, the guard sees the append and
/// the compaction declines; the entry is then emitted by the next poll like any other. A positive
/// control follows, because a guard that always refused would pass the first half.
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class CompactionAsksItsGuardInsideTheGateTests : IDisposable
{
    /// <summary>Well past COMPACT_ABOVE_ENTRIES, so the compactor has work to do.</summary>
    const int ENTRIES_ABOVE_THRESHOLD = 120;

    /// <summary>
    /// Inside the compactor's one-second lock budget with room to spare: a hold longer than the
    /// budget makes it give up, which would pass this test with the defect intact.
    /// </summary>
    const int HOLD_BEFORE_THE_APPEND_MILLISECONDS = 300;

    const string ORCH_ID = "orch-x";

    /// <summary>Short enough that the polls below span it; the rule itself is not this file's subject.</summary>
    static readonly TimeSpan TRAILING_ENTRY_QUIET = TimeSpan.FromMilliseconds(20);

    readonly string _tempFolder;
    readonly string _channelFile;
    readonly IDiscoveredChannel _channel;

    public CompactionAsksItsGuardInsideTheGateTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-compaction-guard-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
        _channel = DiscoveredChannel_Factory.Create_ForImplementer(ORCH_ID, "imp-1", _channelFile);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void AnEntryAppendedWhileCompactionWaitsForTheGate_IsStillMirrored()
    {
        Write_LongChannel();

        // A SHORT TRAILING QUIET, because this file's subject is the compaction guard and not the
        // tailer's quiet rule. The shipped 4 s hold on a trailing entry is wall-clock, so the six
        // rapid polls below would return nothing at all and the "was it mirrored" assertion would
        // fail for a reason that has nothing to do with the gate.
        var tailer = ChannelTailer_Factory.Create_Fresh(TRAILING_ENTRY_QUIET, Clock_Factory.Create_System());
        var log = new RecordingLog();

        // First sight registers the file at its end and emits nothing; the channel now counts as polled.
        tailer.Poll([_channel]);

        long? compactionResult = null;
        Task? compaction = null;

        var held = ChannelWrite_Lock.Try_Run_Serialised(_channelFile, TimeSpan.FromSeconds(5), () =>
        {
            compaction = Task.Run(() => compactionResult = Channel_CompactionStep.Compact_IfAllowed(tailer, _channelFile, log, ORCH_ID));

            // Let it reach the gate, then land the append underneath it — the incident's shape.
            Thread.Sleep(HOLD_BEFORE_THE_APPEND_MILLISECONDS);

            File.AppendAllText(_channelFile, Build_Entry(ENTRIES_ABOVE_THRESHOLD + 1, "the owner's answer"));
        }, out _);

        Assert.True(held, "the test could not take the gate it is testing against");

        var compactionTask = compaction ?? throw new Exception("the compaction task was never started inside the hold");

        Assert.True(compactionTask.Wait(TimeSpan.FromSeconds(20)), "compaction never returned after the gate was released");

        Assert.True(
            compactionResult == null,
            "THE DEFECT: the compactor rewrote the channel with an unmirrored entry in it — its guard "
            + "answered before the wait for the gate, so it never saw the append that landed during it");

        var emitted = Collect_Entries(tailer, polls: 6);

        Assert.Contains(emitted, entry => entry.Index == ENTRIES_ABOVE_THRESHOLD + 1);

        // POSITIVE CONTROL: with the entry delivered, the same call compacts. A guard that refused
        // unconditionally would have passed everything above.
        var newLength = Channel_CompactionStep.Compact_IfAllowed(tailer, _channelFile, log, ORCH_ID);

        Assert.NotNull(newLength);
        Assert.Equal(Channel_Compactor.KEEP_RECENT_ENTRIES, ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile)).Count);
    }

    /// <summary>Polls as the mirror loop does, confirming every append, and returns what was emitted.</summary>
    IReadOnlyList<IChannelEntry> Collect_Entries(IChannelTailer tailer, int polls)
    {
        List<IChannelEntry> entries = [];

        for (var i = 0; i < polls; i++)
        {
            foreach (var append in tailer.Poll([_channel]).CompletedAppends)
            {
                entries.AddRange(append.Entries);
                tailer.Confirm_Append(append.Channel.FilePath);
            }

            // The trailing entry is released only after a quiet stretch of WALL CLOCK, so the polls
            // have to be spread across one rather than fired back to back.
            Thread.Sleep((int)TRAILING_ENTRY_QUIET.TotalMilliseconds + 10);
        }

        return entries;
    }

    void Write_LongChannel()
    {
        var text = new StringBuilder("# SUPERVISION CHANNEL\n\n");

        for (var index = 1; index <= ENTRIES_ABOVE_THRESHOLD; index++)
            text.Append(Build_Entry(index, $"entry {index}"));

        File.WriteAllText(_channelFile, text.ToString());
    }

    static string Build_Entry(int index, string subject)
    {
        return $"## [{index}] FROM implementer — 2026-09-10 11:00 — {subject}\n\nbody {index}\n\n";
    }

    sealed class RecordingLog : IOrchestrationLog
    {
        public List<string> Warnings { get; } = [];

        public void Log_Info(string orchId, string message)
        {
        }

        public void Log_Warning(string orchId, string message)
        {
            Warnings.Add(message);
        }

        public void Log_Error(string orchId, string message, Exception? exception)
        {
        }

        public event Action<IOrchestrationLogEntry>? EntryLogged
        {
            add { }
            remove { }
        }
    }
}
