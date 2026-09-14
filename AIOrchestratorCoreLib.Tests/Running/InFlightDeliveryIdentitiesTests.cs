using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE ONE THING THE UNIT TESTS OF THE REPORTER CANNOT PIN: that the identities the DISPATCHER records
/// for an in-flight turn actually intersect the ones the REPORTER computes from the channel file.
///
/// <para>
/// The reporter's own cases feed it a set built by the test, so they pass whatever the dispatcher
/// really stores — and two independent things could make the fix of 2026-09-10 inert in production
/// while all of them stayed green: a session key that does not match the one the supervisor is
/// registered under, and a digest computed over text that differs between the two reads. This drives
/// the REAL dispatcher (fake CLI, real state files, real channel appends) through a boot turn and a
/// member report, waits until a turn is genuinely in flight, and compares.
/// </para>
/// <para>
/// IT CARRIES ITS OWN CONTROL, which is why it is worth its seconds: the same pending set is described
/// twice, once with the dispatcher's real set and once with an empty one. The empty case MUST still
/// call the entry dropped — that is the false alarm of 14:12:43 reproduced on demand — and the real
/// case must not. A test that only asserted the second would pass just as well if the reporter had
/// stopped warning about anything at all (decision 20: a state with two routes to it pins neither).
/// </para>
/// <para>
/// Adopted from a probe written by the adversarial review of this branch, which is what proved the
/// wiring in the first place. Kept because nothing else covers it.
/// </para>
/// </summary>
public class InFlightDeliveryIdentitiesTests
{
    [Fact]
    public async Task WhatTheDispatcherIsCarrying_IntersectsWhatTheReporterReads_AndTheBlindCaseStillWarns()
    {
        using var harness = new PrintRunnerTestHarness("supervisor,implementer");
        harness.Register_Supervisor("repo-1", SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"on it"}}""");

        var member = harness.Store.Add_Member("repo-1", MemberKinds.Implementer).Members[^1].MemberId;
        var dispatcher = harness.Create_Dispatcher();

        // The boot turn, so cursors exist for the owner channel and for the spoke.
        Assert.True(
            PrintRunnerTestHarness.Drive_Until(
                dispatcher,
                () => harness.Read_State(SessionRoles.Supervisor, "repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).ExecutedTurns.Count == 1,
                PrintRunnerTestHarness.GENEROUS),
            "the boot turn never ran");

        Assert.True(ChannelAppender.Append_SessionEntry(
            harness.Paths.Get_ImplementerChannelFile("repo-1", member),
            ChannelAuthors.Implementer,
            "REPORT — done",
            "Done. F1 closed, all gates re-run.",
            DateTime.Now));

        Assert.True(
            PrintRunnerTestHarness.Drive_Until(
                dispatcher,
                () => dispatcher.Is_TurnInFlight("repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID),
                PrintRunnerTestHarness.GENEROUS),
            "no supervisor turn ever went in flight");

        var delivering = dispatcher.Get_DeliveringIdentities("repo-1", SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);

        // AGENT-WRITTEN CASING MUST NOT MISS. The orchId reaches a close straight from request JSON,
        // and the session store resolves it through a file path (case-insensitive on Windows), so a
        // lookup that disagreed with the operation that succeeded would hand the reporter an empty set.
        var deliveringOddCase = dispatcher.Get_DeliveringIdentities("REPO-1", " sup ");

        var described = UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(harness.Paths, "repo-1", member, delivering);
        var describedBlind = UndeliveredSpokeTraffic_Reporter.Describe_Pending_OrNull(harness.Paths, "repo-1", member, new HashSet<string>(StringComparer.Ordinal));

        await dispatcher.Stop_Async();

        Assert.NotEmpty(delivering);
        Assert.Equal(delivering, deliveringOddCase);

        Assert.NotNull(describedBlind);
        Assert.True(describedBlind.Value.AnythingDropped, $"the control did not reproduce the false alarm — line was: {describedBlind.Value.Line}");

        Assert.NotNull(described);
        Assert.False(described.Value.AnythingDropped, $"the identities did NOT intersect. delivering=[{string.Join(",", delivering)}] line={described.Value.Line}");
    }
}
