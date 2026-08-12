using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Planning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// What puts the task ledger in debt. It has to be a VERDICT — the supervisor answering work a
/// member filed — and not a BRIEF, which assigns work that has not happened yet.
///
/// The distinction was missing: any supervisor entry in any spoke armed a 90-second countdown to a
/// nudge, so BRIEFING someone got the supervisor told off for not having recorded their work. Five
/// false nudges on 2026-08-11, two of them inside two minutes, each threatening a turn-end block on
/// a ledger that was already accurate.
/// </summary>
public class LedgerVerdictTriggerTests
{
    /// <summary>
    /// THE fixture that matters, taken from a real channel rather than a minimal one: every member's
    /// FIRST act is an "&lt;id&gt; online" entry (implementer.md, reviewer.md), so a brief always follows
    /// a member entry. A predicate that asked "did a member speak last" armed the ledger here — on a
    /// brief — which is the regression this whole trigger exists to kill.
    ///
    /// The earlier version of this test used a channel containing ONLY the brief. It passed, and the
    /// rule was broken in every orchestration that has ever run: a green test on the one shape the
    /// boot protocol makes atypical.
    /// </summary>
    [Fact]
    public void ABriefFollowingTheMembersOnlineEntry_IsNotAVerdict()
    {
        Assert.False(LedgerHealth_Tracker.Is_VerdictOnMemberWork(Parse(
            "## [1] FROM implementer — 2026-08-11 21:22 — imp-1 online",
            "## [2] FROM supervisor — 2026-08-11 21:27 — BRIEF: investigate the limits path")));
    }

    [Fact]
    public void AnswerToAMemberReport_IsAVerdict()
    {
        Assert.True(LedgerHealth_Tracker.Is_VerdictOnMemberWork(Parse(
            "## [1] FROM implementer — 2026-08-11 21:22 — imp-1 online",
            "## [2] FROM supervisor — 2026-08-11 21:27 — BRIEF",
            "## [3] FROM implementer — 2026-08-11 23:36 — done, 5 commits",
            "## [4] FROM supervisor — 2026-08-11 23:40 — accepted")));
    }

    /// <summary>A brief into a channel with no traffic at all is still not a verdict.</summary>
    [Fact]
    public void ABriefIntoAFreshChannel_IsNotAVerdict()
    {
        Assert.False(LedgerHealth_Tracker.Is_VerdictOnMemberWork(Parse(
            "## [1] FROM supervisor — 2026-08-11 10:00 — BRIEF: investigate the limits path")));
    }

    /// <summary>
    /// A re-brief after a completed cycle is still a brief. The member spoke last two entries ago,
    /// but the conversation has already passed back to it.
    /// </summary>
    [Fact]
    public void ASecondBriefAfterAVerdict_IsNotAVerdict()
    {
        Assert.False(LedgerHealth_Tracker.Is_VerdictOnMemberWork(Parse(
            "## [1] FROM implementer — 2026-08-11 21:22 — imp-1 online",
            "## [2] FROM supervisor — 2026-08-11 21:27 — BRIEF",
            "## [3] FROM implementer — 2026-08-11 23:36 — done",
            "## [4] FROM supervisor — 2026-08-11 23:40 — accepted",
            "## [5] FROM supervisor — 2026-08-11 23:45 — NEXT UNIT: the close guard")));
    }

    /// <summary>A supervisor adding to its own brief is still briefing.</summary>
    [Fact]
    public void ASecondSupervisorEntryInARow_IsNotAVerdict()
    {
        Assert.False(LedgerHealth_Tracker.Is_VerdictOnMemberWork(Parse(
            "## [1] FROM implementer — 2026-08-11 09:00 — imp-1 online",
            "## [2] FROM supervisor — 2026-08-11 10:00 — BRIEF",
            "## [3] FROM supervisor — 2026-08-11 10:02 — ADDENDUM to the brief")));
    }

    /// <summary>
    /// An app nudge landing between the report and the verdict does not stop the verdict being one —
    /// and app entries arrive in exactly that gap, since that is when a member is waiting.
    /// </summary>
    [Fact]
    public void AnAppEntryBetweenTheReportAndTheVerdict_IsSkipped()
    {
        Assert.True(LedgerHealth_Tracker.Is_VerdictOnMemberWork(Parse(
            "## [1] FROM supervisor — 2026-08-11 10:00 — BRIEF",
            "## [2] FROM implementer — 2026-08-11 11:00 — report filed",
            "## [3] FROM app — 2026-08-11 11:08 — unread traffic — you have not answered",
            "## [4] FROM supervisor — 2026-08-11 11:10 — accepted")));
    }

    /// <summary>A reviewer's report earns a verdict exactly as an implementer's does.</summary>
    [Fact]
    public void AnAnswerToAReviewer_IsAVerdict()
    {
        Assert.True(LedgerHealth_Tracker.Is_VerdictOnMemberWork(Parse(
            "## [1] FROM reviewer — 2026-08-11 10:00 — rev-1 online",
            "## [2] FROM supervisor — 2026-08-11 10:05 — BRIEF: review d089f50",
            "## [3] FROM reviewer — 2026-08-11 11:00 — 7 findings",
            "## [4] FROM supervisor — 2026-08-11 11:05 — accepted, queued")));
    }

    /// <summary>Nobody else's entry arms it — the ledger is the SUPERVISOR's obligation.</summary>
    [Theory]
    [InlineData("## [1] FROM supervisor — 2026-08-11 10:00 — brief\n\n## [2] FROM implementer — 2026-08-11 11:00 — done")]
    [InlineData("## [1] FROM implementer — 2026-08-11 11:00 — done\n\n## [2] FROM app — 2026-08-11 11:08 — nudge")]
    public void AnEntryThatIsNotTheSupervisorsLast_IsNeverAVerdict(string channelText)
    {
        Assert.False(LedgerHealth_Tracker.Is_VerdictOnMemberWork(ChannelEntry_Parser.Parse_All(channelText)));
    }

    [Fact]
    public void AnEmptyChannel_IsNotAVerdict()
    {
        Assert.False(LedgerHealth_Tracker.Is_VerdictOnMemberWork(ChannelEntry_Parser.Parse_All("")));
    }

    static IReadOnlyList<AIOrchestratorCoreLib.Channels.ChannelEntry.IChannelEntry> Parse(params string[] headers)
    {
        return ChannelEntry_Parser.Parse_All(string.Join("\n\n", headers.Select(header => $"{header}\n\nbody\n")));
    }
}
