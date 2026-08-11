using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// The app is not a participant in a member's conversation — its entries are ABOUT that conversation,
/// not part of it — and counting it as the last speaker inverted the answer on the most common path.
///
/// A member files a report and waits. The app nudges the supervisor, which it does precisely BECAUSE
/// someone is waiting. The member then stopped reading as "awaiting review" and became "briefed, not
/// started yet": the status line mislabelled it, the idle-nudge logic stopped treating it as waiting
/// on someone else and nudged it again for waiting on the reply the app had just chased, and the
/// turn-end hook could not tell a verdict from a brief.
///
/// One place answers this now, and these are its cases.
/// </summary>
public class MemberStateAppEntryTests
{
    [Fact]
    public void AnAppNudgeAfterAMemberReport_LeavesTheMemberAwaitingReview()
    {
        Assert.Equal(
            MemberStates.AwaitingSupervisorReview,
            Resolve(
                "## [1] FROM implementer — x — imp-1 online",
                "## [2] FROM supervisor — x — brief",
                "## [3] FROM implementer — x — report filed",
                "## [4] FROM app — x — unread traffic, you have not answered"));
    }

    /// <summary>
    /// A member whose only entry is its boot line is waiting for WORK, not for a verdict — and
    /// publishing it as awaiting review let the awaiting-answer hook permit BRIEFING it while the
    /// owner was deciding, which is the one thing that exemption forbids.
    /// </summary>
    [Fact]
    public void AMemberThatHasOnlySaidOnline_IsNotAwaitingAVerdict()
    {
        Assert.Equal(MemberStates.ImplementerWorking, Resolve("## [1] FROM implementer — x — imp-1 online"));
    }

    /// <summary>Skipping the app must not resurrect a member that never spoke — a brief stays a brief.</summary>
    [Fact]
    public void AnAppEntryAfterABrief_LeavesTheMemberWorking()
    {
        Assert.Equal(
            MemberStates.ImplementerWorking,
            Resolve(
                "## [1] FROM supervisor — x — brief",
                "## [2] FROM app — x — GO AHEAD, resume"));
    }

    /// <summary>A blocked report keeps its state through a nudge too — it is still blocked.</summary>
    [Fact]
    public void AnAppNudgeDoesNotClearBlockedOnOwner()
    {
        Assert.Equal(
            MemberStates.BlockedOnOwner,
            Resolve(
                "## [1] FROM implementer — x — BLOCKED ON OWNER: which database?",
                "## [2] FROM app — x — unread traffic"));
    }

    /// <summary>Several app entries in a row are still all app entries.</summary>
    [Fact]
    public void RepeatedAppEntries_AreAllSkipped()
    {
        Assert.Equal(
            MemberStates.AwaitingSupervisorReview,
            Resolve(
                "## [1] FROM supervisor — x — brief",
                "## [2] FROM implementer — x — report filed",
                "## [3] FROM app — x — unread traffic",
                "## [4] FROM app — x — unread traffic again"));
    }

    /// <summary>A channel of nothing but app entries has no conversation in it yet.</summary>
    [Fact]
    public void OnlyAppEntries_ReadAsWorking_NotAwaiting()
    {
        Assert.Equal(MemberStates.ImplementerWorking, Resolve("## [1] FROM app — x — GO AHEAD, resume"));
    }

    static MemberStates Resolve(params string[] headers)
    {
        return MemberState_Resolver.Resolve(
            ChannelEntry_Parser.Parse_All(string.Join("\n\n", headers.Select(header => $"{header}\n\nbody\n"))));
    }
}
