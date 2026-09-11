using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Diagnostics;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE COMPACTION GATE: what it costs to say "no", and that the cheapening never says no to a file
/// that should have been compacted.
///
/// <para>
/// WHY THIS EXISTS. <c>Compact_IfNeeded</c> is offered every channel of every orchestration on every
/// 2-second tick, and for all but the oldest the answer is "far short of 90 entries". It used to
/// reach that answer by taking the channel's write gate, opening the file, reading all of it and
/// building an entry object per header — thirty times a minute, per channel, to throw the lot away.
/// It now answers from the length the existence stat already returned. That is invisible to every
/// assertion about the file's CONTENT, which is unchanged either way, so the only way to pin it is to
/// count the reads.
/// </para>
/// <para>
/// AND THE FLOOR HAS TO BE PROVEN, not asserted in prose. <c>MIN_ENTRY_BYTES</c> claims no entry can
/// occupy fewer than 11 bytes; if that were ever too generous the length gate would skip a channel
/// that really is over the threshold, and the failure would be silent — the file simply grows for
/// ever instead of being archived. <see cref="TheSmallestPossibleChannel_OverTheThreshold_IsStillCompacted"/>
/// builds the file the constant is derived from and requires compaction to happen.
/// </para>
/// <para>
/// THE ARCHIVE-BEFORE-REWRITE ORDER IS NOT RE-PINNED HERE — <c>ChannelCompactorTests</c> owns it
/// (<c>Compact_WhenTheLiveFileCannotBeRewritten_ReturnsNull_AndLosesNoEntries</c>), and a second copy
/// of an assertion is the drift this codebase pays for repeatedly. This file is about the gate in
/// front of that decision, never about the decision.
/// </para>
/// </summary>
public class ChannelCompactionGateTests : IDisposable
{
    readonly string _tempFolder;

    public ChannelCompactionGateTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-compact-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void AChannelFarUnderTheThreshold_IsNotOpenedAtAll()
    {
        var channelFile = Write_Channel(entryCount: 10);

        using var counting = TickIo_Counters.Begin_Scope();

        Assert.Null(Channel_Compactor.Compact_IfNeeded(channelFile));

        Assert.Equal(0, counting.Counts.TextFileReads);
    }

    /// <summary>
    /// The control for the test above. Without it, "zero reads" would also be the reading of a
    /// counter that is not wired to anything — which is the shape that let a harness report sixteen
    /// confident failures about code it never ran (CLAUDE.md decision 20).
    /// </summary>
    [Fact]
    public void AChannelOverTheThreshold_IsOpened()
    {
        var channelFile = Write_Channel(entryCount: Channel_Compactor.COMPACT_ABOVE_ENTRIES + 1);

        using var counting = TickIo_Counters.Begin_Scope();

        Channel_Compactor.Compact_IfNeeded(channelFile);

        Assert.True(
            counting.Counts.TextFileReads > 0,
            "a channel over the threshold was not read, so the zero-read assertion in the test above "
            + "proves nothing: it would read zero for a counter that is not wired to the read at all.");
    }

    /// <summary>
    /// The file <c>MIN_ENTRY_BYTES</c> is derived from: the fewest bytes 91 entries can occupy, using
    /// the most permissive header the parser accepts and no body at all. Real channels are two orders
    /// of magnitude larger, so a floor that passes here passes for everything.
    /// </summary>
    [Fact]
    public void TheSmallestPossibleChannel_OverTheThreshold_IsStillCompacted()
    {
        const int TOTAL = Channel_Compactor.COMPACT_ABOVE_ENTRIES + 1;

        var channelFile = Path.Combine(_tempFolder, "smallest.md");

        // '## [n] FROM author' with every optional space dropped — the shortest text the header
        // regex matches — and nothing else on the line.
        File.WriteAllText(channelFile, string.Concat(Enumerable.Range(1, TOTAL).Select(index => $"##[{index}]FROM a\n")));

        Assert.Equal(TOTAL, ChannelEntry_Parser.Parse_All(File.ReadAllText(channelFile)).Count);

        Assert.True(
            new FileInfo(channelFile).Length >= (long)TOTAL * Channel_Compactor.MIN_ENTRY_BYTES,
            $"MIN_ENTRY_BYTES ({Channel_Compactor.MIN_ENTRY_BYTES}) is too high: {TOTAL} minimal entries "
            + $"occupy {new FileInfo(channelFile).Length} bytes, which is below the length gate's floor of "
            + $"{(long)TOTAL * Channel_Compactor.MIN_ENTRY_BYTES}. Channels over the threshold would stop "
            + "being compacted, silently — the file just grows.");

        Assert.NotNull(Channel_Compactor.Compact_IfNeeded(channelFile));

        var live = ChannelEntry_Parser.Parse_All(File.ReadAllText(channelFile));
        var archived = ChannelEntry_Parser.Parse_All(File.ReadAllText(Channel_Compactor.Build_ArchiveFilePath(channelFile)));

        Assert.Equal(Channel_Compactor.KEEP_RECENT_ENTRIES, live.Count);
        Assert.Equal(TOTAL, live.Count + archived.Count);
    }

    /// <summary>
    /// The pre-filter is allowed to be cheap; it is not allowed to disagree with the parse. Same text,
    /// same header pattern, same answer — including for the shapes that are easy to split wrongly: a
    /// file with no trailing newline, CRLF line endings, and a preamble before the first header.
    /// </summary>
    [Theory]
    [InlineData("", 0)]
    [InlineData("# seed\n\nno headers here\n", 0)]
    [InlineData("## [1] FROM supervisor — 2026-09-09 10:00 — one\nbody", 1)]
    [InlineData("# seed\r\n\r\n## [1] FROM supervisor — d — a\r\nbody\r\n\r\n## [2] FROM imp-1 — d — b\r\n", 2)]
    [InlineData("##[7]FROM a", 1)]
    public void TheCheapCount_AgreesWithTheParse(string channelText, int expected)
    {
        Assert.Equal(expected, ChannelEntry_Parser.Count_Entries(channelText));
        Assert.Equal(expected, ChannelEntry_Parser.Parse_All(channelText).Count);
    }

    string Write_Channel(int entryCount)
    {
        var path = Path.Combine(_tempFolder, "channel.md");

        File.WriteAllText(path, "# channel seed\n\n");

        for (var index = 1; index <= entryCount; index++)
            File.AppendAllText(path, $"## [{index}] FROM supervisor — 2026-09-09 10:00 — entry {index}\nbody of entry {index}\n\n");

        return path;
    }
}
