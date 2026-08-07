using AIOrchestratorCoreLib.Channels;
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
