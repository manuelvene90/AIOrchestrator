using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Tests.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// F3, PROBE (a), END TO END — the probe that parked a healthy session for fourteen hours with
/// <c>FailedAttempts = 0</c>.
///
/// <para>
/// A turn can SUCCEED and still reach <c>Record_Failure</c>: <c>Run_Turn_Async</c> routes a turn
/// whose reply could not be appended (the channel was locked for the whole append budget) into the
/// failure path so the same traffic is retried. The result it carries is the SUCCESSFUL one, so
/// <c>result.ResultText</c> is the MODEL'S OWN WORDS — and the usage-limit gate was a
/// case-insensitive <c>Contains("limit")</c> over exactly that text. A report mentioning a limit
/// and a reset clause therefore bought the session an appointment, spent no attempt, and left it
/// out of the world until somebody typed <c>/resume</c>.
/// </para>
/// <para>
/// It is its own file because it has to HOLD the channel gate, which is process-wide state — the
/// <see cref="CHANNEL_LOCK_COLLECTION"/> is what keeps it from colliding with the other tests that
/// do the same.
/// </para>
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class ASucceededTurnIsNeverParkedForALimitTests
{
    /// <summary>
    /// A perfectly ordinary report that happens to discuss a limit and quote a reset clause — which
    /// is what a session working on THIS feature writes all day.
    /// </summary>
    const string REPORT_ABOUT_A_LIMIT =
        "REPORT\n\nThe parser now reads the refusal 'You've hit your weekly limit · resets 5am (Europe/Berlin)' "
        + "and the suite is green. Nothing is blocked.";

    [Fact]
    public async Task ATurnThatExitedZero_ButWhoseReplyTheChannelRefused_SpendsAnAttemptAndIsNeverParked()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        var channelFile = harness.Paths.Get_ImplementerChannelFile(orchId, memberId);

        harness.Write_Scenario(
            """{"default":{"result":"REPORT_TEXT"}}"""
                .Replace("REPORT_TEXT", REPORT_ABOUT_A_LIMIT.Replace("\n", "\\n")));

        var dispatcher = harness.Create_Dispatcher(retryBackoff: TimeSpan.FromMilliseconds(100));

        // The brief goes in BEFORE the gate is taken — the turn has to have something to answer.
        Assert.True(ChannelAppender.Append_SessionEntry(channelFile, ChannelAuthors.Supervisor, "BRIEF", "start on the parser", DateTime.Now));

        using var held = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);

        // THE HOLD MUST OUTLAST THE WAIT BELOW, and it did not: the gate was held for 30 seconds
        // while the outcome was awaited for 60. On a loaded machine the dispatcher needs more than
        // thirty seconds to spawn a real process and spend an attempt — the lease then expired, the
        // appends the turn makes started SUCCEEDING, and the test failed reporting "the attempt was
        // not counted" about a premise that had quietly evaporated. Red once in three full parallel
        // runs on 2026-09-10. The claim is untouched: what changed is that the refusal this test is
        // built on lasts as long as the test looks for its consequence.
        var holder = Task.Run(() => ChannelWrite_Lock.Try_Run_Serialised(channelFile, TimeSpan.FromMinutes(3), () =>
        {
            held.Set();
            release.Wait(TimeSpan.FromMinutes(2));
        }, out _));

        Assert.True(held.Wait(TimeSpan.FromSeconds(10)), "the test could not take the channel gate it is testing against");

        bool counted;
        var observedFailedAttempts = 0;

        try
        {
            // Every append the turn makes now fails: its reply, and then its turn_ended record. The
            // turn itself exits 0 with is_error false and no api_error_status.
            counted = PrintRunnerTestHarness.Drive_Until(
                dispatcher,
                () =>
                {
                    // OBSERVED, NOT RE-READ. The attempt count is recorded at the instant the
                    // predicate sees it, because the assertion below used to take a SECOND reading
                    // after Stop_Async and could find 0 where Drive_Until had just found 1 — the
                    // state file is written by the dispatcher while the test reads it, so the two
                    // readings are two different facts. That gave one claim two routes to being
                    // true and one route to being falsely false: red once in three full runs on
                    // 2026-09-10, with the message "the attempt was not counted" while `counted`
                    // was itself true. Decision 20's corollary — never assert on a state with two
                    // routes to it — applies to a state read twice as much as to one reached twice.
                    observedFailedAttempts = harness.Read_State(SessionRoles.Implementer, orchId, memberId).FailedAttempts;

                    return observedFailedAttempts >= 1;
                },
                TimeSpan.FromSeconds(60));
        }
        finally
        {
            release.Set();
            await holder;
        }

        await dispatcher.Stop_Async();

        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);

        // THE WHOLE FINDING: the appointment must not exist. Before the fix this was a stamp up to a
        // day out, with FailedAttempts still 0 — so the session could never stall, never alert, and
        // never be retried by anything except the owner noticing.
        Assert.Null(state.RetryNotBeforeUtc);
        Assert.True(counted, $"the successful turn was PARKED instead of counted — failed_attempts {state.FailedAttempts}, retry_not_before {state.RetryNotBeforeUtc:O}");
        Assert.True(observedFailedAttempts >= 1, "the attempt was not counted, so this turn can never stall or alert");
    }
}
