using System.Text.RegularExpressions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE LIST OF PLACES THE APP WRITES INTO A CHANNEL, and it is the list itself.
///
/// <para>
/// The 2026-09-15 one-wake-model plan 02 classified every one of these into "bureaucracy that leaves
/// the channel" and "conversation that stays". A classification in a document is not a check: the
/// failure this file exists to catch is a SEVENTH ledger advisory, or a fourth question-coaching
/// notice, added next year straight onto <c>ChannelAppender</c> and never routed — which puts the
/// line back in every session's boot read with nothing saying so.
/// </para>
/// <para>
/// A COUNT AND NOT A WHITELIST OF LINE NUMBERS: line numbers move on every edit and a test that has
/// to be re-baselined on every edit is a test nobody reads. When this fails, read the plan's
/// classification table, decide which bucket the new site is in, route it or state why it stays, and
/// move the number here in the same commit.
/// </para>
/// </summary>
public class AppAuthoredWritesCensusTests
{
    /// <summary>
    /// Counted 2026-09-15 on `feat/one-wake-model`. Raw hits of the five append names plus the
    /// announcement queue, MINUS the five that are the bodies of the pass-through helpers
    /// (Append_GeneralAppEntry, Append_OrchestrationAppEntry, Append_AppEntry_Safe, the
    /// Append_SupervisorAttention_UnlessMeeting choke point, Drain_PendingAnnouncements).
    /// </summary>
    /// <summary>
    /// 77 when this census was written (2026-09-15). 78 since 2026-09-16, when
    /// <c>FreshSupervisor_Gate</c> arrived on a branch this census could not see and filed a notice of
    /// its own — the refusal to make a supervisor fresh while its conclusions file is empty.
    ///
    /// <para>
    /// THAT IS THIS TEST WORKING, not a number in the way. A write site added by another agent, on
    /// another branch, was caught by a merge instead of discovered in production.
    /// </para>
    /// <para>
    /// IT IS NOW CLASSIFIED, and it is BUCKET G — conversation, and it STAYS in the channel (plan 02
    /// task 4, 2026-09-16). Two reasons, and the first is mechanical. The gate answers "have I already
    /// said this" by READING THE NOTICE BACK OUT OF THE CHANNEL
    /// (<c>FreshSupervisor_Gate.Has_AlreadySaidIt</c>, over live + archive), because its in-process set
    /// is lost at every bridge restart; routing the entry to the log without moving that reader with it
    /// would re-file the notice at every boot — a waterfall with a longer period, which its own comment
    /// names as the thing it must not become. That is brief constraint 5: the entry IS the durable
    /// trace of the event, and nothing else records it. The second reason is what it says: it is not a
    /// receipt for work that already happened but an instruction to the supervisor to write a file,
    /// once per orchestration, which is conversation in the shape bucket G describes — and at one entry
    /// per orchestration for ever it contributes nothing to the boot-read volume this series exists to
    /// cut.
    /// </para>
    /// </summary>
    /// <summary>
    /// RECONCILED 2026-09-15 (plan 02 task 12), because three documents carried two numbers. The
    /// plan's prose still says 77 in the places written before <c>FreshSupervisor_Gate</c> arrived;
    /// <c>AppNoteKinds</c> and <c>AppNote_Writer</c> say 78 and are right. THIS FILE IS THE
    /// AUTHORITY and it was re-counted by hand as well as run: 86 hits of <see cref="APPEND_NAMES"/>
    /// across <c>AIOrchestratorCoreLib</c>, minus the 8 helper bodies, is 78 event sites.
    ///
    /// <para>
    /// THE SPLIT, for the record, since tasks 5-9 changed which helper a site calls and not how many
    /// sites there are. 17 of the 78 reach <see cref="StatusLog.AppNote_Writer"/>: 8 in
    /// <c>PrintTurnDispatcherModel</c> call it directly, 5 go through
    /// <c>BridgeEngineModel.Route_ChannelNote</c>, and 4 pass a <c>routedKind</c> to
    /// <c>Append_SupervisorAttention_UnlessMeeting</c> (three ledger advisories and the orphan
    /// report). The other 61 still call an appender directly. 17 + 61 = 78.
    /// </para>
    /// </summary>
    /// <summary>
    /// 80 SINCE 2026-09-17 (plan 03 tasks 8 and 9, the routed report). The routed-report sweep added
    /// exactly two write sites and both are BUCKET G — conversation, and both STAY in the channel.
    ///
    /// <para>
    /// The RELAY (<c>ChannelAppender.Append_AppEntry</c> into a reviewer's own channel) is the entry
    /// the reviewer is expected to ANSWER: it carries its supervisor's brief and the delta, and a
    /// re-review is written against it. Routing that to the log would delete the round. It is also the
    /// one entry in the system whose absence is silent, which is why the contract only moves to
    /// <c>Routed</c> when this returns true.
    /// </para>
    /// <para>
    /// The CAP ALERT (through <c>Append_SupervisorAttention_UnlessMeeting</c>) tells a supervisor that
    /// a re-review it was promised never came back and that the round is its own again — an
    /// instruction about what to do next, not a receipt for something that already happened. At one
    /// entry per expired contract it adds nothing to the boot-read volume this series exists to cut.
    /// </para>
    /// </summary>
    /// <para>
    /// EIGHTY-TWO since 2026-09-18: <c>BridgeEngineModel.Notice_DeferredRequest</c> tells a spawning
    /// request's own channel that it is parked under a dispatch pause — one general site for a
    /// start-orchestration, one orchestration site for add-implementer / promote — once per file per
    /// pause. Before it, a request filed under a pause sat with no line anywhere (2026-09-18, 63 min).
    /// </para>
    /// <para>
    /// EIGHTY-SIX since 2026-09-18 (the lever on the dispatch pause): the general channel is told
    /// when an agent's <c>clear-dispatch-pause</c> request is filed as ASKED or MOOT, when the owner
    /// LIFTS the pause (or declares an account change with nothing paused), and when the owner KEEPS
    /// it — four sites, all agent-audience, all in the general channel where the asker reads.
    /// </para>
    const int EXPECTED_EVENT_SITES = 86;

