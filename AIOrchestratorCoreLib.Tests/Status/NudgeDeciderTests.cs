using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// The two nudges were written apart and were EXHAUSTIVE without either author noticing: a channel
/// ends either in a member's entry or in someone else's, so one of the two always fired. Measured on
/// two live channels — "unread traffic — you have not answered" when the supervisor spoke last, and
/// "you stopped mid-task" when the member did — with no third configuration available, and a nudge
/// about a nudge among them.
///
/// So these tests are written against the RULE and not against either implementation: the property
/// that matters is that a declared-idle member is quiet on BOTH paths at once, which is the one
/// state the old pair could not represent.
/// </summary>
public class NudgeDeciderTests
{
    /// <summary>THE case. Both predicates, one channel, no nudge from either.</summary>
    [Fact]
    public void ADeclaredIdleMemberIsQuietOnBothPaths()
    {
        // TITLED, because a real declaration puts the marker in its SUBJECT — that is what separates
        // it from a report that merely closes by going quiet, and the derived subject the plain
        // helper produces cannot express the difference.
        var entries = BuildTitled(
            (ChannelAuthors.Supervisor, "hold", "nothing to build, nothing to commit"),
            (ChannelAuthors.Implementer, "STANDING BY — waiting on rev-2's review", "Nothing owed."));

        Assert.False(Nudge_Decider.Is_DormantMidWork(entries, true));
        Assert.False(Nudge_Decider.Has_UnansweredInboundTraffic(entries));
        Assert.False(Nudge_Decider.Owes_MemberAVerdict(entries));
    }

