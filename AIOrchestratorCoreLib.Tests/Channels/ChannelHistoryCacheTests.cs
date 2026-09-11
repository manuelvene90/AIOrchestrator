using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Diagnostics;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE CHANNEL CACHE MUST NEVER SERVE TEXT THE CHANNEL NO LONGER CONTAINS.
///
/// <para>
/// A cache over the agents' own memory is the kind of change that is easy to get 99% right and
/// catastrophic in the remaining 1%: an answer built from a channel's previous contents is not slow,
/// it is WRONG, and it is wrong invisibly — the supervisor is told the implementer has not answered
/// long after it did. That is the class of defect CLAUDE.md decision 13 was written for.
/// </para>
/// <para>
/// SO THE INVALIDATION IS PINNED FROM BOTH SIDES: it must serve a hit when the file is byte-for-byte
/// the one it parsed (otherwise it saves nothing), and it must miss on every change — a longer file,
/// a shorter file, and a file rewritten to exactly the same LENGTH, which is the one case a length
/// alone cannot see and the one compaction can produce.
/// </para>
/// <para>
/// AND IT MUST NOT BE TIME-BASED, which is asserted here by never advancing any clock: every miss in
/// these tests is caused by a change to the file and by nothing else, and every hit survives however
/// long the test takes.
/// </para>
/// </summary>
public class ChannelHistoryCacheTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;

    public ChannelHistoryCacheTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-history-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void AnUnchangedFile_IsReadOnce_HoweverManyAskers()
    {
        Write_Entries(1, 2, 3);

        using var counting = TickIo_Counters.Begin_Scope();

        for (var asker = 0; asker < 12; asker++)
            Assert.Equal(3, ChannelHistory_Cache.Read_Entries(_channelFile).Count);

        Assert.Equal(1, counting.Counts.TextFileReads);
    }

    /// <summary>
    /// The ordinary case: a channel is append-only, so every real change makes it longer. A tick that
    /// ran before the append and one that runs after must not be told the same thing.
    /// </summary>
    [Fact]
    public void AnAppendedFile_IsReadAgain_AndTheNewEntryIsThere()
    {
        Write_Entries(1, 2, 3);

        using var counting = TickIo_Counters.Begin_Scope();

        Assert.Equal(3, ChannelHistory_Cache.Read_Entries(_channelFile).Count);

        Append_Entry(4);

        var after = ChannelHistory_Cache.Read_Entries(_channelFile);

        Assert.Equal(4, after.Count);
        Assert.Equal(4, after[^1].Index);
        Assert.Equal(2, counting.Counts.TextFileReads);
    }

    /// <summary>
    /// COMPACTION'S SHAPE: the live file gets SHORTER and its oldest entries move to the archive. A
    /// cache that kept serving the pre-compaction parse would hand every reader entries the live file
    /// no longer holds — and would keep doing it for as long as the file stayed that size.
    /// </summary>
    [Fact]
    public void AShortenedFile_IsReadAgain()
    {
        Write_Entries(1, 2, 3, 4, 5);

        Assert.Equal(5, ChannelHistory_Cache.Read_Entries(_channelFile).Count);

        Write_Entries(4, 5);

        Assert.Equal(2, ChannelHistory_Cache.Read_Entries(_channelFile).Count);
    }

    /// <summary>
    /// THE ONE CASE A LENGTH CANNOT SEE, which is why the key is a pair. The stamp is forced to a
    /// distinct value rather than trusted to the filesystem's clock: on a coarse timestamp this test
    /// would otherwise pass or fail depending on how fast the machine is, and a test that is a race
    /// pins nothing.
    /// </summary>
    [Fact]
    public void AFileRewrittenToTheSameLength_IsReadAgain()
    {
        // The two differ only in the author and in how many 'a's pad the subject, so that the file's
        // LENGTH is identical either way and only the stamp can tell them apart.
        var first = "## [1] FROM supervisor — 2026-09-09 10:00 — aaaa\nbody\n\n";
        var second = "## [1] FROM implementer — 2026-09-09 10:00 — aaa\nbody\n\n";

        Assert.Equal(first.Length, second.Length);

        File.WriteAllText(_channelFile, first);
        File.SetLastWriteTimeUtc(_channelFile, new DateTime(2026, 9, 9, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(ChannelAuthors.Supervisor, ChannelHistory_Cache.Read_Entries(_channelFile)[0].Author);

        File.WriteAllText(_channelFile, second);
        File.SetLastWriteTimeUtc(_channelFile, new DateTime(2026, 9, 9, 10, 0, 1, DateTimeKind.Utc));

        Assert.Equal(ChannelAuthors.Implementer, ChannelHistory_Cache.Read_Entries(_channelFile)[0].Author);
    }

    /// <summary>
    /// A member's channel does not exist until its first entry, and every tick asks about it anyway.
    /// Remembering "empty" would remember it for ever, because an absent file has no stamp to
    /// invalidate against.
    /// </summary>
    [Fact]
    public void AMissingFile_IsEmpty_AndIsNotRememberedAsEmpty()
    {
        Assert.Empty(ChannelHistory_Cache.Read_Entries(_channelFile));

        Write_Entries(1);

        Assert.Single(ChannelHistory_Cache.Read_Entries(_channelFile));
    }

    /// <summary>
    /// The counter spans live file AND archive (decision 13), and it now goes through the cache. The
    /// two must still add up: a count taken from the cache and a count taken from the disk are the
    /// same count.
    /// </summary>
    [Fact]
    public void TheHistoryCounter_SpansTheArchive_ThroughTheCache()
    {
        Write_Entries(46, 47);
        File.WriteAllText(
            Channel_Compactor.Build_ArchiveFilePath(_channelFile),
            string.Concat(Enumerable.Range(1, 45).Select(index => $"## [{index}] FROM supervisor — 2026-09-09 10:00 — e{index}\nbody\n\n")));

        Assert.Equal(47, ChannelHistory_Counter.Read_AllEntries(_channelFile).Count);

        Append_Entry(48);

        Assert.Equal(48, ChannelHistory_Counter.Read_AllEntries(_channelFile).Count);
    }

    void Write_Entries(params int[] indexes)
    {
        File.WriteAllText(_channelFile, string.Concat(indexes.Select(Build_Entry)));
    }

    void Append_Entry(int index)
    {
        File.AppendAllText(_channelFile, Build_Entry(index));
    }

    static string Build_Entry(int index)
    {
        return $"## [{index}] FROM supervisor — 2026-09-09 10:00 — entry {index}\nbody of entry {index}\n\n";
    }
}
