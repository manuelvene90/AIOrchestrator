using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Running.TurnCursor;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE GATE IS OFF UNTIL SOMEBODY SAYS OTHERWISE, and the one configuration that would make a
/// session deaf is refused rather than trusted to the person editing the file.
///
/// <para>
/// THE REFUSAL IS ASKED OF A SESSION, NOT OF A ROLE. config.json's <c>runners.&lt;role&gt;</c> block
/// describes the role; whether the app opens THIS session's turns — and therefore whether it hands it
/// a state pack — is <see cref="IPrintSessionState.DrivesTurns"/>, a flag on the session's own file.
/// The two disagree in production and the disagreement is not an edge case: a member demoted to a
/// terminal keeps a role whose config still reads <c>print</c>
/// (<c>OrchestrationLauncherModel.Demote_ToTerminal</c>, plan 01 Task 5), and both the dispatcher
/// (<c>PrintTurnDispatcherModel.Consider_Session</c>) and the wake-ticket sweep
/// (<c>BridgeEngineModel.Sweep_WakeTickets_Async</c>) screen on the FLAG for exactly that reason.
/// A policy that read the role alone would send that session's bookkeeping to a log nothing ever
/// reads for it.
/// </para>
/// </summary>
public class BookkeepingSinkConfigTests
{
    static IRunnerConfigs Parse(string json) => RunnerConfigs_Json.Parse(JsonNode.Parse(json) as JsonObject);

    /// <summary>A session's own state file, which is the only thing that knows whether the app opens its turns.</summary>
    static IPrintSessionState Session(bool drivesTurns, SessionRoles role = SessionRoles.Implementer)
    {
        return PrintSessionState_Factory.Create(
            "11111111-1111-1111-1111-111111111111", true, role, "repo-1", "imp-1",
            "/tmp", null, "/tmp/channel.md", new List<ITurnCursor>(), 1, 0, [], null, drivesTurns: drivesTurns);
    }

    // ----- the config key -----

    [Fact]
    public void ARoleThatStatesNothing_KeepsItsBookkeepingInTheChannel()
    {
        Assert.Equal(BookkeepingSinks.Channel, Parse("{}").Get_ForRole(SessionRoles.Supervisor).Bookkeeping);
    }

    [Fact]
    public void TheWordLog_SelectsTheLog_ForThatRoleAlone()
    {
        var configs = Parse("""{"runners":{"supervisor":{"runner":"print","bookkeeping":"log"}}}""");

        Assert.Equal(BookkeepingSinks.Log, configs.Get_ForRole(SessionRoles.Supervisor).Bookkeeping);
        Assert.Equal(BookkeepingSinks.Channel, configs.Get_ForRole(SessionRoles.Implementer).Bookkeeping);
    }

    /// <summary>
    /// AND THE REST OF THE NODE IS STILL READ. Asserting only that the sink is Channel passes for two
    /// reasons — the word fell back, or nothing parses `bookkeeping` at all — and an assertion with
    /// two routes to its state pins neither (CLAUDE.md decision 20).
    /// </summary>
    [Fact]
    public void AnUnknownWord_FallsBackToTheChannel_WithoutCostingTheRestOfTheNode()
    {
        var role = Parse("""{"runners":{"supervisor":{"runner":"print","resume":"fresh","wake":"ticket","bookkeeping":"telepathy"}}}""")
            .Get_ForRole(SessionRoles.Supervisor);

        Assert.Equal(BookkeepingSinks.Channel, role.Bookkeeping);
        Assert.Equal(SessionRunners.Print, role.Runner);
        Assert.Equal(ResumeModes.Fresh, role.Resume);
        Assert.Equal(WakeModes.Ticket, role.Wake);
    }

    /// <summary>
    /// IT SURVIVES A SAVE. <c>RunnerConfigs_Json.Write</c> runs on every save of config.json, and a
    /// key it does not emit is returned to its default by the app's own next write — which is how an
    /// explicit <c>"wake": "ticket"</c> was silently reverted before plan 01's Task 2 fixed it. The
    /// same trap, one key later.
    /// </summary>
    [Fact]
    public void AnExplicitSink_SurvivesASaveAndAReload()
    {
        var parsed = Parse("""{"runners":{"supervisor":{"runner":"print","bookkeeping":"log"}}}""");
        var written = new JsonObject();

        RunnerConfigs_Json.Write(written, parsed);

        var reloaded = RunnerConfigs_Json.Parse(written);

        Assert.Equal(BookkeepingSinks.Log, reloaded.Get_ForRole(SessionRoles.Supervisor).Bookkeeping);
        Assert.Equal(BookkeepingSinks.Channel, reloaded.Get_ForRole(SessionRoles.Implementer).Bookkeeping);
    }

