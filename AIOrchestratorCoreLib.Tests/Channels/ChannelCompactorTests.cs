using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// Compaction rewrites the durable state of the whole system — nothing may be lost, and the
/// entry numbering must survive so the append-only protocol continues.
/// </summary>
public class ChannelCompactorTests : IDisposable
{
    readonly string _tempFolder;

    public ChannelCompactorTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-compact-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void Compact_ShortChannel_IsLeftAlone()
    {
        var channelFile = Write_Channel(10);
        var before = File.ReadAllText(channelFile);

        Assert.Null(Channel_Compactor.Compact_IfNeeded(channelFile));
        Assert.Equal(before, File.ReadAllText(channelFile));
        Assert.False(File.Exists(Channel_Compactor.Build_ArchiveFilePath(channelFile)));
    }

    [Fact]
    public void Compact_LongChannel_KeepsRecentEntries_ArchivesTheRest_LosesNothing()
    {
        const int TOTAL = 120;
        var channelFile = Write_Channel(TOTAL);

        var newLength = Channel_Compactor.Compact_IfNeeded(channelFile);

        Assert.NotNull(newLength);
        Assert.Equal(new FileInfo(channelFile).Length, newLength.Value);

        var liveEntries = ChannelEntry_Parser.Parse_All(File.ReadAllText(channelFile));
        var archived = ChannelEntry_Parser.Parse_All(File.ReadAllText(Channel_Compactor.Build_ArchiveFilePath(channelFile)));

        Assert.Equal(Channel_Compactor.KEEP_RECENT_ENTRIES, liveEntries.Count);
        Assert.Equal(TOTAL, liveEntries.Count + archived.Count);

        // Original indices survive, so the next entry number continues from the live tail.
        Assert.Equal(TOTAL, liveEntries[^1].Index);
        Assert.Equal(TOTAL + 1, ChannelEntry_Parser.Get_NextIndex(File.ReadAllText(channelFile)));

        // The live file points at the archive so a resuming session knows where history went.
        Assert.Contains(".archive.md", File.ReadAllText(channelFile));
    }

    [Fact]
    public void Compact_Twice_AppendsToTheSameArchive_WithoutLosingTheFirstBatch()
    {
        var channelFile = Write_Channel(120);
        Channel_Compactor.Compact_IfNeeded(channelFile);

        Append_Entries(channelFile, from: 121, count: 90);
        Channel_Compactor.Compact_IfNeeded(channelFile);

        var archived = ChannelEntry_Parser.Parse_All(File.ReadAllText(Channel_Compactor.Build_ArchiveFilePath(channelFile)));
        var live = ChannelEntry_Parser.Parse_All(File.ReadAllText(channelFile));

        Assert.Equal(210, archived.Count + live.Count);
        Assert.Equal(1, archived[0].Index);
    }

