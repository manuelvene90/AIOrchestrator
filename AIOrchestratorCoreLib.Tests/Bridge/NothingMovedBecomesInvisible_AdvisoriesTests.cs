using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE BOOKKEEPING THAT LEFT THE CHANNEL, AND THE CONVERSATION THAT DID NOT — the three PLAN.md
/// advisories, the orphan report and the five coaching notices (2026-09-15 one-wake-model plan 02,
/// tasks 7-9).
///
/// <para>
/// A SOURCE SCAN, and written down as the weaker claim it is. Driving these sites from a test needs a
/// live engine, a mirror tick, measured idleness and a ledger on disk; what has to be true of them is
/// a fact about SHAPE — which helper each one calls, and in what order the choke point asks its two
/// questions — so that is what is asserted. It proves the routing is PRESENT AND PLACED, not that it
/// fires. The same honesty, and the same reason, as <see cref="PauseGatesEveryWakerScanTests"/> next
/// door.
/// </para>
/// <para>
/// IT LIVES IN ITS OWN FILE rather than in <c>NothingMovedBecomesInvisibleTests</c>, which tasks 5-6
/// create in parallel on another branch: two agents creating one file is an add/add conflict, and the
/// two halves belong together only after both have landed. Whoever merges them may fold this class
/// into that one — the filter <c>FullyQualifiedName~NothingMovedBecomesInvisible</c> already catches
/// both.
/// </para>
/// </summary>
public class NothingMovedBecomesInvisible_AdvisoriesTests
{
    const string ENGINE_FILE = "BridgeEngineModel.cs";

    static string Body(string signatureMark) => SourceTree.Extract_Method(ENGINE_FILE, signatureMark);

    // ---------------------------------------------------------------------------------------------
    // Task 7 — the ledger advisories
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// THE CHOKE POINT'S SCREENS ARE NOT BYPASSED BY ROUTING. Every piece of supervisor-facing
    /// attention traffic passes through <c>Append_SupervisorAttention_UnlessMeeting</c>, which refuses
    /// during a meeting and while an orchestration is paused — *"miss one and dormancy is a word"*
    /// (CLAUDE.md's PAUSE decision). A routed advisory that reached the status log while the owner was
    /// at the terminal, or while the orchestration was asleep, would be exactly the missed one: the
    /// note is still there at the session's next turn and the pause meant nothing.
    /// </summary>
    [Fact]
    public void TheSupervisorAttentionChokePoint_ScreensBeforeItRoutes()
    {
        var body = Body("bool Append_SupervisorAttention_UnlessMeeting");

        var meeting = body.IndexOf("Suppresses_SupervisorAttention", StringComparison.Ordinal);
        var paused = body.IndexOf("Is_Paused(orchId)", StringComparison.Ordinal);
        var route = body.IndexOf("Route_SupervisorNote(", StringComparison.Ordinal);

        Assert.True(meeting >= 0 && paused >= 0, "this scan is reading a choke point it does not understand");
        Assert.True(route >= 0, "the choke point no longer routes — the ledger advisories are back in the channel unconditionally");

        Assert.True(meeting < route, "a routed advisory is written during a meeting");
        Assert.True(paused < route, "a routed advisory is written to a paused orchestration");
    }

