using System.Text;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;
using AIOrchestratorCoreLib.Tailing.TailerPollResult;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Tailing;

public class ChannelTailerTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;
    readonly IDiscoveredChannel _channel;

    public ChannelTailerTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-tailer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
        _channel = DiscoveredChannel_Factory.Create_ForImplementer("orch-x", "imp-1", _channelFile);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void Poll_FirstSighting_SkipsExistingHistory()
    {
        File.WriteAllText(_channelFile, "## [1] FROM supervisor — d — old entry\n\nold body\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();

        var result = tailer.Poll([_channel]);
        var quietResult1 = tailer.Poll([_channel]);
        var quietResult2 = tailer.Poll([_channel]);

        Assert.Empty(result.CompletedAppends);
        Assert.Empty(quietResult1.CompletedAppends);
        Assert.Empty(quietResult2.CompletedAppends);
    }

    [Fact]
    public void Poll_AppendedEntry_EmittedAfterQuietPolls()
    {
        File.WriteAllText(_channelFile, "seed\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile, "## [1] FROM implementer — d — report\n\nall green\n");

        var readPoll = tailer.Poll([_channel]);
        var quietPoll1 = tailer.Poll([_channel]);
        var quietPoll2 = tailer.Poll([_channel]);

        Assert.Empty(readPoll.CompletedAppends);
        Assert.Empty(quietPoll1.CompletedAppends);

        var append = Assert.Single(quietPoll2.CompletedAppends);
        var entry = Assert.Single(append.Entries);
        Assert.Equal("report", entry.Subject);
        Assert.Equal("all green", entry.Body);
    }

    [Fact]
    public void Poll_NextHeaderArrives_CompletesPreviousEntryImmediately()
    {
        File.WriteAllText(_channelFile, "seed\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile,
            "## [1] FROM supervisor — d — orders\n\ndo X\n\n" +
            "## [2] FROM implementer — d — in progress\n");

        var result = tailer.Poll([_channel]);

        var append = Assert.Single(result.CompletedAppends);
        var entry = Assert.Single(append.Entries);
        Assert.Equal(1, entry.Index);
        Assert.Equal("orders", entry.Subject);
    }

    [Fact]
    public void Poll_ConfirmedEntry_EmittedOnce_NeverDuplicated()
    {
        File.WriteAllText(_channelFile, "seed\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile, "## [1] FROM implementer — d — report\n\nbody\n");

        var emittedCount = 0;

        for (var i = 0; i < 6; i++)
        {
            var pollResult = tailer.Poll([_channel]);
            emittedCount += pollResult.CompletedAppends.Sum(a => a.Entries.Count);

            // What the bridge does after a successful send. Without it the entry is deliberately
            // re-emitted (see Poll_UnconfirmedEntry_KeepsBeingReEmittedUntilConfirmed).
            foreach (var append in pollResult.CompletedAppends)
                tailer.Confirm_Append(append.Channel.FilePath);
        }

        Assert.Equal(1, emittedCount);
    }

    [Fact]
    public void Poll_UnconfirmedEntry_KeepsBeingReEmittedUntilConfirmed()
    {
        File.WriteAllText(_channelFile, "seed\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile, "## [1] FROM implementer — d — report\n\nbody\n");

        var firstEmission = Emit_UntilAnAppendArrives(tailer);
        var withoutConfirmation = tailer.Poll([_channel]);

        tailer.Confirm_Append(_channelFile);
        var afterConfirmation = tailer.Poll([_channel]);

        Assert.Equal("report", Assert.Single(Assert.Single(firstEmission.CompletedAppends).Entries).Subject);
        Assert.Equal("report", Assert.Single(Assert.Single(withoutConfirmation.CompletedAppends).Entries).Subject);
        Assert.Empty(afterConfirmation.CompletedAppends);
    }

    [Fact]
    public void Poll_UnreadableChannel_IsReported_AndTheOtherChannelsStillTail()
    {
        var otherFile = Path.Combine(_tempFolder, "other-channel.md");
        var otherChannel = DiscoveredChannel_Factory.Create_ForImplementer("orch-x", "imp-2", otherFile);

        File.WriteAllText(_channelFile, "seed\n");
        File.WriteAllText(otherFile, "seed\n");

        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel, otherChannel]);

        // Grown but unopenable: the tailer sees new bytes it cannot read. Before the per-channel
        // guard this threw out of Poll and took the OTHER channel's already-built append with it.
        using var exclusiveHandle = new FileStream(_channelFile, FileMode.Open, FileAccess.Write, FileShare.None);
        exclusiveHandle.Seek(0, SeekOrigin.End);
        exclusiveHandle.Write(Encoding.UTF8.GetBytes("## [1] FROM supervisor — d — unreadable\n\nbody\n"));
        exclusiveHandle.Flush();

        File.AppendAllText(otherFile, "## [1] FROM implementer — d — readable\n\nbody\n");

        var poll1 = tailer.Poll([_channel, otherChannel]);
        var poll2 = tailer.Poll([_channel, otherChannel]);
        var poll3 = tailer.Poll([_channel, otherChannel]);

        var emittedSubjects = new[] { poll1, poll2, poll3 }
            .SelectMany(result => result.CompletedAppends)
            .SelectMany(append => append.Entries)
            .Select(entry => entry.Subject)
            .ToList();

        Assert.Contains(poll1.UnreadableFiles, report => report.Contains(_channelFile, StringComparison.Ordinal));
        Assert.Equal(["readable"], emittedSubjects);
    }

    ITailerPollResult Emit_UntilAnAppendArrives(IChannelTailer tailer)
    {
        for (var i = 0; i < 5; i++)
        {
            var pollResult = tailer.Poll([_channel]);

            if (pollResult.CompletedAppends.Count > 0)
                return pollResult;
        }

        throw new Exception("The tailer emitted nothing within 5 polls — the quiet-poll flush never happened.");
    }

    [Fact]
    public void OwedEntry_ReadButNotYetEmitted_IsDeclaredUndelivered()
    {
        File.WriteAllText(_channelFile, "seed\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile, "## [1] FROM implementer — d — report\n\nbody\n");

        // The poll that READS the entry emits nothing: it is the trailing entry and the quiet-poll
        // window has not elapsed. Those bytes are owed to Telegram all the same, and this is the
        // window in which the bridge used to ask "does this channel owe anything?" and be told no.
        var readPoll = tailer.Poll([_channel]);

        Assert.Empty(readPoll.CompletedAppends);
        Assert.True(tailer.Has_UndeliveredEntries(_channelFile));
    }

    [Fact]
    public void OwedEntry_CompactionOfferedBeforeItWasEmitted_IsHeldBackAndStillMirrored()
    {
        File.WriteAllText(_channelFile, "seed\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile, "## [1] FROM implementer — d — report\n\nbody\n");

        tailer.Poll([_channel]);
        Compact_IfTheTailerAllowsIt(tailer, _channelFile);

        var delivered = Collect_DeliveredEntries(tailer, polls: 4, [_channel]);

        // Without the guard the rewrite lands on a channel that still owes this entry, Set_Offset
        // re-anchors past it, and it is never mirrored — gone from Telegram forever, with the file
        // on disk intact, which is why nobody notices.
        Assert.Equal("report", Assert.Single(delivered).Subject);
    }

    [Fact]
    public void DeferredChannel_CompactedWhileItsCursorWasFrozen_StillDeliversItsCatchUpBurst()
    {
        var deferredFile = Path.Combine(_tempFolder, "deferred-channel.md");
        var deferredChannel = DiscoveredChannel_Factory.Create_ForImplementer("orch-x", "imp-2", deferredFile);

        File.WriteAllText(_channelFile, "seed\n");
        File.WriteAllText(deferredFile, "seed\n");

        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel, deferredChannel]);

        File.AppendAllText(deferredFile, "## [1] FROM supervisor — d — while you were away\n\nbody\n");

        // The topic is DEFERRED, so Find_ActiveChannels drops it: these ticks poll the active
        // channel ONLY and the deferred cursor freezes, which is exactly what the owner is promised
        // — everything it produced replays when they un-defer. Compaction still visits every
        // discovered channel each tick, deferred ones included.
        for (var i = 0; i < 3; i++)
        {
            tailer.Poll([_channel]);
            Compact_IfTheTailerAllowsIt(tailer, deferredFile);
        }

        var delivered = Collect_DeliveredEntries(tailer, polls: 4, [_channel, deferredChannel]);

        // The F1 guard cannot save this one and that is the point: the channel was never polled, so
        // its Pending is empty and "does it owe anything?" answers false. Only "was it polled?" sees it.
        Assert.Equal("while you were away", Assert.Single(delivered).Subject);
    }

    /// <summary>
    /// The compaction step of a mirror tick (<c>BridgeEngineModel.Compact_LongChannels</c>): archive
    /// the older entries and re-anchor the tailer to the rewritten file — but only if the tailer
    /// allows it. The rewrite KEEPS the newest entry, as <c>Channel_Compactor</c> does: what strands
    /// an entry is the cursor jumping to EOF, not the text leaving the file.
    /// </summary>
    static void Compact_IfTheTailerAllowsIt(IChannelTailer tailer, string channelFilePath)
    {
        if (tailer.Has_UndeliveredEntries(channelFilePath))
            return;

        if (!tailer.Was_PolledInLastPoll(channelFilePath))
            return;

        var keptTail = File.ReadAllText(channelFilePath).Split("## ").LastOrDefault() ?? string.Empty;

        File.WriteAllText(channelFilePath, $"## [0] FROM app — d — compacted\n\nolder entries archived\n\n## {keptTail}");
        tailer.Set_Offset(channelFilePath, new FileInfo(channelFilePath).Length);
    }

    /// <summary>
    /// Polls as the mirror loop does, confirming every append — a delivery that lands. Without the
    /// confirmation the tailer would re-emit the same entry on every poll, by contract, and the
    /// count would say nothing about whether it was preserved.
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

    [Fact]
    public void Poll_TruncatedFile_ReportsAnomalyAndRecovers()
    {
        File.WriteAllText(_channelFile, "some long seed content here\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.WriteAllText(_channelFile, "short\n");

        var result = tailer.Poll([_channel]);

        Assert.Contains(_channelFile, result.TruncatedFiles);
    }

    [Fact]
    public void Get_OffsetsSnapshot_RestartWithPersistedOffsets_DoesNotReMirrorOldEntries()
    {
        File.WriteAllText(_channelFile, "seed\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile, "## [1] FROM implementer — d — report\n\nbody\n");
        tailer.Poll([_channel]);
        tailer.Poll([_channel]);
        tailer.Poll([_channel]);

        // The entry was delivered, which is what lets the persisted cursor move past it.
        tailer.Confirm_Append(_channelFile);

        var restartedTailer = ChannelTailer_Factory.Create(tailer.Get_OffsetsSnapshot());

        var afterRestart1 = restartedTailer.Poll([_channel]);
        var afterRestart2 = restartedTailer.Poll([_channel]);
        var afterRestart3 = restartedTailer.Poll([_channel]);

        Assert.Empty(afterRestart1.CompletedAppends);
        Assert.Empty(afterRestart2.CompletedAppends);
        Assert.Empty(afterRestart3.CompletedAppends);
    }

    [Fact]
    public void Get_OffsetsSnapshot_EntryNeverConfirmed_IsMirroredAgainAfterARestart()
    {
        File.WriteAllText(_channelFile, "seed\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile, "## [1] FROM implementer — d — report\n\nbody\n");
        Emit_UntilAnAppendArrives(tailer);

        // No Confirm_Append: the send failed, and the process dies still owing this entry. The
        // persisted cursor must therefore point BEFORE it, so the next process re-sends it — an
        // entry the owner never saw is not "already mirrored".
        var restartedTailer = ChannelTailer_Factory.Create(tailer.Get_OffsetsSnapshot());

        var afterRestart1 = restartedTailer.Poll([_channel]);
        var afterRestart2 = restartedTailer.Poll([_channel]);
        var afterRestart3 = restartedTailer.Poll([_channel]);

        var reEmitted = new[] { afterRestart1, afterRestart2, afterRestart3 }
            .SelectMany(result => result.CompletedAppends)
            .SelectMany(append => append.Entries)
            .ToList();

        Assert.Equal("report", Assert.Single(reEmitted).Subject);
    }
}
