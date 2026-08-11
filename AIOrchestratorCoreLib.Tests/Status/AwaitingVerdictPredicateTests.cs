using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// The predicate, stated once and tested as a RULE rather than as the reported case:
///
///   A member is awaiting a verdict when, SINCE THE SUPERVISOR LAST SPOKE in that channel, it has
///   filed at least one entry that is not merely its boot announcement.
///
/// It is anchored to a POSITION. Two earlier versions were not, and each was correct about the case
/// that was reported and wrong about the general one. "Did a member speak last" broke on the boot
/// protocol, which makes a member speak FIRST. "Has a supervisor entry ever existed" is permanently
/// true from entry [2], so it silently became the rule it replaced — correct only for a member's
/// very first boot, which was exactly the shape the test then encoded.
///
/// Our lifecycle makes the second failure routine, not exotic: resume is a fresh role-command
/// re-entry for every role, so after any restart a settled channel reads `verdict → imp-1 online`.
/// </summary>
public class AwaitingVerdictPredicateTests
{
    [Fact]
    public void BriefIntoAVirginChannel_IsNotAwaiting()
    {
        Assert.False(Awaiting("## [1] FROM supervisor — x — BRIEF"));
    }

    /// <summary>The one that returned after every restart: a boot line is not filed work.</summary>
    [Fact]
    public void VerdictThenOnline_IsNotAwaiting()
    {
        Assert.False(Awaiting(
            "## [1] FROM implementer — x — imp-1 online",
            "## [2] FROM supervisor — x — BRIEF",
            "## [3] FROM implementer — x — done",
            "## [4] FROM supervisor — x — accepted",
            "## [5] FROM implementer — x — imp-1 online"));
    }

    [Fact]
    public void VerdictThenOnlineThenBrief_IsNotAwaiting()
    {
        Assert.False(Awaiting(
            "## [1] FROM supervisor — x — accepted",
            "## [2] FROM implementer — x — imp-1 online",
            "## [3] FROM supervisor — x — BRIEF"));
    }

    [Fact]
    public void ReportThenNudgeThenVerdict_IsNotAwaiting()
    {
        Assert.False(Awaiting(
            "## [1] FROM supervisor — x — BRIEF",
            "## [2] FROM implementer — x — report filed",
            "## [3] FROM app — x — unread traffic",
            "## [4] FROM supervisor — x — accepted"));
    }

    [Fact]
    public void BriefThenMemberWork_IsAwaiting()
    {
        Assert.True(Awaiting(
            "## [1] FROM supervisor — x — BRIEF",
            "## [2] FROM implementer — x — done, 5 commits"));
    }

    /// <summary>Never briefed: nothing was asked, so nothing is owed.</summary>
    [Fact]
    public void OnlineOnly_IsNotAwaiting()
    {
        Assert.False(Awaiting("## [1] FROM implementer — x — imp-1 online"));
    }

    /// <summary>An app nudge is not the supervisor answering, so it does not end the wait.</summary>
    [Fact]
    public void BriefThenReportThenNudge_IsAwaiting()
    {
        Assert.True(Awaiting(
            "## [1] FROM supervisor — x — BRIEF",
            "## [2] FROM implementer — x — report filed",
            "## [3] FROM app — x — unread traffic"));
    }

    /// <summary>The boot line is skipped; the report after it is not.</summary>
    [Fact]
    public void VerdictThenOnlineThenReport_IsAwaiting()
    {
        Assert.True(Awaiting(
            "## [1] FROM supervisor — x — accepted",
            "## [2] FROM implementer — x — imp-1 online",
            "## [3] FROM implementer — x — resumed and finished the last item"));
    }

    /// <summary>
    /// "one token, then the word online" — so a genuine report that happens to end in that word is
    /// filed work, not a hello. This is what keeps the exclusion safe rather than clever.
    /// </summary>
    [Fact]
    public void AReportEndingInTheWordOnline_IsFiledWork()
    {
        Assert.True(Awaiting(
            "## [1] FROM supervisor — x — BRIEF",
            "## [2] FROM implementer — x — the server is back online"));
    }

    /// <summary>The same sentence, from the ledger's side: the verdict arms where work was filed.</summary>
    [Fact]
    public void TheVerdictAfterFiledWork_ArmsTheLedger()
    {
        var entries = Parse(
            "## [1] FROM implementer — x — imp-1 online",
            "## [2] FROM supervisor — x — BRIEF",
            "## [3] FROM implementer — x — done",
            "## [4] FROM app — x — unread traffic",
            "## [5] FROM supervisor — x — accepted");

        Assert.True(LedgerHealth_Tracker.Is_VerdictAt(entries, 5));
    }

    /// <summary>...and a brief that merely follows a boot line does not.</summary>
    [Fact]
    public void ABriefAfterABootLine_DoesNotArmTheLedger()
    {
        var entries = Parse(
            "## [1] FROM supervisor — x — accepted",
            "## [2] FROM implementer — x — imp-1 online",
            "## [3] FROM supervisor — x — BRIEF");

        Assert.False(LedgerHealth_Tracker.Is_VerdictAt(entries, 3));
    }

    static bool Awaiting(params string[] headers)
    {
        return MemberState_Resolver.Is_AwaitingVerdict(Parse(headers));
    }

    static IReadOnlyList<AIOrchestratorCoreLib.Channels.ChannelEntry.IChannelEntry> Parse(params string[] headers)
    {
        return ChannelEntry_Parser.Parse_All(string.Join("\n\n", headers.Select(header => $"{header}\n\nbody\n")));
    }
}