    /// <summary>
    /// SIX since 2026-09-16: <c>AppNote_Writer.Write</c> (plan 02 task 4) is the sixth pass-through,
    /// and its <c>ChannelAppender.Append_AppEntry</c> is the router's own channel branch, not a place
    /// the app decides to say something. <c>AppNote_Writer.Write(</c> is in
    /// <see cref="APPEND_NAMES"/> for the same reason the four wrappers are: tasks 5-9 move sites onto
    /// it, and a site that changed which helper it calls must not read here as a site that disappeared.
    ///
    /// <para>
    /// EIGHT since 2026-09-16 (plan 02 tasks 7-9), and the two additions are both bodies rather than
    /// sites: <c>BridgeEngineModel.Route_SupervisorNote</c> and <c>BridgeEngineModel.Route_ChannelNote</c>
    /// each call the router once, and neither is a place the app decides to say anything — they are the
    /// engine's two adapters onto <c>AppNote_Writer</c>, one for a channel resolved from a role and one
    /// for a channel the mirror discovered. <c>EXPECTED_EVENT_SITES</c> did NOT move with them, which is
    /// the arithmetic those tasks have to satisfy: nine sites changed which helper they call and not one
    /// stopped being a site.
    /// </para>
    /// </summary>
    const int HELPER_BODIES = 8;

    static readonly string[] APPEND_NAMES =
    [
        "ChannelAppender.Append_AppEntry(",
        "Append_GeneralAppEntry(",
        "Append_OrchestrationAppEntry(",
        "Append_AppEntry_Safe(",
        "Append_SupervisorAttention_UnlessMeeting(",
        "Announce(",
        "AppNote_Writer.Write(",

        // THE FIVE QUESTION-COACHING SITES CALL THIS AND NOTHING ELSE (plan 02 task 9). Leaving it out
        // would have dropped the count by five the moment they were routed, and a register that reads a
        // MOVE as a disappearance is a register that gets its constant edited instead of read.
        // Route_SupervisorNote deliberately is NOT here: its callers reach it through
        // Append_SupervisorAttention_UnlessMeeting, which is already in this list, so they are counted
        // once where they always were.
        "Route_ChannelNote(",
    ];

    [Fact]
    public void EveryPlaceTheAppWritesAChannelEntry_IsOneOfTheClassifiedSeventyEight()
    {
        var hits = 0;

        foreach (var file in SourceTree.EnumerateCSharp("AIOrchestratorCoreLib"))
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                // A DECLARATION IS NOT A CALL. The four wrappers declare themselves with these very
                // names; counting a declaration would make the total drift by exactly the number of
                // wrappers every time one is renamed, which is the kind of noise that gets a register
                // deleted. `Route_\w+` joined the alternation on 2026-09-16 with the engine's two
                // router adapters, for exactly that reason and no other: `bool Route_ChannelNote(` is
                // where the helper is written, not a sixth place the app coaches a session.
                if (Regex.IsMatch(line, @"^\s*(?:bool|void|static|async|public|private|internal)\b.*\b(?:Append_\w+|Announce|Route_\w+)\s*\("))
                    continue;

                foreach (var name in APPEND_NAMES)
                {
                    if (line.Contains(name, StringComparison.Ordinal))
                    {
                        hits++;
                        break;
                    }
                }
            }
        }

        Assert.Equal(EXPECTED_EVENT_SITES + HELPER_BODIES, hits);
    }
}
