using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Running.WakeDecision;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE SAME FOUR RULES, ASKED FROM OUTSIDE THE DISPATCHER. These cases restate what
/// <see cref="WakeUpDigestReviewFixTests"/> already pins through the dispatcher — deliberately, because
/// the extraction is only correct if both callers get the same answers. If one of these disagrees with
/// its dispatcher twin, the extraction changed behaviour and the extraction is wrong.
/// </summary>
public class WakeDecisionResolverTests
{
    static readonly DateTime T0 = new(2026, 9, 15, 10, 0, 0);

    [Fact]
    public void The_owner_wakes_it_now()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_OwnerEntry("do the merge");

        var decision = fixture.Resolve(digestHeldSince: null, nowLocal: T0);

        Assert.NotNull(decision);
        Assert.Contains("the owner wrote", decision!.Reason);
    }

    [Fact]
    public void An_ordinary_member_report_is_held_for_the_digest()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "TASK 1 committed abc1234");

        var decision = fixture.Resolve(digestHeldSince: T0, nowLocal: T0.AddMinutes(1));

        Assert.Null(decision);
    }

    [Fact]
    public void The_same_report_wakes_it_once_the_window_has_passed()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_SeenMemberEntry("imp-1", "TASK 1 committed abc1234");

        var decision = fixture.Resolve(digestHeldSince: T0, nowLocal: T0.AddMinutes(6));

        Assert.NotNull(decision);

        // NOT "because six minutes passed" — that is true of the held case too until the window is up.
        // The reason is the ONE thing that distinguishes the two calls, so it is what is asserted.
        Assert.Contains("digest elapsed", decision!.Reason);
    }

    [Fact]
    public void An_app_note_never_produces_a_decision_and_rides_the_next_one()
    {
        using var fixture = WakeFixture.ForSupervisor("repo-1").With_AppNote("PLAN.md is behind your verdicts", T0);

        Assert.Null(fixture.Resolve(digestHeldSince: null, nowLocal: T0.AddMinutes(1)));

        fixture.With_OwnerEntry("status?");
        var decision = fixture.Resolve(digestHeldSince: null, nowLocal: T0.AddMinutes(2));

        Assert.NotNull(decision);
        Assert.Contains(decision!.Pending, p => p.Entry.Author == ChannelAuthors.App);

        // THE NOTE RIDES IN FRONT AND DECIDED NOTHING — the two halves of the rule, and a set that
        // merely CONTAINS the note would pass with either of them broken.
        Assert.Equal(ChannelAuthors.App, decision.Pending[0].Entry.Author);
        Assert.Contains("the owner wrote", decision.Reason);
    }

    /// <summary>
    /// A SUPERVISOR THAT HAS TAKEN A TURN, over a real supervision root with real channel files and a
    /// real state file — the resolver reads cursors and entries off the disk, so a mock of either
    /// would be a test of the mock. The boot turn is deliberately spent (<c>ExecutedTurns</c> is
    /// non-empty): it is the one rule that answers before all the others, so leaving it unspent would
    /// make every case here read "the session has not taken a turn yet" and pin nothing.
    /// </summary>
    sealed class WakeFixture : IDisposable
    {
        readonly PrintRunnerTestHarness _harness;
        readonly string _orchId;
        readonly string _supervisorId;

        WakeFixture(PrintRunnerTestHarness harness, string orchId, string supervisorId)
        {
            _harness = harness;
            _orchId = orchId;
            _supervisorId = supervisorId;
        }

        public static WakeFixture ForSupervisor(string orchId)
        {
            var harness = new PrintRunnerTestHarness("supervisor:stream");
            var supervisorId = harness.Register_Supervisor(orchId);
            var fixture = new WakeFixture(harness, orchId, supervisorId);

            fixture.Spend_TheBootTurn();
            return fixture;
        }

        public WakeFixture With_OwnerEntry(string text)
        {
            if (!ChannelAppender.Append_OwnerEntry(_harness.Paths.Get_OwnerChannelFile(_orchId), text, T0))
                throw new Exception($"could not append the owner entry to '{_orchId}'");

            return this;
        }

        /// <summary>
        /// A member whose spoke this supervisor HAS already been handed something from — the greeting
        /// is appended and then absorbed into the cursor, exactly as the first turn absorbs it, so what
        /// the case measures afterwards is the digest and not the first-entry exemption.
        /// </summary>
        public WakeFixture With_SeenMemberEntry(string memberId, string body)
        {
            var (_, allocated) = _harness.Register_Member(MemberKinds.Implementer, _orchId);

            if (!string.Equals(allocated, memberId, StringComparison.Ordinal))
                throw new Exception($"the store allocated '{allocated}', not the '{memberId}' this case names");

            var spokeFile = _harness.Paths.Get_ImplementerChannelFile(_orchId, allocated);

            if (!ChannelAppender.Append_SessionEntry(spokeFile, ChannelAuthors.Implementer, $"{allocated} online", "reporting for duty", T0))
                throw new Exception($"could not append '{allocated}' greeting to its spoke");

            Absorb_Spoke(allocated, spokeFile);

            if (!ChannelAppender.Append_SessionEntry(spokeFile, ChannelAuthors.Implementer, "REPORT", body, T0))
                throw new Exception($"could not append '{allocated}' report to its spoke");

            return this;
        }

        public WakeFixture With_AppNote(string subject, DateTime stampedLocal)
        {
            if (!ChannelAppender.Append_AppEntry(_harness.Paths.Get_OwnerChannelFile(_orchId), AppEntryAudiences.Agent, subject, "Bring it up to date.", stampedLocal))
                throw new Exception($"could not append the app note to '{_orchId}'");

            return this;
        }

        public IWakeDecision? Resolve(DateTime? digestHeldSince, DateTime nowLocal)
        {
            return WakeDecision_Resolver.Resolve_OrNull(
                _harness.Paths, _harness.Store, Read_State(), RunnerConfigs_Factory.Create_Default(), digestHeldSince, nowLocal);
        }

        IPrintSessionState Read_State()
        {
            return _harness.Read_State(SessionRoles.Supervisor, _orchId, _supervisorId);
        }

        void Write_State(IPrintSessionState state)
        {
            PrintSessionState_Store.Write(PrintSessionState_Store.Get_StateFile(_harness.Paths, SessionRoles.Supervisor, _orchId, _supervisorId), state);
        }

        void Spend_TheBootTurn()
        {
            var state = Read_State();

            Write_State(PrintSessionState_Factory.CreateFrom_Existing_TurnExecuted(
                state,
                ExecutedTurn_Factory.Create(1, "req-boot", 0, 0, DateTime.UtcNow, "ok", null),
                state.SessionId,
                state.Cursors));
        }

        /// <summary>Records everything now live in the spoke as delivered, the way a completed turn does.</summary>
        void Absorb_Spoke(string memberId, string spokeFile)
        {
            var state = Read_State();
            var source = TurnSource_Factory.Create_Spoke(memberId, spokeFile);
            var baseline = TurnCursor_Factory.Create_Baseline(source, SessionRoles.Supervisor, ChannelEntry_Parser.Parse_All(File.ReadAllText(spokeFile)));

            if (baseline.Delivered.Count == 0 || baseline.HighWaterIndex == 0)
                throw new Exception($"the greeting was not absorbed into '{memberId}' cursor, so the case would measure the first-entry exemption instead of the digest");

            Write_State(PrintSessionState_Factory.CreateFrom_Existing_Cursors(
                state, [.. state.Cursors.Where(cursor => !string.Equals(cursor.SourceKey, memberId, StringComparison.OrdinalIgnoreCase)), baseline]));
        }

        public void Dispose()
        {
            _harness.Dispose();
        }
    }
}
