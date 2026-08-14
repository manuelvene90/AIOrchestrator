using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.GeneralSupervision;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// rev-5 F7: the promotion gate read the LIVE owner channel only, so compaction could hide a filed
/// handover and the app would refuse a solo for something it had already done.
///
/// Direct hit on decision 13. `Channel_Compactor` moves all but the newest 45 entries into a sibling
/// archive once a channel passes 90, `owner-channel.md` is on the discovery list, and the repo already
/// ships a helper that spans both — written after `option-lab-2` compacted two minutes after a
/// delivery and the app spent the rest of the evening telling the owner their answered message was
/// still waiting.
///
/// The failure here is the same shape: solo files its handover, the request lapses unanswered after
/// twelve hours, the solo keeps working, the channel crosses 90 entries and compacts, and the next
/// ask is answered "file your HANDOVER entry first" — instructing it to do the thing it did a day
/// earlier.
/// </summary>
public class ChannelHistoryReadTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _channelFile;

    public ChannelHistoryReadTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-history-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _channelFile = Path.Combine(_tempRoot, "owner-channel.md");
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// THE HANDOVER SURVIVES COMPACTION. Written through the real archive path, so the fixture cannot
    /// disagree with the compactor about where history goes.
    /// </summary>
    [Fact]
    public void AHandoverEntryInTheArchiveStillCounts()
    {
        Write(Channel_Compactor.Build_ArchiveFilePath(_channelFile), Entry(7, "solo", "HANDOVER — the parser is the hard part"));
        Write(_channelFile, Entry(52, "solo", "still working on the screener"));

        Assert.True(HandoverEntry_Detector.Has_HandoverEntry(ChannelHistory_Counter.Read_Entries(_channelFile)));

        // And the live file ALONE does not — which is the whole finding, stated so this test cannot
        // pass because the entry happened to be in both.
        Assert.False(HandoverEntry_Detector.Has_HandoverEntry(
            ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile))));
    }

    /// <summary>
    /// ARCHIVE FIRST, because it holds the OLDER entries and anything reading them in order should
    /// meet the conversation as it happened.
    /// </summary>
    [Fact]
    public void HistoryReadsOldestFirst()
    {
        Write(Channel_Compactor.Build_ArchiveFilePath(_channelFile), Entry(1, "owner", "the old one"));
        Write(_channelFile, Entry(2, "solo", "the new one"));

        var entries = ChannelHistory_Counter.Read_Entries(_channelFile);

        Assert.Equal(2, entries.Count);
        Assert.Equal("the old one", entries[0].Subject);
        Assert.Equal("the new one", entries[1].Subject);
    }

    /// <summary>
    /// No archive is the ordinary case and must read exactly as before — most channels never compact.
    /// </summary>
    [Fact]
    public void AChannelWithNoArchiveReadsItsLiveFile()
    {
        Write(_channelFile, Entry(1, "solo", "HANDOVER — everything you need"));

        Assert.True(HandoverEntry_Detector.Has_HandoverEntry(ChannelHistory_Counter.Read_Entries(_channelFile)));
    }

    /// <summary>A channel that does not exist at all is empty rather than an exception.</summary>
    [Fact]
    public void AMissingChannelIsEmpty()
    {
        Assert.Empty(ChannelHistory_Counter.Read_Entries(Path.Combine(_tempRoot, "nothing-here.md")));
    }

    static void Write(string path, string content)
    {
        File.WriteAllText(path, content);
    }

    static string Entry(int index, string author, string subject)
    {
        return $"## [{index}] FROM {author} — 2026-08-13 13:00 — {subject}\n\nbody\n";
    }
}
