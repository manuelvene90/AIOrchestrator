using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// A MEMBER'S STATE READ ACROSS THE COMPACTION BOUNDARY — the combination the app's call sites use,
/// pinned here because neither half is wrong on its own. <see cref="MemberState_Resolver"/> is a pure
/// function of the list it is handed, and it was handed the live file alone; the defect lives in the
/// JOIN, so the join is what these cases assert.
///
/// The state that makes the two readings differ is a live file with no conversation entry at all, which
/// needs 45 consecutive app entries and sounds unreachable. On 2026-08-14 three member channels on this
/// machine were in it — `ai-orchestrator-3/imp-1`, `da-vinci-fintech-suite-5/imp-6` and `imp-8` — all
/// three with a null `closedUtc`, so no closed-member guard screened them out.
///
/// EACH CASE ASSERTS BOTH READINGS, on purpose. Asserting only the fixed answer would leave a case that
/// could pass because the whole-history read works OR because the live-only read happened to agree —
/// two routes to one green, which is the failure this branch has been removing all week. Asserting the
/// difference pins which read produced it.
/// </summary>
public class MemberStateAcrossArchiveTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;

    public MemberStateAcrossArchiveTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-archive-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    /// <summary>
    /// THE WINDOW-SEMANTICS CHANGE, PINNED RATHER THAN LEFT TO A COMMIT MESSAGE. A member announced a
    /// writing window, the announcement has since been compacted into the archive, and it never closed
    /// one — so the files are still in flight and the supervisor must not audit them.
    ///
    /// Reading the live file alone, the opener is invisible and the member reads as ordinary work: the
    /// window silently expires by being archived, which is the opposite of what an unclosed window
    /// means. An unclosed window is precisely the stalled case the marker exists to catch, and the
    /// longer a channel runs — the very channels that compact — the more certain it was to be missed.
    /// </summary>
    [Fact]
    public void AnUnclosedWritingWindowSurvivesBeingCompactedIntoTheArchive()
    {
        Write_Live(("app", 178), ("app", 179));
        Write_Archive(
            ("supervisor", 1, "the brief"),
            ("implementer", 2, $"{MemberState_Resolver.WRITING_WINDOW_OPEN_MARKER} — editing the engine"));

        Assert.Equal(
            MemberStates.WritingWindowOpen,
            MemberState_Resolver.Resolve(ChannelHistory_Counter.Read_AllEntries(_channelFile)));

        Assert.NotEqual(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(Read_LiveOnly()));
    }

    /// <summary>
    /// A window whose OPEN is archived and whose CLOSE is still live is a COMPLETE pair, and must not
    /// read as open. This is the other side of the case above and it is why the helper concatenates
    /// archive-then-live rather than either alone: a scan that sees only one half of a pair draws the
    /// wrong conclusion from a channel that did everything right.
    /// </summary>
    [Fact]
    public void AWindowClosedAfterCompactionIsNotStillOpen()
    {
        Write_Live(("implementer", 91, $"{MemberState_Resolver.WRITING_WINDOW_CLOSED_MARKER} — landed abc1234"));
        Write_Archive(
            ("supervisor", 1, "the brief"),
            ("implementer", 2, $"{MemberState_Resolver.WRITING_WINDOW_OPEN_MARKER} — editing the engine"));

        Assert.NotEqual(
            MemberStates.WritingWindowOpen,
            MemberState_Resolver.Resolve(ChannelHistory_Counter.Read_AllEntries(_channelFile)));
    }

    /// <summary>
    /// THE MEASURED CASE. A member declared itself idle on purpose, the declaration was compacted out,
    /// and only the app's own writes are left live. Read live-only it becomes `ImplementerWorking` —
    /// the app reporting a member as busy on the strength of having written to it itself, which is the
    /// same shape as the nudge loop this branch exists to close.
    /// </summary>
    [Fact]
    public void ADeclarationStillStandsAfterItHasBeenCompactedOut()
    {
        Write_Live(("app", 178), ("app", 179));
        Write_Archive(
            ("supervisor", 1, "the brief"),
            ("implementer", 2, $"{MemberState_Resolver.STANDING_BY_MARKER} — nothing owed, nothing running"));

        Assert.Equal(
            MemberStates.StandingBy,
            MemberState_Resolver.Resolve(ChannelHistory_Counter.Read_AllEntries(_channelFile)));

        Assert.Equal(MemberStates.ImplementerWorking, MemberState_Resolver.Resolve(Read_LiveOnly()));
    }

    /// <summary>
    /// LIVE STILL WINS. The archive holds the older entries, so a member that declared itself idle and
    /// has since been briefed again must read as working — otherwise the fix becomes a mute switch,
    /// pinning every long-running member to an ancient state.
    /// </summary>
    [Fact]
    public void ALiveBriefBeatsAnArchivedDeclaration()
    {
        Write_Live(("supervisor", 90, "the NEW brief"));
        Write_Archive(
            ("supervisor", 1, "the brief"),
            ("implementer", 2, $"{MemberState_Resolver.STANDING_BY_MARKER} — nothing owed, nothing running"));

        Assert.Equal(
            MemberStates.ImplementerWorking,
            MemberState_Resolver.Resolve(ChannelHistory_Counter.Read_AllEntries(_channelFile)));
    }

    IReadOnlyList<IChannelEntry> Read_LiveOnly()
    {
        return ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));
    }

    void Write_Live(params (string Author, int Index)[] entries)
    {
        File.WriteAllText(_channelFile, Build_Text([.. entries.Select(e => (e.Author, e.Index, "subject"))]));
    }

    void Write_Live(params (string Author, int Index, string Subject)[] entries)
    {
        File.WriteAllText(_channelFile, Build_Text(entries));
    }

    void Write_Archive(params (string Author, int Index, string Subject)[] entries)
    {
        File.WriteAllText(Channel_Compactor.Build_ArchiveFilePath(_channelFile), Build_Text(entries));
    }

    static string Build_Text((string Author, int Index, string Subject)[] entries)
    {
        var text = "";

        foreach (var entry in entries)
            text += $"## [{entry.Index}] FROM {entry.Author} — 2026-08-14 11:00 — {entry.Subject}\n\nbody\n\n";

        return text;
    }
}
