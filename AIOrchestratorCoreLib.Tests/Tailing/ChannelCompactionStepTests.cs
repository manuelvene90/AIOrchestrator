using System.Text;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Tailing;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Tailing;

/// <summary>
/// Drives <see cref="Channel_CompactionStep"/> — the same code the mirror tick runs, guards and all.
/// These assertions used to run against a replica of that guard sequence written inside the test
/// file, which is a green that certifies a copy of the code rather than the code.
/// </summary>
public class ChannelCompactionStepTests : IDisposable
{
    /// <summary>Comfortably past <see cref="Channel_Compactor.COMPACT_ABOVE_ENTRIES"/> (90), so compaction really runs.</summary>
    const int ENTRIES_ABOVE_THRESHOLD = 95;

    readonly string _tempFolder;
    readonly string _channelFile;
    readonly IDiscoveredChannel _channel;

    public ChannelCompactionStepTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-compaction-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
        _channel = DiscoveredChannel_Factory.Create_ForImplementer("orch-x", "imp-1", _channelFile);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void PolledChannelOwingNothing_IsCompacted()
    {
        // The fixture's own proof. Without it the two guard tests below would have a second route to
        // green — the entry survives because compaction was blocked, or because the file was too
        // short to compact at all — and a test that allows either pins neither.
        Write_LongChannel(_channelFile);
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        var lengthBefore = new FileInfo(_channelFile).Length;
        var newLength = Channel_CompactionStep.Compact_IfAllowed(tailer, _channelFile);

        Assert.NotNull(newLength);
        Assert.True(newLength < lengthBefore, $"the live file should have shrunk: {lengthBefore} -> {newLength}");
        Assert.True(File.Exists(Channel_Compactor.Build_ArchiveFilePath(_channelFile)), "the archive should exist");
    }

    [Fact]
    public void CompactedChannel_ReAnchorsTheCursor_SoTheShrinkIsNotReadAsAProtocolAnomaly()
    {
        Write_LongChannel(_channelFile);
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        var newLength = Channel_CompactionStep.Compact_IfAllowed(tailer, _channelFile);
        var afterCompaction = tailer.Poll([_channel]);

        // Re-anchoring is the whole reason compaction may touch a tailed file at all. Without it the
        // cursor still points past the end of the rewritten file, the next poll sees a file SHORTER
        // than its offset, and that is the append-only protocol breaking as far as the tailer knows:
        // it reports the anomaly and resets. The suite never pinned this, so the Set_Offset call
        // could be deleted outright with every test still green (found by rev-4, 2026-08-13).
        Assert.NotNull(newLength);
        Assert.Empty(afterCompaction.TruncatedFiles);
    }

    [Fact]
    public void ChannelOwingAnUnemittedEntry_IsNotCompacted_AndTheEntryIsStillMirrored()
    {
        Write_LongChannel(_channelFile);
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile, Build_Entry(ENTRIES_ABOVE_THRESHOLD + 1, "the newest thing said"));

        // The poll that READS the entry emits nothing — it is the trailing entry and the quiet-poll
        // window has not elapsed — so the bytes are owed to Telegram and held in Pending alone.
        tailer.Poll([_channel]);

        var newLength = Channel_CompactionStep.Compact_IfAllowed(tailer, _channelFile);
        var delivered = Collect_DeliveredEntries(tailer, polls: 4, [_channel]);

        Assert.Null(newLength);
        Assert.Equal("the newest thing said", Assert.Single(delivered).Subject);
    }

    [Fact]
    public void ChannelAppendedToAfterItsPoll_IsNotCompacted_AndTheNewEntryIsStillMirrored()
    {
        Write_LongChannel(_channelFile);
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        // The mirror tick appends to channel files BETWEEN the poll and the compaction step, on the
        // same thread: Check_LedgerHealth_Async, Check_ChannelShapes_Async and Push_PeriodicStatus_Async
        // all write entries after the poll has run. The tailer has not seen these bytes, so nothing
        // it holds says they exist — Pending is empty and the channel looks perfectly clear.
        File.AppendAllText(_channelFile, Build_Entry(ENTRIES_ABOVE_THRESHOLD + 1, "written after the poll"));

        var newLength = Channel_CompactionStep.Compact_IfAllowed(tailer, _channelFile);
        var delivered = Collect_DeliveredEntries(tailer, polls: 4, [_channel]);

        // Compacting here keeps this entry in the file — among the newest 45 — and returns EOF, so
        // Set_Offset parks the cursor past it. It is then gone from Telegram and intact on disk,
        // and it never reaches the log either: the per-entry log line lives inside Mirror_Append_Async.
        Assert.Null(newLength);
        Assert.Equal("written after the poll", Assert.Single(delivered).Subject);
    }

    [Fact]
    public void DeferredChannelWithAFrozenCursor_IsNotCompacted_AndStillDeliversItsCatchUpBurst()
    {
        var activeFile = Path.Combine(_tempFolder, "active-channel.md");
        var activeChannel = DiscoveredChannel_Factory.Create_ForImplementer("orch-x", "imp-2", activeFile);

        Write_LongChannel(_channelFile);
        File.WriteAllText(activeFile, "seed\n");

        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel, activeChannel]);

        File.AppendAllText(_channelFile, Build_Entry(ENTRIES_ABOVE_THRESHOLD + 1, "while you were away"));

        // DEFERRED: Find_ActiveChannels drops this channel, so these ticks poll the active one only
        // and the deferred cursor freezes — which is the catch-up the owner is promised. Compaction
        // still visits every discovered channel each tick, deferred ones included.
        List<long?> compactionResults = [];

        for (var i = 0; i < 3; i++)
        {
            tailer.Poll([activeChannel]);
            compactionResults.Add(Channel_CompactionStep.Compact_IfAllowed(tailer, _channelFile));
        }

        var delivered = Collect_DeliveredEntries(tailer, polls: 4, [_channel, activeChannel]);

        // The undelivered-entries guard cannot save this one, which is why it is a separate guard:
        // the channel was never polled, so its Pending is empty and "does it owe anything?" is false.
        Assert.All(compactionResults, result => Assert.Null(result));
        Assert.Equal("while you were away", Assert.Single(delivered).Subject);
    }

    static void Write_LongChannel(string channelFilePath)
    {
        var text = new StringBuilder("# SUPERVISION CHANNEL\n\n");

        for (var index = 1; index <= ENTRIES_ABOVE_THRESHOLD; index++)
            text.Append(Build_Entry(index, $"entry {index}"));

        File.WriteAllText(channelFilePath, text.ToString());
    }

    static string Build_Entry(int index, string subject)
    {
        return $"## [{index}] FROM implementer — 2026-08-13 09:00 — {subject}\n\nbody {index}\n\n";
    }

    /// <summary>
    /// Polls as the mirror loop does, confirming every append — a delivery that lands. Without the
    /// confirmation the tailer re-emits the same entry on every poll, by contract, and the count
    /// would say nothing about whether it was preserved.
    /// </summary>
    static IReadOnlyList<IChannelEntry> Collect_DeliveredEntries(
        IChannelTailer tailer,
        int polls,
        IReadOnlyList<IDiscoveredChannel> channels)
    {
        List<IChannelEntry> entries = [];

        for (var i = 0; i < polls; i++)
        {
            foreach (var append in tailer.Poll(channels).CompletedAppends)
            {
                entries.AddRange(append.Entries);
                tailer.Confirm_Append(append.Channel.FilePath);
            }
        }

        return entries;
    }
}
