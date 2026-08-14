using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// The count that decides whether the owner's message has been answered. It is compared against an
/// earlier reading, so the ONLY property that matters is that it never goes down.
/// </summary>
public class ChannelHistoryCounterTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;

    public ChannelHistoryCounterTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-history-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "owner-channel.md");
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void Count_CountsOnlyTheAskedAuthor()
    {
        File.WriteAllText(_channelFile, Build_Entries(("supervisor", 1), ("owner", 2), ("supervisor", 3), ("app", 4)));

        Assert.Equal(2, ChannelHistory_Counter.Count_Entries_ByAuthor(_channelFile, ChannelAuthors.Supervisor));
        Assert.Equal(1, ChannelHistory_Counter.Count_Entries_ByAuthor(_channelFile, ChannelAuthors.Owner));
    }

    /// <summary>
    /// The 2026-08-10 incident: `option-lab-2` compacted two minutes after an owner message was
    /// delivered, and 18 supervisor entries left the live file. A live-file-only count therefore
    /// FELL below the figure recorded at delivery, so no later answer could ever exceed it — the
    /// owner was told their message was still waiting after it had been answered, and the
    /// supervisor was nudged for a failure that never happened.
    /// </summary>
    [Fact]
    public void Count_DoesNotFall_WhenCompactionMovesEntriesIntoTheArchive()
    {
        File.WriteAllText(_channelFile, Build_Entries(("supervisor", 1), ("supervisor", 2), ("supervisor", 3)));

        var beforeCompaction = ChannelHistory_Counter.Count_Entries_ByAuthor(_channelFile, ChannelAuthors.Supervisor);
        Assert.Equal(3, beforeCompaction);

        // Exactly what the compactor does: the oldest entries MOVE to the sibling archive.
        File.WriteAllText(Channel_Compactor.Build_ArchiveFilePath(_channelFile), Build_Entries(("supervisor", 1), ("supervisor", 2)));
        File.WriteAllText(_channelFile, Build_Entries(("supervisor", 3)));

        Assert.Equal(beforeCompaction, ChannelHistory_Counter.Count_Entries_ByAuthor(_channelFile, ChannelAuthors.Supervisor));

        // And the supervisor's next answer still registers as growth, which is what clears the pending.
        File.AppendAllText(_channelFile, Build_Entries(("supervisor", 4)));

        Assert.Equal(beforeCompaction + 1, ChannelHistory_Counter.Count_Entries_ByAuthor(_channelFile, ChannelAuthors.Supervisor));
    }

    [Fact]
    public void Count_IsZero_ForAChannelThatDoesNotExistYet()
    {
        Assert.Equal(0, ChannelHistory_Counter.Count_Entries_ByAuthor(
            Path.Combine(_tempFolder, "absent.md"), ChannelAuthors.Supervisor));
    }

    /// <summary>
    /// THE ORDER IS THE CONTRACT, asserted on its own rather than inside a behaviour case. Every
    /// consumer of the whole history scans BACKWARDS for "the last X", so a reversed concatenation
    /// hands them the OLDEST match while still returning the right entries and the right count. That
    /// failure is invisible to any test that only checks membership, which is why this one checks
    /// position.
    /// </summary>
    [Fact]
    public void Read_AllEntries_PutsTheArchiveBeforeTheLiveFile()
    {
        File.WriteAllText(_channelFile, Build_Entries(("supervisor", 40), ("implementer", 41)));
        File.WriteAllText(Channel_Compactor.Build_ArchiveFilePath(_channelFile), Build_Entries(("supervisor", 1), ("owner", 2)));

        var all = ChannelHistory_Counter.Read_AllEntries(_channelFile);

        Assert.Equal(4, all.Count);
        Assert.Equal([1, 2, 40, 41], all.Select(entry => entry.Index));
    }

    /// <summary>
    /// The ordinary case — nothing archived yet — must be exactly the live file, not the live file
    /// plus an empty prefix that some caller then has to reason about.
    /// </summary>
    [Fact]
    public void Read_AllEntries_IsJustTheLiveFile_WhenNothingHasBeenArchived()
    {
        File.WriteAllText(_channelFile, Build_Entries(("supervisor", 1), ("implementer", 2)));

        Assert.Equal([1, 2], ChannelHistory_Counter.Read_AllEntries(_channelFile).Select(entry => entry.Index));
    }

    /// <summary>
    /// THE STATE THE WHOLE FIX IS ABOUT: a live file holding no conversation entry at all, every one of
    /// them compacted out. Three real member channels were in it on 2026-08-14 with a null `closedUtc`.
    /// A live-only read answers "there is no conversation here"; the whole history answers correctly.
    /// </summary>
    [Fact]
    public void Read_AllEntries_FindsTheConversation_WhenTheLiveFileIsAppOnly()
    {
        File.WriteAllText(_channelFile, Build_Entries(("app", 178), ("app", 179)));
        File.WriteAllText(Channel_Compactor.Build_ArchiveFilePath(_channelFile), Build_Entries(("supervisor", 68), ("app", 69)));

        var all = ChannelHistory_Counter.Read_AllEntries(_channelFile);

        Assert.Equal(68, all.Last(entry => entry.Author != ChannelAuthors.App).Index);
    }

    [Fact]
    public void Read_AllEntries_IsEmpty_ForAChannelThatDoesNotExistYet()
    {
        Assert.Empty(ChannelHistory_Counter.Read_AllEntries(Path.Combine(_tempFolder, "absent.md")));
    }

    static string Build_Entries(params (string Author, int Index)[] entries)
    {
        var text = "";

        foreach (var entry in entries)
            text += $"## [{entry.Index}] FROM {entry.Author} — 2026-08-10 15:18 — subject\n\nbody\n\n";

        return text;
    }
}
