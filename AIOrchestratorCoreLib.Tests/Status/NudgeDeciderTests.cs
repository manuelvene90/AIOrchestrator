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
        var entries = Build(
            (ChannelAuthors.Supervisor, "hold — nothing to build, nothing to commit"),
            (ChannelAuthors.Implementer, "STANDING BY — waiting on rev-2's review"));

        Assert.False(Nudge_Decider.Is_DormantMidWork(entries));
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

        Assert.True(Nudge_Decider.Is_DormantMidWork(entries) || Nudge_Decider.Owes_MemberAVerdict(entries));
    }

    /// <summary>Load-bearing and NOT weakened: work announced, then silence, is still woken.</summary>
    [Fact]
    public void AMemberStalledWithAnOpenWritingWindowIsStillNudged()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "implement the parser"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Parser.cs, Model.cs"));

        Assert.True(Nudge_Decider.Is_DormantMidWork(entries));
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

        Assert.True(Nudge_Decider.Is_DormantMidWork(entries));
    }

    /// <summary>New inbound traffic clears the declaration — both nudges are live again.</summary>
    [Fact]
    public void InboundTrafficAfterADeclarationMakesTheNudgesLiveAgain()
    {
        var entries = Build(
            (ChannelAuthors.Implementer, "STANDING BY — waiting for a brief"),
            (ChannelAuthors.Supervisor, "new task: fix the ledger denominator"));

        Assert.True(Nudge_Decider.Has_UnansweredInboundTraffic(entries));
    }

    /// <summary>A filed report still owes the supervisor a verdict — that nudge is not weakened.</summary>
    [Fact]
    public void AFiledReportStillPutsTheSupervisorOnTheHook()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "implement it"),
            (ChannelAuthors.Implementer, "done, commit 8b58b2e, 473 tests pass"));

        Assert.True(Nudge_Decider.Owes_MemberAVerdict(entries));
        Assert.False(Nudge_Decider.Is_DormantMidWork(entries));
    }

    /// <summary>BLOCKED ON OWNER is quiet for the other legitimate reason, and stays quiet.</summary>
    [Fact]
    public void BlockedOnOwnerIsNotDormant()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "implement it"),
            (ChannelAuthors.Implementer, "BLOCKED ON OWNER — which schema do they want?"));

        Assert.False(Nudge_Decider.Is_DormantMidWork(entries));
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
        Assert.False(Nudge_Decider.Is_DormantMidWork(Build(
            (ChannelAuthors.Implementer, "imp-1 online"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — scratch.cs"))));
    }

    /// <summary>Reviewers are members too — they were invisible to this detector once already.</summary>
    [Fact]
    public void AReviewerDeclarationCountsTheSameAsAnImplementers()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "review 40dacff"),
            (ChannelAuthors.Reviewer, "STANDING BY — review filed, nothing else queued"));

        Assert.False(Nudge_Decider.Is_DormantMidWork(entries));
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
        Assert.False(Nudge_Decider.Is_DormantMidWork([]));
        Assert.False(Nudge_Decider.Has_UnansweredInboundTraffic([]));
        Assert.False(Nudge_Decider.Owes_MemberAVerdict([]));
    }

    static IReadOnlyList<IChannelEntry> Build(params (ChannelAuthors Author, string Body)[] entries)
    {
        List<IChannelEntry> built = [];

        for (var index = 0; index < entries.Length; index++)
        {
            var author = entries[index].Author;
            var body = entries[index].Body;

            built.Add(ChannelEntry_Factory.Create(
                index + 1,
                author,
                "2026-08-12",
                body,
                body,
                $"## [{index + 1}] FROM {author} — 2026-08-12 02:00 — {body}\n{body}"));
        }

        return built;
    }
}
