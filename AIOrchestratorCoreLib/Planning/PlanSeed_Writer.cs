using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// Writes the PLAN.md skeleton when an orchestration is created, so the ledger EXISTS from minute
/// one (the card shows a bar, /progress answers something) and the supervisor only has to fill it
/// in. Never overwrites an existing plan.
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

            var seed =
                $"""
                # PLAN — {repoName} ({orchId})

                Task ledger maintained by the SUPERVISOR. One task per line:
                `- [ ]` open · `- [>]` in progress · `- [x]` done · `- [!]` blocked on owner.
                The app reads this file for the card's progress bar and the owner's /progress command,
                so update it at every boundary.

                - [>] agree the direction with the owner, then replace this line with the real tasks

                """;

            File.WriteAllText(planFile, seed);
        }
        catch
        {
            // A missing seed only means the bar appears once the supervisor writes the file itself.
        }
    }
}
