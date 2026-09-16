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
    const int EXPECTED_EVENT_SITES = 78;

    /// <summary>
    /// SIX since 2026-09-16: <c>AppNote_Writer.Write</c> (plan 02 task 4) is the sixth pass-through,
    /// and its <c>ChannelAppender.Append_AppEntry</c> is the router's own channel branch, not a place
    /// the app decides to say something. <c>AppNote_Writer.Write(</c> is in
    /// <see cref="APPEND_NAMES"/> for the same reason the four wrappers are: tasks 5-9 move sites onto
    /// it, and a site that changed which helper it calls must not read here as a site that disappeared.
    /// </summary>
    const int HELPER_BODIES = 6;

    static readonly string[] APPEND_NAMES =
    [
        "ChannelAppender.Append_AppEntry(",
        "Append_GeneralAppEntry(",
        "Append_OrchestrationAppEntry(",
        "Append_AppEntry_Safe(",
        "Append_SupervisorAttention_UnlessMeeting(",
        "Announce(",
        "AppNote_Writer.Write(",
    ];

    [Fact]
    public void EveryPlaceTheAppWritesAChannelEntry_IsOneOfTheClassifiedSeventySeven()
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
                // deleted.
                if (Regex.IsMatch(line, @"^\s*(?:bool|void|static|async|public|private|internal)\b.*\b(?:Append_\w+|Announce)\s*\("))
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