    /// <summary>
    /// AND THE ROUTE REACHES THE ROUTER. Without this the case above passes on a
    /// <c>Route_SupervisorNote</c> that appends to the channel and calls itself routing — the second
    /// route to the same green that makes a shape assertion worthless.
    /// </summary>
    [Fact]
    public void TheSupervisorRouteGoesThroughTheOneRouter_AndAsksTheSessionsOwnState()
    {
        var body = Body("bool Route_SupervisorNote");

        Assert.Contains("AppNote_Writer.Write(", body, StringComparison.Ordinal);

        // THE SINK IS A QUESTION ABOUT THIS SESSION, not about the role (BookkeepingSink_Policy). A
        // router handed null for the state keeps the channel, which is the safe answer; a router
        // handed nothing at all would not compile, so this pins that the state is READ rather than
        // defaulted at the call site.
        Assert.Contains("PrintSessionState_Store.Read_OrNull(", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// ALL THREE ADVISORIES, COUNTED. Bucket B of the plan's classification is three sites and routing
    /// two of them is not the task: the one left behind is the one nobody notices, because the other
    /// two prove the mechanism works.
    /// </summary>
    [Theory]
    [InlineData("async Task Check_LedgerHealth_Async", "PLAN.md is behind your verdicts")]
    [InlineData("void Report_LedgerShape", "PLAN.md has lines that cannot show progress")]
    [InlineData("void Report_StaleInProgress", "PLAN.md claims work that nobody is doing")]
    public void EveryLedgerAdvisory_NamesItsKind(string signatureMark, string subject)
    {
        var body = Body(signatureMark);

        Assert.Contains(subject, body, StringComparison.Ordinal);
        Assert.Contains("AppNoteKinds.LedgerAdvisory", body, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // Task 8 — the orphan report, and the respawn note that does not exist
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheOrphanReport_IsRouted()
    {
        var body = Body("async Task Nudge_IdleImplementers_Async");

        Assert.Contains("Describe_Report(", body, StringComparison.Ordinal);
        Assert.Contains("AppNoteKinds.OrphanReport", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE NUDGE STAYS AND THE REPORT MOVES, and the asymmetry is the whole of the care here.
    /// <c>Nudge_Decider</c>'s walk skips OWNER-facing app entries and stops at an agent-tagged one,
    /// because orphan escalation is the only proof a monitor is dead and can only run on a member that
    /// has already been nudged — so the app's own nudge must go on counting. Its case
    /// <c>AnAppEntryStillCountsAsInbound_BecauseEscalationDependsOnIt</c> was left as a tripwire for
    /// precisely this change; this is the tripwire read out loud, on the writing side.
    /// </summary>
    [Fact]
    public void TheMemberNudgeIsNotRouted_BecauseTheOrphanEscalationCountsIt()
    {
        var body = Body("async Task<bool> Nudge_Implementer_Async");

        Assert.Contains("ChannelAppender.Append_AppEntry", body, StringComparison.Ordinal);
        Assert.DoesNotContain("AppNote_Writer", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Route_ChannelNote", body, StringComparison.Ordinal);
        Assert.DoesNotContain("routedKind", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// THERE IS NO RESPAWN NOTE TO MOVE. The 2026-09-15 spec's step 3 names "orphan and respawn
    /// notes"; <c>Recover_OrphanedImplementer_Async</c>, the only thing that ever wrote
    /// <c>Nudge_Wording.RESPAWN_SUBJECT</c>, was deleted, and the constant survives only so
    /// <c>Is_WakeSubject</c> keeps recognising the entries already sitting in members' channels.
    ///
    /// <para>
    /// The check is worth keeping after the finding is recorded, because the failure it catches is a
    /// writer coming BACK: a respawn entry appended again, unrouted and unclassified, while the
    /// constant's docstring and the plan both say nothing writes it.
    /// </para>
    /// </summary>
    [Fact]
    public void NothingWritesARespawnNote()
    {
        var writers = 0;

        foreach (var file in SourceTree.EnumerateCSharp("AIOrchestratorCoreLib"))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (!line.Contains("RESPAWN_SUBJECT", StringComparison.Ordinal))
                    continue;

                if (line.Contains("Append_", StringComparison.Ordinal)
                    || line.Contains("AppNote_Writer", StringComparison.Ordinal)
                    || line.Contains("Route_", StringComparison.Ordinal))
                {
                    writers++;
                }
            }
        }

        Assert.Equal(0, writers);
    }

    /// <summary>
    /// AND THE RECOGNISER IS STILL THERE, which is the other half of the same fact. A scan that only
    /// counted writers would be green on the day somebody deleted the constant along with its last
    /// reader, and the respawn entries already in members' channels would stop being recognised as the
    /// app's own noise.
    /// </summary>
    [Fact]
    public void TheRespawnSubjectIsStillRecognisedAsAWakeSubject()
    {
        Assert.True(global::AIOrchestratorCoreLib.Status.Nudge_Wording.Is_WakeSubject(
            global::AIOrchestratorCoreLib.Status.Nudge_Wording.RESPAWN_SUBJECT));
    }

    // ---------------------------------------------------------------------------------------------
    // Task 9 — the contract and question coaching
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// THE FIVE COACHING NOTICES ROUTE, AND THE DEAD QUESTION IS WHY THE PACK SECTION IS NOT OPTIONAL.
    /// "your question was NOT sent to the owner — it is incomplete" is the ONLY trace that a question
    /// died: the owner never saw it, the log line is the operator's and not the agent's, and the
    /// session believes it is waiting for an answer that nobody will ever give. A routed note that the
    /// pack does not carry converts a visible refusal into a silent hang.
    /// </summary>
    [Theory]
    [InlineData("void Coach_OnContractFaults")]
    [InlineData("void Refuse_Question")]
    [InlineData("async Task Supersede_OlderQuestions_Async")]
    [InlineData("void Handle_ReaskOfADecidedQuestion")]
    [InlineData("void Handle_RepeatedQuestion")]
    public void EveryQuestionCoachingSiteRoutes_AndNoneOfThemStillCallsTheAppenderDirectly(string signatureMark)
    {
        var body = Body(signatureMark);

        Assert.Contains("Route_ChannelNote(", body, StringComparison.Ordinal);
        Assert.Contains("AppNoteKinds.ContractCoaching", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ChannelAppender.Append_AppEntry", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// AND THE ADAPTER REACHES THE ROUTER, resolving the role from the CHANNEL. The general
    /// supervisor's channel is discovered as an OWNER channel under the reserved orch id
    /// <c>general</c>; reading it as a supervisor's would build a status-log path under an
    /// orchestration folder that does not exist, so the note would land beside nothing.
    /// </summary>
    [Fact]
    public void TheDiscoveredChannelRoute_GoesThroughTheOneRouter_AndKnowsTheGeneralChannelIsNotASupervisors()
    {
        var body = Body("bool Route_ChannelNote");

        Assert.Contains("AppNote_Writer.Write(", body, StringComparison.Ordinal);
        Assert.Contains("PrintSessionState_Store.Read_OrNull(", body, StringComparison.Ordinal);

        Assert.Contains("GENERAL_ORCH_ID", body, StringComparison.Ordinal);
        Assert.Contains("SessionRoles.General", body, StringComparison.Ordinal);

        // A spoke is its MEMBER's, and a member is not always an implementer — a reviewer and a solo
        // carry their own runner rows, so reading every spoke as an implementer would ask the wrong
        // role's config for the sink.
        Assert.Contains("From_MemberKind", body, StringComparison.Ordinal);
    }
}
