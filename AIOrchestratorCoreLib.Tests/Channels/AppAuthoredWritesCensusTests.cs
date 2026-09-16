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
    /// another branch, was caught by a merge instead of discovered in production. **The new site is
    /// NOT YET CLASSIFIED into a bucket** — plan 02 task 4 routes it, and raising this number without
    /// saying so would be exactly the silence the census exists to prevent.
    /// </para>
    /// </summary>
    const int EXPECTED_EVENT_SITES = 78;

    const int HELPER_BODIES = 5;

    static readonly string[] APPEND_NAMES =
    [
        "ChannelAppender.Append_AppEntry(",
        "Append_GeneralAppEntry(",
        "Append_OrchestrationAppEntry(",
        "Append_AppEntry_Safe(",
        "Append_SupervisorAttention_UnlessMeeting(",
        "Announce(",
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