    /// <summary>
    /// The proof that the pair was exhaustive: without the declaration, that same idle member is
    /// nudged — and the supervisor is nudged about the entry that asked it for nothing.
    /// </summary>
    [Fact]
    public void WithoutTheDeclaration_TheSameIdleMemberIsNudgedAndSoIsTheSupervisor()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "hold — nothing to build, nothing to commit"),
            (ChannelAuthors.Implementer, "holding, nothing in flight"));

        Assert.True(Nudge_Decider.Is_DormantMidWork(entries, true) || Nudge_Decider.Owes_MemberAVerdict(entries));
    }

    /// <summary>Load-bearing and NOT weakened: work announced, then silence, is still woken.</summary>
    [Fact]
    public void AMemberStalledWithAnOpenWritingWindowIsStillNudged()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "implement the parser"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Parser.cs, Model.cs"));

        Assert.True(Nudge_Decider.Is_DormantMidWork(entries, true));
    }

    /// <summary>
    /// A declaration cannot be used to go quiet mid-task: the window is still open, so the marker
    /// does not silence anything. Otherwise the fix would be a switch for turning off the one nudge
    /// that matters.
    /// </summary>
    [Fact]
    public void StandingByDoesNotSilenceAMemberThatStillHasAnOpenWindow()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "implement the parser"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Parser.cs"),
            (ChannelAuthors.Implementer, "STANDING BY"));

        Assert.True(Nudge_Decider.Is_DormantMidWork(entries, true));
    }

    /// <summary>
    /// New inbound traffic clears the declaration — both nudges are live again.
    ///
    /// The two False assertions are not padding: without them, removing the "last author is not a
    /// member" guard from EITHER predicate left all 484 tests green. A supervisor-last channel would
    /// have counted as the member being dormant mid-work AND as the supervisor owing itself a
    /// verdict for its own entry.
    /// </summary>
    [Fact]
    public void InboundTrafficAfterADeclarationMakesTheNudgesLiveAgain()
    {
        var entries = Build(
            (ChannelAuthors.Implementer, "STANDING BY — waiting for a brief"),
            (ChannelAuthors.Supervisor, "new task: fix the ledger denominator"));

        Assert.True(Nudge_Decider.Has_UnansweredInboundTraffic(entries));
        Assert.False(Nudge_Decider.Is_DormantMidWork(entries, true));
        Assert.False(Nudge_Decider.Owes_MemberAVerdict(entries));
    }

    /// <summary>
    /// THE CASE THAT FAILED TODAY, five times. A filed report that ENDS with a standing-by
    /// declaration — the exact shape the role commands tell members to write — still owes a verdict.
    ///
    /// The old rule returned false for anything resolving to StandingBy, so the reminder was silenced
    /// for a member idle BECAUSE it was waiting on the supervisor. A spurious nudge costs a wake; a
    /// missing one costs filed work sitting unread with nothing anywhere saying so.
    ///
    /// The SUBJECT is what separates the two, which is why these build entries with explicit
    /// subjects: a report is titled by its result and closes by going quiet, a declaration is
    /// titled by the marker itself.
    /// </summary>
    [Fact]
    public void AReportThatEndsWithADeclarationStillOwesAVerdict()
    {
        var entries = BuildTitled(
            (ChannelAuthors.Supervisor, "brief", "fix the ledger denominator"),
            (ChannelAuthors.Implementer, "call pinned: d899c7e, 635 tests", "Evidence.\n\nSTANDING BY for your verdict."));

        Assert.True(Nudge_Decider.Owes_MemberAVerdict(entries));
        Assert.False(Nudge_Decider.Is_DormantMidWork(entries, true));
    }

    /// <summary>
    /// And the state the feature exists for is UNWEAKENED: a declaration with no filed work behind
    /// it owes nobody anything. Asserted separately so neither case can pass for the other's reason.
    /// </summary>
    [Fact]
    public void ADeclarationWithNothingFiledBehindItOwesNoVerdict()
    {
        var entries = BuildTitled(
            (ChannelAuthors.Supervisor, "hold", "nothing to build, nothing to commit"),
            (ChannelAuthors.Implementer, "STANDING BY — waiting on rev-4", "Nothing owed, nothing running."));

        Assert.False(Nudge_Decider.Owes_MemberAVerdict(entries));
        Assert.False(Nudge_Decider.Is_DormantMidWork(entries, true));
    }

    /// <summary>A filed report still owes the supervisor a verdict — that nudge is not weakened.</summary>
    [Fact]
    public void AFiledReportStillPutsTheSupervisorOnTheHook()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "implement it"),
            (ChannelAuthors.Implementer, "done, commit 8b58b2e, 473 tests pass"));

        Assert.True(Nudge_Decider.Owes_MemberAVerdict(entries));
        Assert.False(Nudge_Decider.Is_DormantMidWork(entries, true));
    }

    /// <summary>BLOCKED ON OWNER is quiet for the other legitimate reason, and stays quiet.</summary>
    [Fact]
    public void BlockedOnOwnerIsNotDormant()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "implement it"),
            (ChannelAuthors.Implementer, "BLOCKED ON OWNER — which schema do they want?"));

        Assert.False(Nudge_Decider.Is_DormantMidWork(entries, true));
    }

    /// <summary>
    /// A freshly spawned member that has never been briefed is waiting for work, not dormant in it.
    /// Nudging it respawned every orchestration's pre-spawned pair on a loop and cost them their
    /// context.
    ///
    /// The channel here has an OPEN WINDOW on purpose. The obvious version of this test — a lone
    /// "imp-1 online" entry — passes whether the never-briefed guard exists or not, because a plain
    /// member-last entry is already legitimately quiet: it proves the fallback, not the guard. This
    /// shape is the only one where the guard is the thing deciding.
    /// </summary>
    [Fact]
    public void ANeverBriefedMemberIsWaitingForWork_NotDormant()
    {
        var entries = Build(
            (ChannelAuthors.Implementer, "imp-1 online"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — scratch.cs"));

        Assert.False(Nudge_Decider.Is_DormantMidWork(entries, hasBeenBriefed: false));
    }

    /// <summary>Reviewers are members too — they were invisible to this detector once already.</summary>
    [Fact]
    public void AReviewerDeclarationCountsTheSameAsAnImplementers()
    {
        var entries = BuildTitled(
            (ChannelAuthors.Supervisor, "review 40dacff", "depth standard"),
            (ChannelAuthors.Reviewer, "STANDING BY — review filed, nothing else queued", "Nothing else queued."));

        Assert.False(Nudge_Decider.Is_DormantMidWork(entries, true));
        Assert.False(Nudge_Decider.Owes_MemberAVerdict(entries));
    }

    /// <summary>
    /// The app's own nudge still counts as inbound. Deliberate: orphan-recovery is the only proof a
    /// monitor is dead, and it can only run on a member that has already been nudged. Pinned so that
    /// a future "stop nudging about nudges" change has to face the escalation path it would break.
    /// </summary>
    [Fact]
    public void AnAppEntryStillCountsAsInbound_BecauseEscalationDependsOnIt()
    {
        var entries = Build(
            (ChannelAuthors.Implementer, "report filed"),
            (ChannelAuthors.App, "unread traffic — you have not answered"));

        Assert.True(Nudge_Decider.Has_UnansweredInboundTraffic(entries));
    }

    [Fact]
    public void AnEmptyChannelNudgesNobody()
    {
        Assert.False(Nudge_Decider.Is_DormantMidWork([], true));
        Assert.False(Nudge_Decider.Has_UnansweredInboundTraffic([]));
        Assert.False(Nudge_Decider.Owes_MemberAVerdict([]));
    }

    /// <summary>
    /// CLAUDE.md item 13, applied to "has this member ever been briefed". Compaction moves older
    /// entries into a sibling archive, so a live-file scan is not monotonic: a long-running member
    /// whose briefs have all been archived reverts to looking freshly spawned, and the load-bearing
    /// stalled-mid-task nudge switches off for exactly the members that have been running longest.
    ///
    /// Real files, because the defect is entirely about which file is read.
    /// </summary>
    [Fact]
    public void BeingBriefedIsRememberedAfterCompactionMovesTheBriefsToTheArchive()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"nudge-briefed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            var channelFile = Path.Combine(folder, "channel.md");

            File.WriteAllText(channelFile, "## [9] FROM implementer — 2026-08-12 03:00 — WRITING WINDOW OPEN\nbatch 2\n");
            Assert.False(Nudge_Decider.Has_BeenBriefed(channelFile));

            File.WriteAllText(
                Channel_Compactor.Build_ArchiveFilePath(channelFile),
                "## [1] FROM supervisor — 2026-08-11 22:00 — the brief\ndo the work\n");

            Assert.True(Nudge_Decider.Has_BeenBriefed(channelFile));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void AChannelThatDoesNotExistHasNoBriefs()
    {
        Assert.False(Nudge_Decider.Has_BeenBriefed(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "channel.md")));
    }

    /// <summary>
    /// The SUBJECT is deliberately not the body. An earlier version passed one string as both, so a
    /// marker matcher reading Subject instead of RawText — or Body instead of either — would have
    /// been invisible here: every field said the same thing, so every implementation agreed. That
    /// matters most while the matcher is the thing being changed.
    ///
    /// The body is also placed on its OWN LINE below the header, which is where a real entry puts it.
    /// A declaration is anchored to the start of a line, so a fixture that flattens the entry into
    /// one string cannot tell a declaration from a mention of one.
    /// </summary>
    /// <summary>
    /// Entries with an EXPLICIT subject. The plain Build derives one, which cannot express the
    /// difference between a report and a declaration — and that difference is the subject.
    /// </summary>
    /// <summary>
    /// AN OPEN WINDOW IS NOT FILED WORK. "I am about to write code" publishes the supervisor as owing
    /// that member a verdict on nothing at all.
    ///
    /// The fixture opens with a supervisor brief on purpose: without one, Is_AwaitingVerdict returns
    /// false at its first guard — nothing has been asked of this member — and the case would pass for
    /// a reason that has nothing to do with windows. That is the two-routes shape three sessions hit
    /// on the previous feature, so it is designed out here rather than found later.
    ///
    /// BOTH FAMILIES, because a mutation window is the same marker with a different word in it.
    /// </summary>
    [Theory]
    [InlineData("WRITING WINDOW OPEN — Parser.cs, Model.cs")]
    [InlineData("MUTATION WINDOW OPEN — the three gates")]
    public void AnAnnouncedWindowOwesTheSupervisorNothing(string subject)
    {
        var entries = BuildTitled(
            (ChannelAuthors.Supervisor, "brief", "implement the parser"),
            (ChannelAuthors.Implementer, subject, "starting now"));

        Assert.False(Nudge_Decider.Owes_MemberAVerdict(entries));
    }

    /// <summary>
    /// THE CONTRADICTION THAT PROVED IT, and the reason no wake is lost by the exclusion.
    ///
    /// A quiet window-open member used to be published as BOTH "the supervisor owes it a verdict" AND
    /// "it stopped mid-task" at the same instant. Those cannot both be true of one session. The
    /// dormancy nudge is the correct one — it is aimed at the MEMBER, which is who has the work — and
    /// it is unweakened here.
    ///
    /// Asserted together in one case deliberately: apart, a later change could silence the dormancy
    /// nudge for window-open members and both halves would still pass separately.
    /// </summary>
    [Fact]
    public void AWindowOpenMemberIsNudgedItselfAndNeverOnTheSupervisorsBehalf()
    {
        var entries = BuildTitled(
            (ChannelAuthors.Supervisor, "brief", "implement the parser"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Parser.cs", "starting now"));

        Assert.True(Nudge_Decider.Is_DormantMidWork(entries, true));
        Assert.False(Nudge_Decider.Owes_MemberAVerdict(entries));
    }

    /// <summary>
    /// AND A CLOSE IS THE OPPOSITE CASE, which is why only openers are excluded. The member is saying
    /// the files are settled and the work is filed — a verdict IS owed, and both consumers already
    /// agreed on that before this fix.
    ///
    /// It is the case an over-broad exclusion would silently take with it: skipping every window
    /// marker would leave a finished batch waiting with nothing anywhere saying so, which is the
    /// defect this whole area exists to prevent.
    /// </summary>
    [Fact]
    public void AClosedWindowStillOwesAVerdict()
    {
        var entries = BuildTitled(
            (ChannelAuthors.Supervisor, "brief", "implement the parser"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Parser.cs", "starting now"),
            (ChannelAuthors.Implementer, "WRITING WINDOW CLOSED — Parser.cs, 12 tests", "done, commit 8b58b2e"));

        Assert.True(Nudge_Decider.Owes_MemberAVerdict(entries));
    }

    /// <summary>
    /// THE PROPERTY THAT MAKES THE LOOP IMPOSSIBLE: the app writing cannot change what the member is
    /// being nudged about, so nothing the app says can ever qualify a member for another nudge.
    ///
    /// The old repetition needed no one — the nudge woke the member, the waking proved it alive, that
    /// proof cleared the escalation map, and the quiet clock its own write had restarted elapsed.
    /// Every 8 minutes, aimed exclusively at members behaving correctly.
    /// </summary>
    [Fact]
    public void AnAppEntryDoesNotChangeWhatTheMemberIsNudgedAbout()
    {
        var beforeTheNudge = BuildTitled(
            (ChannelAuthors.Supervisor, "brief", "implement the parser"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Parser.cs", "starting now"));

        var afterTheNudge = BuildTitled(
            (ChannelAuthors.Supervisor, "brief", "implement the parser"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Parser.cs", "starting now"),
            (ChannelAuthors.App, "you stopped mid-task", "nothing was going to wake you"));

        Assert.Equal(
            Nudge_Decider.Identify_LastConversationEntry_OrNull(beforeTheNudge),
            Nudge_Decider.Identify_LastConversationEntry_OrNull(afterTheNudge));
    }

    /// <summary>
    /// And it is not a mute switch: a genuinely new member entry is a new unanswered thing. Asserted
    /// apart, because "always equal" would satisfy the case above and silence the alarm for good —
    /// the silent failure this whole area keeps producing.
    /// </summary>
    [Fact]
    public void ANewMemberEntryIsADifferentThingToBeNudgedAbout()
    {
        var before = BuildTitled(
            (ChannelAuthors.Supervisor, "brief", "implement the parser"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Parser.cs", "starting now"));

        var after = BuildTitled(
            (ChannelAuthors.Supervisor, "brief", "implement the parser"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Parser.cs", "starting now"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Model.cs", "still going"));

        Assert.NotEqual(
            Nudge_Decider.Identify_LastConversationEntry_OrNull(before),
            Nudge_Decider.Identify_LastConversationEntry_OrNull(after));
    }

    /// <summary>
    /// THE IDENTITY IS THE RAW TEXT, NOT THE INDEX AND NOT THE STAMP — the condition the ruling turned
    /// on, pinned rather than promised.
    ///
    /// Both of those are agent-written and neither is unique: one channel carried two `[80]`s and two
    /// `[81]`s, and a header stamp has MINUTE resolution, so two entries a few seconds apart share
    /// one. Under either, a genuinely new entry would compare equal to the remembered one and lose the
    /// nudge it earned — silently, which is the defect this gate replaces wearing the other mask.
    ///
    /// This fixture is the trap: same index, same stamp, different content.
    /// </summary>
    [Fact]
    public void TwoEntriesSharingAnIndexAndAMinuteAreStillDifferentThings()
    {
        var first = Stamped((ChannelAuthors.Implementer, "first thing", "2026-08-12 19:30"));
        var second = Stamped((ChannelAuthors.Implementer, "a completely different thing", "2026-08-12 19:30"));

        Assert.NotEqual(
            Nudge_Decider.Identify_LastConversationEntry_OrNull(first),
            Nudge_Decider.Identify_LastConversationEntry_OrNull(second));
    }

    static IReadOnlyList<IChannelEntry> Stamped(params (ChannelAuthors Author, string Subject, string DateText)[] entries)
    {
        List<IChannelEntry> built = [];

        foreach (var (author, subject, dateText) in entries)
        {
            // INDEX 7 for every entry, deliberately: duplicate indices are real and this fixture must
            // not be able to tell the entries apart by one.
            built.Add(ChannelEntry_Factory.Create(
                7, author, dateText, subject, "body",
                $"## [7] FROM {author} — {dateText} — {subject}\nbody"));
        }

        return built;
    }

    static IReadOnlyList<IChannelEntry> BuildTitled(params (ChannelAuthors Author, string Subject, string Body)[] entries)
    {
        List<IChannelEntry> built = [];

        for (var index = 0; index < entries.Length; index++)
        {
            var (author, subject, body) = entries[index];

            built.Add(ChannelEntry_Factory.Create(
                index + 1, author, "2026-08-12", subject, body,
                $"## [{index + 1}] FROM {author} — 2026-08-12 15:00 — {subject}\n{body}"));
        }

        return built;
    }

    static IReadOnlyList<IChannelEntry> Build(params (ChannelAuthors Author, string Body)[] entries)
    {
        List<IChannelEntry> built = [];

        for (var index = 0; index < entries.Length; index++)
        {
            var author = entries[index].Author;
            var body = entries[index].Body;
            var subject = $"entry {index + 1} from {author}";

            built.Add(ChannelEntry_Factory.Create(
                index + 1,
                author,
                "2026-08-12",
                subject,
                body,
                $"## [{index + 1}] FROM {author} — 2026-08-12 02:00 — {subject}\n{body}"));
        }

        return built;
    }
}
