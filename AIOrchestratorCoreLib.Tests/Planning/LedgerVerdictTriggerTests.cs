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
    [Fact]
    public void AnswerToAMemberReport_IsAVerdict()
    {
        Assert.True(LedgerHealth_Tracker.Is_VerdictOnMemberWork(Parse(
            "## [1] FROM supervisor — 2026-08-11 10:00 — brief",
            "## [2] FROM implementer — 2026-08-11 11:00 — done, 5 commits",
            "## [3] FROM supervisor — 2026-08-11 11:05 — accepted")));
    }

    /// <summary>The regression itself: briefing owes the ledger nothing, because nothing has happened.</summary>
    [Fact]
    public void ABriefIntoAFreshChannel_IsNotAVerdict()
    {
        Assert.False(LedgerHealth_Tracker.Is_VerdictOnMemberWork(Parse(
            "## [1] FROM supervisor — 2026-08-11 10:00 — BRIEF: investigate the limits path")));
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
            "## [1] FROM implementer — 2026-08-11 11:00 — report filed",
            "## [2] FROM app — 2026-08-11 11:08 — unread traffic — you have not answered",
            "## [3] FROM supervisor — 2026-08-11 11:10 — accepted")));
    }

    /// <summary>A reviewer's report earns a verdict exactly as an implementer's does.</summary>
    [Fact]
    public void AnAnswerToAReviewer_IsAVerdict()
    {
        Assert.True(LedgerHealth_Tracker.Is_VerdictOnMemberWork(Parse(
            "## [1] FROM reviewer — 2026-08-11 11:00 — 7 findings",
            "## [2] FROM supervisor — 2026-08-11 11:05 — accepted, queued")));
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
