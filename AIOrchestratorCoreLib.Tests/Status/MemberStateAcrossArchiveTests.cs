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
    /// AN UNCLOSED WINDOW EXPIRES BY BEING COMPACTED, and this case previously asserted the OPPOSITE.
    ///
    /// I argued that an archived unclosed opener should stay open because "an unclosed window is
    /// precisely the stalled case the marker exists to catch", and that argument was accepted. IT WAS
    /// WRONG, and rev-6 found why: `Has_OpenWindow` short-circuits ahead of every other rule, so an
    /// opener with no matching close pins the member in this state — and NOTHING PRUNES AN ARCHIVE, so
    /// no later traffic of any kind can ever clear it. That is not a stricter reading, it is an
    /// unclearable one, and it replaced a wrong state that healed with a wrong state that cannot.
    ///
    /// It was not hypothetical for a single day: `da-vinci-fintech-suite-5/imp-6` and `imp-8` were both
    /// pinned by it, out of the three channels the original commit named as its evidence.
    ///
    /// A window is a statement about the PRESENT. An opener that has been moved out sits behind at
    /// least `KEEP_RECENT_ENTRIES` later entries, and a member that has written 45 entries since is not
    /// mid-write — it forgot the close. Compaction IS the expiry.
    /// </summary>
    [Fact]
    public void AnUnclosedWindowExpiresOnceItHasBeenCompactedOut()
    {
        Write_Live(("app", 178), ("app", 179));
        Write_Archive(
            ("supervisor", 1, "the brief"),
            ("implementer", 2, $"{MemberState_Resolver.WRITING_WINDOW_OPEN_MARKER} — editing the engine"));

        Assert.NotEqual(
            MemberStates.WritingWindowOpen,
            MemberState_Resolver.Resolve(ChannelHistory_Counter.Read_AllEntries(_channelFile)));
    }

    /// <summary>
    /// AND THE LIVE WINDOW STILL WORKS — asserted apart, because the fix above is a narrowing and a
    /// narrowing that went one step too far would silence the load-bearing case: a member that really
    /// is mid-write must still read as such, or the supervisor audits files in flight.
    /// </summary>
    [Fact]
    public void AWindowOpenedInTheLiveFileIsStillOpen()
    {
        Write_Live(("implementer", 91, $"{MemberState_Resolver.WRITING_WINDOW_OPEN_MARKER} — editing the engine"));
        Write_Archive(("supervisor", 1, "the brief"));

        Assert.Equal(
            MemberStates.WritingWindowOpen,
            MemberState_Resolver.Resolve(ChannelHistory_Counter.Read_AllEntries(_channelFile)));
    }

    /// <summary>
    /// THE REAL-WORLD MISS THAT MADE imp-6 LATCH, pinned as CURRENT BEHAVIOUR rather than fixed.
    ///
    /// That member closed its window with the subject `WINDOW CLOSED` — no `WRITING` prefix — so the
    /// marker never matched and its earlier opener stood.
    ///
    /// RELAXING THE MATCHER TO ACCEPT A BARE `WINDOW CLOSED` WOULD BE WRONG, which is why this asserts
    /// the miss instead of removing it: `MUTATION WINDOW CLOSED` CONTAINS that substring, so a mutation
    /// close would silently close a writing window. That is the superset-by-substring defect this
    /// orchestration has met repeatedly — most recently in a count of `catch` clauses that matched the
    /// filtered form because the bare string is a prefix of it.
    ///
    /// Members writing the close wrong is a protocol problem and belongs in the role commands. With the
    /// expiry above it now costs one stale live entry rather than a permanent state.
    /// </summary>
    [Fact]
    public void ACloseWrittenWithoutTheWritingPrefixDoesNotMatchTheMarker()
    {
        Write_Live(
            ("implementer", 26, $"{MemberState_Resolver.WRITING_WINDOW_OPEN_MARKER} — both verified as mine"),
            ("implementer", 27, "WINDOW CLOSED. Both fixed — seven commits."));

        Assert.Equal(
            MemberStates.WritingWindowOpen,
            MemberState_Resolver.Resolve(ChannelHistory_Counter.Read_AllEntries(_channelFile)));

        // And the properly-spelled close DOES clear it — so the case above pins the SPELLING and not
        // some unrelated reason the state happened to stick.
        Write_Live(
            ("implementer", 26, $"{MemberState_Resolver.WRITING_WINDOW_OPEN_MARKER} — both verified as mine"),
            ("implementer", 27, $"{MemberState_Resolver.WRITING_WINDOW_CLOSED_MARKER}. Both fixed — seven commits."));

        Assert.NotEqual(
            MemberStates.WritingWindowOpen,
            MemberState_Resolver.Resolve(ChannelHistory_Counter.Read_AllEntries(_channelFile)));
    }

    /// <summary>
    /// A window whose OPEN is archived and whose CLOSE is still live must not read as open.
    ///
    /// ITS REASON CHANGED AND THE DOCSTRING IS BEING CORRECTED RATHER THAN LEFT. It used to say this
    /// case is why the helper concatenates archive-then-live — that a scan seeing one half of a pair
    /// draws the wrong conclusion. That was true while windows were read from the whole history. They
    /// are not any more: the archived opener is never scanned, so this now passes because NO open is
    /// found rather than because the pair resolves.
    ///
    /// It is kept as a regression guard on the outcome, which is the thing a consumer depends on. But
    /// it no longer discriminates the concatenation order — `Read_AllEntries_PutsTheArchiveBeforeThe
    /// LiveFile` pins that on its own — and a reader should not take it as evidence for one. A test
    /// whose stated reason has quietly stopped matching why it passes is the defect this branch has
    /// spent two days removing; leaving this docstring alone would have added one more.
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