    /// <summary>
    /// THE `bg` BRANCHES CARRY IT TOO. <c>Parse_Role</c> builds the role config in THREE places — the
    /// ordinary branch, the branch where <c>bg</c> is refused and falls back to a terminal, and the
    /// branch where <c>bg</c> is accepted — and a key passed to two of them is dropped on the third
    /// silently. `bg` with settings that do not disable Remote Control is the refused branch
    /// (<c>BgSettings_Rule</c>); `bg` saying nothing else is the accepted one.
    /// </summary>
    [Fact]
    public void EveryBranchOfTheRoleParser_CarriesTheSink()
    {
        var ordinary = Parse("""{"runners":{"implementer":{"runner":"print","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);
        var refused = Parse("""{"runners":{"implementer":{"runner":"bg","settings":"{}","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);
        var accepted = Parse("""{"runners":{"implementer":{"runner":"bg","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);

        Assert.Equal(SessionRunners.Print, ordinary.Runner);
        Assert.Equal(BookkeepingSinks.Log, ordinary.Bookkeeping);

        Assert.Equal(SessionRunners.Terminal, refused.Runner);
        Assert.Equal(BookkeepingSinks.Log, refused.Bookkeeping);

        Assert.Equal(SessionRunners.Bg, accepted.Runner);
        Assert.Equal(BookkeepingSinks.Log, accepted.Bookkeeping);
    }

    // ----- the interlock: the three combinations, and the drift between them -----

    /// <summary>
    /// CASE 1 — the dispatcher opens this session's turns, so it writes it a state pack at every one
    /// of them (<c>PrintTurnExecutorModel</c> → <c>StatePack_Writer.Write_ForSession_OrNull</c>).
    /// A note in the log reaches it.
    /// </summary>
    [Fact]
    public void ABridgeDrivenSession_MayUseTheLog()
    {
        var role = Parse("""{"runners":{"implementer":{"runner":"print","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);

        Assert.Equal(BookkeepingSinks.Log, BookkeepingSink_Policy.Resolve(role, Session(drivesTurns: true)));
    }

    /// <summary>
    /// CASE 2 — a terminal session the app tickets. The sweep writes its pack before the ticket
    /// (<c>BridgeEngineModel.Sweep_WakeTickets_Async</c>), so a note in the log reaches it too.
    /// </summary>
    [Fact]
    public void ATerminalTicketSession_MayUseTheLog()
    {
        var role = Parse("""{"runners":{"implementer":{"runner":"terminal","wake":"ticket","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);

        Assert.Equal(BookkeepingSinks.Log, BookkeepingSink_Policy.Resolve(role, Session(drivesTurns: false)));
    }

    /// <summary>
    /// CASE 3, THE INTERLOCK. A terminal session on the fingerprint watcher gets no state pack and no
    /// riding notes — its only knowledge of anything is its channel file (2026-09-15 spec, step 3:
    /// "safe only after step 2"). Honouring <c>log</c> there would put the ledger advisory, the orphan
    /// report and the "your question was NOT sent" notice in a file nothing ever reads, and the
    /// session would look exactly like one that had nothing to be told.
    /// </summary>
    [Fact]
    public void ATerminalWatcherSession_IsRefusedTheLog_EvenWhenTheConfigAsksForIt()
    {
        var role = Parse("""{"runners":{"implementer":{"runner":"terminal","wake":"watcher","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);

        Assert.Equal(BookkeepingSinks.Log, role.Bookkeeping);
        Assert.Equal(BookkeepingSinks.Channel, BookkeepingSink_Policy.Resolve(role, Session(drivesTurns: false)));
    }

    /// <summary>
    /// THE DRIFT, AND IT IS THE REASON THE POLICY TAKES THE SESSION. The role still reads
    /// <c>print</c> in config.json — so the ticket sweep, which only ever looks at TERMINAL roles,
    /// skips it — while the member's own file says the dispatcher no longer runs its turns, so the
    /// dispatcher skips it too. Nobody writes that session a pack. A policy asked only about the role
    /// would answer <c>Log</c> here and the session would go deaf; this is the defect that bit Task 5
    /// of plan 01, one layer up.
    /// </summary>
    [Fact]
    public void ARoleThatStillReadsPrint_CannotSpeakForASessionTheDispatcherHasLetGo()
    {
        var role = Parse("""{"runners":{"implementer":{"runner":"print","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);

        Assert.Equal(SessionRunners.Print, role.Runner);
        Assert.Equal(WakeModes.Watcher, role.Wake);
        Assert.Equal(BookkeepingSinks.Channel, BookkeepingSink_Policy.Resolve(role, Session(drivesTurns: false)));
    }

    /// <summary>
    /// AND THE OTHER WAY ROUND, which is what proves the policy READS the flag rather than distrusting
    /// the word <c>print</c>: the role says terminal-on-the-watcher, the session's own file says the
    /// dispatcher drives it, and the dispatcher is what writes the pack. Same two inputs, opposite
    /// answer.
    /// </summary>
    [Fact]
    public void ARoleThatReadsTerminalWatcher_DoesNotSilenceASessionTheDispatcherStillDrives()
    {
        var role = Parse("""{"runners":{"implementer":{"runner":"terminal","wake":"watcher","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);

        Assert.Equal(BookkeepingSinks.Log, BookkeepingSink_Policy.Resolve(role, Session(drivesTurns: true)));
    }

    /// <summary>
    /// NO STATE FILE, NO ANSWER, SO NO LOG. A session with no state file has no cursors and no pack
    /// written for it by anything; "I could not find out" is not "it is safe".
    /// </summary>
    [Fact]
    public void ASessionWithNoStateFile_IsRefusedTheLog()
    {
        var role = Parse("""{"runners":{"implementer":{"runner":"print","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);

        Assert.Equal(BookkeepingSinks.Channel, BookkeepingSink_Policy.Resolve(role, null));
    }

    /// <summary>
    /// THE POLICY ONLY EVER SUBTRACTS. It can refuse a stated <c>log</c>; it can never turn a stated
    /// <c>channel</c> into one, whatever the session looks like — without this the cases above would
    /// pass on an implementation that ignored the config key entirely.
    /// </summary>
    [Fact]
    public void AStatedChannel_IsNeverPromotedToTheLog()
    {
        var role = Parse("""{"runners":{"implementer":{"runner":"print","bookkeeping":"channel"}}}""")
            .Get_ForRole(SessionRoles.Implementer);

        Assert.Equal(BookkeepingSinks.Channel, BookkeepingSink_Policy.Resolve(role, Session(drivesTurns: true)));
        Assert.Equal(BookkeepingSinks.Channel, BookkeepingSink_Policy.Resolve(role, Session(drivesTurns: false)));
        Assert.Equal(BookkeepingSinks.Channel, BookkeepingSink_Policy.Resolve(role, null));
    }

    // ----- the refusal says which reason it is, or says nothing at all -----

    /// <summary>
    /// A REFUSAL THAT NAMES ITS REASON, and NULL when there was none — so the router cannot log a
    /// refusal that did not happen, and the two refusals cannot be told apart only by guesswork.
    /// </summary>
    [Fact]
    public void TheRefusalNamesItsReason_AndIsAbsentWhenNothingWasRefused()
    {
        var log = Parse("""{"runners":{"implementer":{"runner":"terminal","wake":"watcher","bookkeeping":"log"}}}""")
            .Get_ForRole(SessionRoles.Implementer);
        var channel = Parse("{}").Get_ForRole(SessionRoles.Implementer);

        var watcherRefusal = BookkeepingSink_Policy.Describe_Refusal_OrNull(SessionRoles.Implementer, log, Session(drivesTurns: false));
        var unreachableRefusal = BookkeepingSink_Policy.Describe_Refusal_OrNull(SessionRoles.Implementer, log, null);

        Assert.Contains("watcher", watcherRefusal);
        Assert.Contains("no state file", unreachableRefusal);
        Assert.NotEqual(watcherRefusal, unreachableRefusal);

        Assert.Null(BookkeepingSink_Policy.Describe_Refusal_OrNull(SessionRoles.Implementer, log, Session(drivesTurns: true)));
        Assert.Null(BookkeepingSink_Policy.Describe_Refusal_OrNull(SessionRoles.Implementer, channel, null));
    }

    // ----- the words -----

    [Fact]
    public void EverySinkHasAWordAndEveryWordRoundTrips()
    {
        foreach (var sink in Enum.GetValues<BookkeepingSinks>())
            Assert.Equal(sink, BookkeepingSink_Names.Parse_OrNull(BookkeepingSink_Names.Get_Word(sink)));

        Assert.Equal(BookkeepingSinks.Log, BookkeepingSink_Names.Parse_OrNull("  LOG "));
        Assert.Null(BookkeepingSink_Names.Parse_OrNull(null));
        Assert.Null(BookkeepingSink_Names.Parse_OrNull("telepathy"));
    }
}
