using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// ONE STALLED TURN, ONE MESSAGE ON THE PHONE. fincanva-6, 2026-09-11: a supervisor that could not log
/// in put "turn stalled sup turn 35 — error × 3" on the owner's phone three times (#158, #165, #172) —
/// once per resend of their message, and once more when the daemon restarted and retried the stalled
/// turn by itself. The end-to-end cases drive the real dispatcher against the FakeClaude stub and ask
/// the MIRROR's own predicate what would have reached the phone; the pure cases pin the matching rule.
/// </summary>
public class StallAlertDeciderTests
{
    const string ORCH = "repo-1";
    const string SUP = SessionLaunch_Factory.SUPERVISOR_MEMBER_ID;

    const string BOOT_OK = """{"default":{"result":"supervisor online — Repo\n\nReady."}}""";
    const string ANSWER_OK = """{"default":{"result":"Smoke\n\nRun the checkout flow."}}""";

    /// <summary>The incident's own failure: the CLI refuses every attempt, and no usage limit is involved.</summary>
    const string NOT_LOGGED_IN = """{"default":{"is_error":true,"exit_code":1,"result":"Not logged in · Please run /login"}}""";

    [Fact]
    public async Task TheSameTurnStallingAgain_AfterAResendAndAfterARestart_ReachesThePhoneOnce_AndTheChannelKeepsEveryLine()
    {
        using var harness = new PrintRunnerTestHarness("supervisor");
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        Boot(harness);

        harness.Write_Scenario(NOT_LOGGED_IN);
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        // The owner's message, the stall; the resend, the same turn stalling again.
        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), "Dimmi che smoke devo fare per finire", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Stalls(harness, 2).Count == 1, PrintRunnerTestHarness.GENEROUS), Describe(harness));

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), "Dimmi che smoke devo fare per finire", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Stalls(harness, 2).Count == 2, PrintRunnerTestHarness.GENEROUS), Describe(harness));
        await dispatcher.Stop_Async();

        // THE RESTART: a dispatcher with no memory at all retries the stalled turn by itself, which is
        // how #172 was written four seconds after the daemon came back up.
        var restarted = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));
        Assert.True(PrintRunnerTestHarness.Drive_Until(restarted, () => Stalls(harness, 2).Count == 3, PrintRunnerTestHarness.GENEROUS), Describe(harness));
        await restarted.Stop_Async();

        var stalls = Stalls(harness, 2);
        var ownerChannel = Owner_Channel(harness);

        // The channel is the audit trail: all three stalls are there.
        Assert.Equal(3, stalls.Count);

        // The phone got the FIRST, and only the first.
        Assert.True(MirrorText_Formatter.Should_Mirror(ownerChannel, stalls[0]), $"the first stall never reached the phone: '{stalls[0].Subject}'");
        Assert.False(MirrorText_Formatter.Should_Mirror(ownerChannel, stalls[1]), $"the resend's stall stacked a second alert: '{stalls[1].Subject}'");
        Assert.False(MirrorText_Formatter.Should_Mirror(ownerChannel, stalls[2]), $"the restart's stall stacked a third alert: '{stalls[2].Subject}'");

        // And the suppression is said, never silent.
        Assert.Contains("stalled again", File.ReadAllText(harness.Paths.Get_OrchestrationLogFile(ORCH)));
    }

    /// <summary>
    /// THE OTHER HALF: a DIFFERENT turn stalling is a different stall and reaches the owner. It passes
    /// on the code before the fix (which alerted every time) and exists to catch an over-eager one —
    /// a rule keyed on the member alone would silence it.
    /// </summary>
    [Fact]
    public async Task ALaterTurnStalling_ReachesThePhoneAgain()
    {
        using var harness = new PrintRunnerTestHarness("supervisor");
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        Boot(harness);

        harness.Write_Scenario(NOT_LOGGED_IN);
        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), "first question", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Stalls(harness, 2).Count == 1, PrintRunnerTestHarness.GENEROUS), Describe(harness));

        // Logged back in: turn 2 runs, and turn 3 is the next one.
        harness.Write_Scenario(ANSWER_OK);
        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), "are you back?", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, ORCH, SUP).ExecutedTurns.Count == 2, PrintRunnerTestHarness.GENEROUS), Describe(harness));

        harness.Write_Scenario(NOT_LOGGED_IN);
        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(ORCH), "second question", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Stalls(harness, 3).Count == 1, PrintRunnerTestHarness.GENEROUS), Describe(harness));
        await dispatcher.Stop_Async();

        var ownerChannel = Owner_Channel(harness);

        Assert.True(MirrorText_Formatter.Should_Mirror(ownerChannel, Assert.Single(Stalls(harness, 2))));
        Assert.True(MirrorText_Formatter.Should_Mirror(ownerChannel, Assert.Single(Stalls(harness, 3))), "a new turn's stall was silenced by the previous turn's alert");
    }

    [Fact]
    public void AnOwnerFacingAlertForTheSameTurn_MakesTheRepeatAgentOnly()
    {
        IReadOnlyList<IChannelEntry> entries =
        [
            Owner(1, "smoke?"),
            App(2, "[agent] turn_ended sup turn 35 — error"),
            App(3, "turn stalled sup turn 35 — error × 3"),
            Owner(4, "smoke?"),
            App(5, "[agent] turn_ended sup turn 35 — error"),
        ];

        Assert.Equal(AppEntryAudiences.Agent, StallAlert_Decider.Resolve_Audience(AppEntryAudiences.Owner, entries, "sup", 35));
    }

    [Fact]
    public void AnEarlierRepeatAlreadyFiledForTheRecord_DoesNotHideTheOneThatWasSent()
    {
        IReadOnlyList<IChannelEntry> entries =
        [
            App(1, "turn stalled sup turn 35 — error × 3"),
            App(2, "[agent] turn stalled sup turn 35 — failed outside the process × 3"),
            App(3, "[agent] turn_ended sup turn 35 — error"),
        ];

        Assert.True(StallAlert_Decider.Has_AlreadyReachedOwner(entries, "sup", 35));
    }

    [Fact]
    public void AnotherTurnNumber_OrAnotherMember_OrAPrefixOfTheNumber_IsNotTheSameStall()
    {
        IReadOnlyList<IChannelEntry> entries = [App(1, "turn stalled sup turn 35 — error × 3")];

        Assert.False(StallAlert_Decider.Has_AlreadyReachedOwner(entries, "sup", 36));
        Assert.False(StallAlert_Decider.Has_AlreadyReachedOwner(entries, "sup", 3));
        Assert.False(StallAlert_Decider.Has_AlreadyReachedOwner(entries, "su", 35));
        Assert.False(StallAlert_Decider.Has_AlreadyReachedOwner([App(1, "turn stalled sup turn 350 — error × 3")], "sup", 35));
    }

    /// <summary>
    /// A deleted <c>print-session.json</c> starts the numbering over, so an old alert for "turn 1" must
    /// not silence the new life's turn 1. The first record of the session naming another turn is where
    /// the walk back stops.
    /// </summary>
    [Fact]
    public void AnOldAlertBehindARecordOfAnotherTurn_BelongsToAnotherLife_AndDoesNotSilence()
    {
        IReadOnlyList<IChannelEntry> entries =
        [
            App(1, "turn stalled sup turn 1 — error × 3"),
            App(2, "[agent] turn_ended sup turn 57 — success"),
            App(3, "[agent] turn_ended sup turn 1 — error"),
        ];

        Assert.False(StallAlert_Decider.Has_AlreadyReachedOwner(entries, "sup", 1));
    }

    [Fact]
    public void AnAgentAudienceStall_StaysAgent()
    {
        Assert.Equal(AppEntryAudiences.Agent, StallAlert_Decider.Resolve_Audience(AppEntryAudiences.Agent, [], "imp-1", 4));
    }

    [Fact]
    public void TheWrittenSubjectIsTheOneTheMatcherReads()
    {
        var subject = StallAlert_Decider.Build_Subject("sup", 35, "error × 3");

        Assert.Equal("turn stalled sup turn 35 — error × 3", subject);
        Assert.True(StallAlert_Decider.Has_AlreadyReachedOwner([App(1, subject)], "sup", 35));
    }

    static void Boot(PrintRunnerTestHarness harness)
    {
        harness.Write_Scenario(BOOT_OK);
        var boot = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        Assert.True(PrintRunnerTestHarness.Drive_Until(boot, () => harness.Read_State(SessionRoles.Supervisor, ORCH, SUP).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS), Describe(harness));
        boot.Stop_Async().GetAwaiter().GetResult();
    }

    static List<IChannelEntry> Stalls(PrintRunnerTestHarness harness, int turnNumber)
    {
        var stem = StallAlert_Decider.Build_SubjectStem(SUP, turnNumber) + " ";

        return [.. ChannelEntry_Parser.Parse_All(File.ReadAllText(harness.Paths.Get_OwnerChannelFile(ORCH)))
            .Where(entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains(stem, StringComparison.Ordinal))];
    }

    static IDiscoveredChannel Owner_Channel(PrintRunnerTestHarness harness)
    {
        return ChannelDiscovery.Find_ChannelFiles(harness.Paths).Single(channel => channel.OrchId == ORCH && channel.IsOwnerChannel);
    }

    static string Describe(PrintRunnerTestHarness harness)
    {
        var file = harness.Paths.Get_OwnerChannelFile(ORCH);
        return $"Owner channel:\n{(File.Exists(file) ? File.ReadAllText(file) : "(none)")}";
    }

    static IChannelEntry App(int index, string subject)
    {
        return ChannelEntry_Factory.Create(index, ChannelAuthors.App, "2026-09-11 08:19", subject, "body", $"## [{index}] FROM app — 2026-09-11 08:19 — {subject}\n\nbody");
    }

    static IChannelEntry Owner(int index, string text)
    {
        return ChannelEntry_Factory.Create(index, ChannelAuthors.Owner, "2026-09-11 08:16", "via Telegram", text, $"## [{index}] FROM owner — 2026-09-11 08:16 — via Telegram\n\n{text}");
    }
}
