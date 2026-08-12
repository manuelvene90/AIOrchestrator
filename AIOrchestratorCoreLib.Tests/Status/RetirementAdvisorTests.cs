using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// The owner's complaint, 2026-08-12: "if an impl is done and the sup doesn't want to use it anymore
/// and spawns another one, the old one stays open forever monitoring the channel and wasting tokens."
///
/// Every test here is really about the same question — can the app tell FINISHED from BUSY without
/// guessing? It can now, and only because the member says so itself. So the exclusions carry as much
/// weight as the inclusion: each one is a member that would be harmed by being retired, and the
/// advisor exists to flag, never to kill.
/// </summary>
public class RetirementAdvisorTests
{
    static readonly DateTime NOW = new(2026, 8, 12, 12, 0, 0);

    /// <summary>The case the owner is paying for: briefed, delivered, declared, and left open.</summary>
    [Fact]
    public void AMemberThatDeclaredItselfIdleLongAgoIsWorthClosing()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "review the marker fix", "2026-08-12 09:00"),
            (ChannelAuthors.Reviewer, "STANDING BY — findings filed", "2026-08-12 10:00"));

        Assert.True(Retirement_Advisor.Should_SuggestClosing(entries, hasBeenBriefed: true, NOW));
        Assert.Equal("2 h 0 min", Retirement_Advisor.Describe_IdleFor_OrNull(entries, NOW));
    }

    /// <summary>
    /// A member that declared five minutes ago is PAUSED, not accumulated. Members declare between
    /// tasks routinely, and flagging that would train everyone to ignore the flag.
    /// </summary>
    [Fact]
    public void ARecentDeclarationIsAPauseRatherThanAnAccumulation()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "the brief", "2026-08-12 11:00"),
            (ChannelAuthors.Implementer, "STANDING BY — waiting for the next one", "2026-08-12 11:55"));

        Assert.False(Retirement_Advisor.Should_SuggestClosing(entries, hasBeenBriefed: true, NOW));
    }

    /// <summary>
    /// AWAITING A VERDICT IS NOT FINISHED — it is owed one, and closing it discards filed work. This
    /// is the exclusion that would cost the most if it were wrong.
    /// </summary>
    [Fact]
    public void AMemberAwaitingAVerdictIsNeverSuggested()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "the brief", "2026-08-12 08:00"),
            (ChannelAuthors.Implementer, "done, commit abc1234, 582 tests", "2026-08-12 09:00"));

        Assert.False(Retirement_Advisor.Should_SuggestClosing(entries, hasBeenBriefed: true, NOW));
    }

    /// <summary>Mid-batch, however long the window has been open.</summary>
    [Fact]
    public void AMemberWithAnOpenWindowIsNeverSuggested()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "the brief", "2026-08-12 08:00"),
            (ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Parser.cs", "2026-08-12 08:30"));

        Assert.False(Retirement_Advisor.Should_SuggestClosing(entries, hasBeenBriefed: true, NOW));
    }

    /// <summary>Waiting on a human is not idleness, and the human may be asleep.</summary>
    [Fact]
    public void AMemberBlockedOnTheOwnerIsNeverSuggested()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "the brief", "2026-08-12 08:00"),
            (ChannelAuthors.Implementer, "BLOCKED ON OWNER — which schema?", "2026-08-12 08:30"));

        Assert.False(Retirement_Advisor.Should_SuggestClosing(entries, hasBeenBriefed: true, NOW));
    }

    /// <summary>
    /// A freshly spawned member waiting for its first brief is the correct state for every
    /// orchestration's first minutes — and retiring it would undo the pre-spawned pair by design.
    /// </summary>
    [Fact]
    public void ANeverBriefedMemberIsNeverSuggested()
    {
        var entries = Build(
            (ChannelAuthors.Implementer, "imp-1 online", "2026-08-12 08:00"),
            (ChannelAuthors.Implementer, "STANDING BY — awaiting a brief", "2026-08-12 08:05"));

        Assert.False(Retirement_Advisor.Should_SuggestClosing(entries, hasBeenBriefed: false, NOW));
    }

    /// <summary>
    /// An unreadable clock must never ARGUE FOR retiring a session. A stamp that cannot be parsed,
    /// or one in the future, yields no duration — so the flag stays off rather than firing on a
    /// number nobody can trust.
    /// </summary>
    [Theory]
    [InlineData("not a date at all")]
    [InlineData("2026-08-13 23:00")]
    public void AnUntrustworthyStampNeverSuggestsClosing(string stamp)
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "the brief", "2026-08-12 08:00"),
            (ChannelAuthors.Implementer, "STANDING BY", stamp));

        Assert.False(Retirement_Advisor.Should_SuggestClosing(entries, hasBeenBriefed: true, NOW));
        Assert.Null(Retirement_Advisor.Describe_IdleFor_OrNull(entries, NOW));
    }

    /// <summary>
    /// The duration comes from the DECLARATION's stamp, not from the last thing written to the file.
    /// An app nudge landing after the declaration must not make an idle member look freshly active —
    /// that is the mtime trap, one level up.
    /// </summary>
    [Fact]
    public void AnAppEntryAfterTheDeclarationDoesNotResetTheClock()
    {
        var entries = Build(
            (ChannelAuthors.Supervisor, "the brief", "2026-08-12 08:00"),
            (ChannelAuthors.Implementer, "STANDING BY — nothing owed", "2026-08-12 09:00"),
            (ChannelAuthors.App, "unread traffic — you have not answered", "2026-08-12 11:58"));

        Assert.Equal("3 h 0 min", Retirement_Advisor.Describe_IdleFor_OrNull(entries, NOW));
    }

    [Fact]
    public void AnEmptyChannelSuggestsNothing()
    {
        Assert.False(Retirement_Advisor.Should_SuggestClosing([], hasBeenBriefed: true, NOW));
        Assert.Null(Retirement_Advisor.Describe_IdleFor_OrNull([], NOW));
    }

    /// <summary>
    /// THE FLAG MUST NEVER REACH TELEGRAM, and this is the assertion that keeps it that way.
    ///
    /// The owner cannot close a member — the supervisor can — so pushing this to their phone would
    /// be the app forwarding somebody else's job to their lock screen, which is exactly what rule 15
    /// exists to stop. Suppression is by SUBJECT, so the constant and the mirror's list must agree
    /// exactly; asserting through the real predicate is what makes a rename impossible to get half
    /// right.
    /// </summary>
    [Fact]
    public void TheFlagSubjectIsSuppressedFromTelegram()
    {
        var entry = ChannelEntry_Factory.Create(
            1, ChannelAuthors.App, "2026-08-12 12:00", Retirement_Advisor.FLAG_SUBJECT, "body",
            $"## [1] FROM app — 2026-08-12 12:00 — {Retirement_Advisor.FLAG_SUBJECT}");

        var ownerChannel = DiscoveredChannel_Factory.Create_ForOwner("orch-1", "owner-channel.md");

        Assert.False(AIOrchestratorCoreLib.Mirroring.MirrorText_Formatter.Should_Mirror(ownerChannel, entry));
    }

    static IReadOnlyList<IChannelEntry> Build(params (ChannelAuthors Author, string Subject, string Stamp)[] entries)
    {
        List<IChannelEntry> built = [];

        for (var index = 0; index < entries.Length; index++)
        {
            var (author, subject, stamp) = entries[index];

            built.Add(ChannelEntry_Factory.Create(
                index + 1, author, stamp, subject, "body",
                $"## [{index + 1}] FROM {author} — {stamp} — {subject}\nbody"));
        }

        return built;
    }
}
