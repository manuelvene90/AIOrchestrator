using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE COUNT THAT DECIDES "HAS THE OWNER BEEN ANSWERED" MUST SEE A SOLO'S ANSWER.
///
/// It counted `FROM supervisor` entries only. A BASIC orchestration has no supervisor — its single
/// session signs `FROM solo` — so on every basic orchestration that number could never rise: the
/// pending reply was never cleared, and the app went on nudging the session and telling the owner
/// they were still waiting for replies they had already received. Owner, 2026-08-14, quoting the
/// nudge back at us: *"And why do I receive messages like this so very often?"*
///
/// It is the same root as the "🔴 Sup:" labels they reported minutes earlier — the app not knowing
/// the orchestration is basic — but a different mechanism, and this one is the expensive half: the
/// label was confusing, this one produced a message every few minutes for the life of the session.
///
/// The counter is compared against an earlier reading of itself, so the property is that it never
/// goes DOWN and rises when the owner is answered — by either role.
/// </summary>
public class ASolosAnswerConsumesTheOwnersWaitTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;

    public ASolosAnswerConsumesTheOwnersWaitTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-solowait-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "owner-channel.md");
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    /// <summary>
    /// The defect itself: the owner writes, the solo answers, and the count must have RISEN across
    /// that answer — that rise is the only thing that clears the pending reply.
    /// </summary>
    [Fact]
    public void ASolosAnswerRaisesTheCount()
    {
        File.WriteAllText(_channelFile, Build_Entries(("solo", 1), ("owner", 2)));

        var atDelivery = ChannelHistory_Counter.Count_OwnerFacingEntries(_channelFile);

        File.AppendAllText(_channelFile, Build_Entries(("solo", 3)));

        Assert.True(
            ChannelHistory_Counter.Count_OwnerFacingEntries(_channelFile) > atDelivery,
            "a solo's answer must register as growth, or the owner is nudged for a reply they already got");
    }

    /// <summary>
    /// And the crew case is untouched — the fix must not be "count everything". A supervisor's answer
    /// still counts, and the entries that are NOT answers still do not.
    /// </summary>
    [Fact]
    public void ASupervisorsAnswerStillRaisesIt_AndNothingElseDoes()
    {
        File.WriteAllText(_channelFile, Build_Entries(("supervisor", 1)));

        var atDelivery = ChannelHistory_Counter.Count_OwnerFacingEntries(_channelFile);

        // The owner's own message and the app's nudge are not answers, and counting them would clear
        // the wait without anybody having replied — the failure in the opposite direction.
        File.AppendAllText(_channelFile, Build_Entries(("owner", 2), ("app", 3)));
        Assert.Equal(atDelivery, ChannelHistory_Counter.Count_OwnerFacingEntries(_channelFile));

        File.AppendAllText(_channelFile, Build_Entries(("supervisor", 4)));
        Assert.Equal(atDelivery + 1, ChannelHistory_Counter.Count_OwnerFacingEntries(_channelFile));
    }

    /// <summary>
    /// A member's report is not an answer to the owner either. `imp-1` and `rev-1` do not write on
    /// the owner channel by design, so this is about the count refusing to be fooled if one does —
    /// the mirror already flags such an entry rather than hiding it.
    /// </summary>
    [Fact]
    public void AMembersEntryIsNotAnAnswerToTheOwner()
    {
        File.WriteAllText(_channelFile, Build_Entries(("solo", 1)));

        var atDelivery = ChannelHistory_Counter.Count_OwnerFacingEntries(_channelFile);

        File.AppendAllText(_channelFile, Build_Entries(("imp-1", 2), ("rev-1", 3)));

        Assert.Equal(atDelivery, ChannelHistory_Counter.Count_OwnerFacingEntries(_channelFile));
    }

    /// <summary>
    /// IT STILL SPANS THE ARCHIVE, which is the property this count already had and must not lose:
    /// compaction moves older entries out of the live file, so a live-file-only count FALLS and no
    /// later answer can exceed the figure recorded at delivery (the 2026-08-10 `option-lab-2`
    /// incident). Asserted here for the SOLO author, because the fix added a second author to a
    /// method whose archive-spanning is the reason it exists.
    /// </summary>
    [Fact]
    public void ItStillSpansTheArchive_ForASoloToo()
    {
        File.WriteAllText(_channelFile, Build_Entries(("solo", 1), ("solo", 2), ("solo", 3)));

        var beforeCompaction = ChannelHistory_Counter.Count_OwnerFacingEntries(_channelFile);
        Assert.Equal(3, beforeCompaction);

        // Exactly what the compactor does: the oldest entries MOVE to the sibling archive.
        File.WriteAllText(Channel_Compactor.Build_ArchiveFilePath(_channelFile), Build_Entries(("solo", 1), ("solo", 2)));
        File.WriteAllText(_channelFile, Build_Entries(("solo", 3)));

        Assert.Equal(beforeCompaction, ChannelHistory_Counter.Count_OwnerFacingEntries(_channelFile));

        File.AppendAllText(_channelFile, Build_Entries(("solo", 4)));

        Assert.Equal(beforeCompaction + 1, ChannelHistory_Counter.Count_OwnerFacingEntries(_channelFile));
    }

    /// <summary>
    /// A PROMOTED orchestration's channel carries both, and the count sees the whole conversation —
    /// the solo's history plus the supervisor that took the channel over. Nothing has to know which
    /// mode it is in, which is why the count sums the two rather than asking.
    /// </summary>
    [Fact]
    public void APromotedChannelCountsBothItsVoices()
    {
        File.WriteAllText(_channelFile, Build_Entries(("solo", 1), ("solo", 2), ("supervisor", 3)));

        Assert.Equal(3, ChannelHistory_Counter.Count_OwnerFacingEntries(_channelFile));
    }

    static string Build_Entries(params (string Author, int Index)[] entries)
    {
        var text = "";

        foreach (var entry in entries)
            text += $"## [{entry.Index}] FROM {entry.Author} — 2026-08-14 20:30 — subject\n\nbody\n\n";

        return text;
    }
}