    [RequiresFileShareEnforcementFact]
    public void Compact_WhenTheLiveFileCannotBeRewritten_ReturnsNull_AndLosesNoEntries()
    {
        const int TOTAL = 120;
        var channelFile = Write_Channel(TOTAL);
        var before = File.ReadAllText(channelFile);

        // FileShare.Read rather than None: None would deny the compactor's own READ, and the run
        // would end long before the rewrite this test is about. Read lets it reach the rename and
        // then denies the replace, because this handle withholds Delete on the target.
        using (var liveFileLock = new FileStream(channelFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Null(Channel_Compactor.Compact_IfNeeded(channelFile));
        }

        // The guarantee that matters most: a failed compaction costs an update, never an entry.
        Assert.Equal(before, File.ReadAllText(channelFile));
        Assert.Equal(TOTAL, ChannelEntry_Parser.Parse_All(File.ReadAllText(channelFile)).Count);

        // And the archive is back to what it was — here, absent. The append that preceded the refused
        // rewrite is UNDONE, because the live file still holds every one of those entries and the next
        // pass will archive them again. Leaving it used to be the documented behaviour; see below.
        Assert.False(File.Exists(Channel_Compactor.Build_ArchiveFilePath(channelFile)));
    }

    /// <summary>
    /// THE LOOP THAT FILLED THE DISK. Compaction is offered every channel on every 2-second tick, and
    /// on Windows the rename-over is refused whenever anything holds the live file open. Each refused
    /// pass left its archive append behind, so every tick archived the same entries again: measured
    /// 2026-09-30, `da-vinci-fintech-suite-32/owner-channel.archive.md` was 427 MB holding 844
    /// distinct entries 682,908 times, and the per-tick screen that reads archives whole froze the app
    /// (mirror ticks of 60-97 s instead of 2).
    /// </summary>
    [RequiresFileShareEnforcementFact]
    public void Compact_RefusedOnEveryTick_ThenAllowed_ArchivesEachEntryExactlyOnce()
    {
        const int TOTAL = 120;
        var channelFile = Write_Channel(TOTAL);

        using (var liveFileLock = new FileStream(channelFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            for (var tick = 0; tick < 3; tick++)
                Assert.Null(Channel_Compactor.Compact_IfNeeded(channelFile));
        }

        Assert.NotNull(Channel_Compactor.Compact_IfNeeded(channelFile));

        var archived = ChannelEntry_Parser.Parse_All(File.ReadAllText(Channel_Compactor.Build_ArchiveFilePath(channelFile)));
        var live = ChannelEntry_Parser.Parse_All(File.ReadAllText(channelFile));

        Assert.Equal(TOTAL, archived.Count + live.Count);
        Assert.Equal(archived.Count, archived.Select(entry => entry.Index).Distinct().Count());
    }

    [RequiresFileShareEnforcementFact]
    public void Compact_RefusedRewrite_LeavesAnEarlierArchiveByteForByteAsItWas()
    {
        var channelFile = Write_Channel(120);
        Assert.NotNull(Channel_Compactor.Compact_IfNeeded(channelFile));

        var archiveFile = Channel_Compactor.Build_ArchiveFilePath(channelFile);
        var archiveBefore = File.ReadAllBytes(archiveFile);

        Append_Entries(channelFile, from: 121, count: 90);

        using (var liveFileLock = new FileStream(channelFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Null(Channel_Compactor.Compact_IfNeeded(channelFile));
        }

        Assert.Equal(archiveBefore, File.ReadAllBytes(archiveFile));
    }

    /// <summary>
    /// A refused rewrite used to vanish into a bare catch, which is why nobody saw the loop above for
    /// days. The reason now comes back to the caller, which is the one holding a log.
    /// </summary>
    [RequiresFileShareEnforcementFact]
    public void Compact_RefusedRewrite_SaysWhy()
    {
        var channelFile = Write_Channel(120);

        using (var liveFileLock = new FileStream(channelFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Null(Channel_Compactor.Compact_IfNeeded(channelFile, () => true, out var failure));
            Assert.NotNull(failure);
        }
    }

    [Fact]
    public void Compact_NothingToDo_IsNotAFailure()
    {
        var channelFile = Write_Channel(10);

        Assert.Null(Channel_Compactor.Compact_IfNeeded(channelFile, () => true, out var failure));
        Assert.Null(failure);
    }

    [RequiresFileShareEnforcementFact]
    public void Compact_WhenTheLiveFileCannotBeRewritten_LeavesNoTempFileBehind()
    {
        var channelFile = Write_Channel(120);

        using (var liveFileLock = new FileStream(channelFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Null(Channel_Compactor.Compact_IfNeeded(channelFile));
        }

        // The atomic write's temp file is an implementation detail. Every retry leaving one behind
        // would litter the folder a human opens to read the channel.
        Assert.Empty(Directory.GetFiles(_tempFolder, $"*{Atomic_FileWriter.TEMP_FILE_SUFFIX}"));
    }

    string Write_Channel(int entryCount)
    {
        var path = Path.Combine(_tempFolder, "channel.md");
        File.WriteAllText(path, "# channel seed\n\n");
        Append_Entries(path, from: 1, count: entryCount);
        return path;
    }

    static void Append_Entries(string channelFile, int from, int count)
    {
        for (var i = from; i < from + count; i++)
            File.AppendAllText(channelFile, $"## [{i}] FROM supervisor — 2026-08-07 10:00 — entry {i}\nbody of entry {i}\n\n");
    }
}
