using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// Writes the PLAN.md skeleton when an orchestration is created, so the ledger EXISTS from minute
/// one (the card shows a bar, /progress answers something) and whoever runs the orchestration only
/// has to fill it in. Never overwrites an existing plan.
///
/// THAT LAST SENTENCE IS LOAD-BEARING for everything this file says: the seed text is permanent. It
/// is written once, nothing re-seeds it, and promotion does not touch PLAN.md — so every word here
/// has to stay true for an orchestration that later changes shape.
/// </summary>
public static class PlanSeed_Writer
{
    public static void Ensure_Exists(ISupervisionPaths paths, string orchId, string repoName)
    {
        try
        {
            var planFile = paths.Get_PlanFile(orchId);

            if (File.Exists(planFile))
                return;

            // WHOSE FILE IT IS, said by MODE rather than by role. It read "maintained by the
            // SUPERVISOR" for every orchestration, basic ones included — so a solo session was told,
            // in the first line of its own ledger, that the file belonged to a role that does not
            // exist in its mode. A session that correctly follows what it is told concludes the file
            // is not its business, and the owner watches a frozen bar and asks whether we are stuck.
            //
            // NOT a mode parameter, deliberately, though both callers know their mode: this file is
            // written once and never overwritten, and nothing re-seeds it — not even promotion. Text
            // chosen from the mode would be correct until the orchestration gained a supervisor and
            // permanently wrong afterwards, which is the same class of defect as the one being fixed.
            // One sentence naming both is true in every state the file can reach.
            //
            // The legend is BUILT from PlanLedger_Markers rather than restated. This copy is one of
            // the five that drifted, and it drifted twice: it lost `- [-]` entirely and narrowed
            // `- [!]` to "blocked on owner", leaving a supervisor blocked on anything else with no
            // marker to use.
            var seed =
                $"""
                # PLAN — {repoName} ({orchId})

                Task ledger maintained by whoever runs this orchestration — the SOLO session in a basic
                one, the SUPERVISOR in a crew. One task per line:
                {PlanLedger_Markers.Describe_Legend()}.
                `- [x]` means finished, tested, diff read and evidence shown, with only the owner's merge
                left — it is NOT a claim that anybody reviewed it.
                The app reads this file for the card's progress bar and the owner's /progress command,
                so update it at every boundary.

                - [>] agree the direction with the owner, then replace this line with the real tasks

                ## PARKED — found, not asked for

                Problems found while working that the owner did NOT ask for. One line each, plain
                bullets, who found it and when. They are NOT counted by the bar and never become work
                without the owner — the endeavour is what they asked for (owner directive, 2026-08-14).

                """;

            File.WriteAllText(planFile, seed);
        }
        catch
        {
            // A missing seed only means the bar appears once the supervisor writes the file itself.
        }
    }
}
