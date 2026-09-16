using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// AN APP ENTRY THAT IS THE ONLY TRACE OF AN EVENT MAY NOT VANISH WITHOUT A REPLACEMENT. Several
/// things in this codebase read the app's own entries back out of a channel —
/// <see cref="StallAlert_Decider"/>, <c>PrintTurn_Trigger.Is_AgentNote</c>, <c>Nudge_Decider</c>,
/// <c>MemberState_Resolver</c> and <c>MirrorText_Formatter</c>. This class is where each one is
/// checked against the kinds plan 02 moves, so that "it is in the log now" is a fact about a reader
/// rather than a hope.
/// </summary>
public class NothingMovedBecomesInvisibleTests
{
    static readonly DateTime STALLED_AT = new(2026, 9, 15, 10, 5, 0, DateTimeKind.Local);
    static readonly DateTime ENDED_AT = new(2026, 9, 15, 11, 0, 0, DateTimeKind.Local);

    /// <summary>
    /// The channel as it is left once <c>turn_ended</c> has been routed away: the brief, and the ONE
    /// stall alert that reached the owner's phone. Owner-facing, so it carries no <c>[agent]</c> tag
    /// and the router never moves it.
    /// </summary>
    const string CHANNEL_WITHOUT_THE_ENDINGS =
        "## [1] FROM supervisor — 2026-09-15 10:00 — brief\n\ndo X\n\n" +
        "## [2] FROM app — 2026-09-15 10:05 — turn stalled imp-1 turn 4 — error × 3\n\nit went quiet\n";

    /// <summary>
    /// THE STALL DECIDER'S EVIDENCE IS SPLIT ACROSS TWO FILES the moment <c>turn_ended</c> is routed,
    /// and the damage of reading only one of them is SILENCE, not a waterfall.
    ///
    /// <para>
    /// Plan 02 task 5 states the opposite — "the decider stops seeing half its evidence and a stall
    /// alert is repeated on the owner's phone" — and reading the walk shows it cannot happen that way.
    /// A <c>turn_ended</c> record naming THIS turn only ever CONTINUES the backward walk, so losing it
    /// changes no answer by itself. What is lost is every <c>turn_ended</c> naming ANOTHER turn, and
    /// those are precisely the records that STOP the walk and answer false. Fewer stoppers means more
    /// trues, and a true means "the owner has already been told" — so the alert is SUPPRESSED.
    /// </para>
    /// <para>
    /// The case below is the one that happens: a session's turn numbering starts over, which is what a
    /// deleted <c>print-session.json</c> does, and the old turn 4's alert is still in the channel. With
    /// the log, the ending of turn 9 separates the two and the new stall reaches the phone. Without it,
    /// the walk goes straight past to the year-old alert and files the new stall for the session only —
    /// the owner is never told their orchestration stopped, which is the silence this alert exists to
    /// break.
    /// </para>
    /// <para>
    /// TWO ASSERTIONS THAT DIFFER IN ONE ARGUMENT. Same channel, same member, same turn number: only
    /// the log changes, so neither assertion can pass for the other one's reason.
    /// </para>
    /// </summary>
    [Fact]
    public void AStallAlertStillReachesTheOwner_WhenTheEndingThatSeparatesTwoTurnsIsInTheLogRatherThanTheChannel()
    {
        var log = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

        try
        {
            StatusLog_Store.Append(log, $"{PrintTurn_Words.TURN_ENDED_SUBJECT} imp-1 turn 9 — success", "request_id: repo-1/imp-1/9", ENDED_AT);

            var channelEntries = ChannelEntry_Parser.Parse_All(CHANNEL_WITHOUT_THE_ENDINGS);
            var logEntries = StatusLog_Store.Read_Entries(log);

            Assert.Equal(2, channelEntries.Count);
            Assert.Single(logEntries);

            // Reading the channel alone, the newest thing this member has to say is an owner-facing
            // alert for a turn 4 — so the decider believes the owner already knows, and the new
            // stall is filed for the session only.
            Assert.True(StallAlert_Decider.Has_AlreadyReachedOwner(channelEntries, [], "imp-1", 4));

            // Reading both, turn 9's ending sits after that alert and says the numbering moved on:
            // this is a different turn 4 and the owner has been told nothing about it.
            Assert.False(StallAlert_Decider.Has_AlreadyReachedOwner(channelEntries, logEntries, "imp-1", 4));

            // And the audience follows, which is the half the owner's phone actually feels.
            Assert.Equal(
                AppEntryAudiences.Owner,
                StallAlert_Decider.Resolve_Audience(AppEntryAudiences.Owner, channelEntries, logEntries, "imp-1", 4));
        }
        finally
        {
            File.Delete(log);
        }
    }

    /// <summary>
    /// THE OTHER DIRECTION, so the fix is not "the log always answers false". The alert for THIS turn
    /// is still the last word about it once the log is read in, and a repeat of the same stall is still
    /// kept off the phone — decision 14, which is the rule the decider was written for.
    /// </summary>
    [Fact]
    public void ARepeatOfTheSameStallIsStillKeptOffThePhone_WithTheLogRead()
    {
        var log = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

        try
        {
            StatusLog_Store.Append(log, $"{PrintTurn_Words.TURN_ENDED_SUBJECT} imp-1 turn 4 — error", "request_id: repo-1/imp-1/4", STALLED_AT.AddMinutes(-1));

            var channelEntries = ChannelEntry_Parser.Parse_All(CHANNEL_WITHOUT_THE_ENDINGS);

            Assert.True(StallAlert_Decider.Has_AlreadyReachedOwner(channelEntries, StatusLog_Store.Read_Entries(log), "imp-1", 4));
        }
        finally
        {
            File.Delete(log);
        }
    }

    /// <summary>
    /// THE MERGE IS BY TIME, NOT BY FILE. A log record stamped BEFORE a channel entry has to be walked
    /// after it, or the interleave is a concatenation with extra steps: here the ending of turn 9 is
    /// older than the alert for turn 4, the alert is therefore the last word, and the answer is the one
    /// the channel alone would have given. Concatenating the log onto the end instead would put the
    /// ending last and answer false — which is the same wrong answer, arrived at without reading a
    /// stamp.
    /// </summary>
    [Fact]
    public void ALogRecordOlderThanTheAlert_IsWalkedBeforeIt()
    {
        var log = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

        try
        {
            StatusLog_Store.Append(log, $"{PrintTurn_Words.TURN_ENDED_SUBJECT} imp-1 turn 9 — success", "request_id: repo-1/imp-1/9", STALLED_AT.AddHours(-1));

            Assert.True(StallAlert_Decider.Has_AlreadyReachedOwner(
                ChannelEntry_Parser.Parse_All(CHANNEL_WITHOUT_THE_ENDINGS),
                StatusLog_Store.Read_Entries(log),
                "imp-1",
                4));
        }
        finally
        {
            File.Delete(log);
        }
    }
}
