using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// Makes an omitted ledger update VISIBLE, which is the whole difference between this artifact and
/// every other step in the protocol: a missed channel entry blocks someone, a missed watcher stalls
/// the orchestration, a missed style check blocks turn-end — a missed PLAN.md update produced no
/// signal at all until the owner happened to read the bar.
///
/// The rule is mechanical: once the supervisor posts a VERDICT into an implementer's channel, the
/// ledger must be touched too. If it is not, the orchestration is flagged BEHIND — for the owner,
/// for the card, and for the turn-end hook that blocks the supervisor.
/// </summary>
public static class LedgerHealth_Tracker
{
    /// <summary>Grace after a verdict before the ledger counts as behind (the supervisor is mid-turn).</summary>
    public const int LEDGER_GRACE_SECONDS = 90;

    /// <summary>The flag the turn-end hook reads. Present = this supervisor owes a ledger update.</summary>
    public static string Build_FlagFilePath(ISupervisionPaths paths, string orchId)
    {
        return Path.Combine(paths.Get_OrchestrationFolder(orchId), ".ledger-behind");
    }

    /// <summary>
    /// True when a supervisor verdict is newer than the ledger. Verdict time is supplied by the
    /// caller (the bridge observes verdicts as they are appended), so this stays a pure comparison.
    /// </summary>
    public static bool Is_LedgerBehind(ISupervisionPaths paths, string orchId, DateTime? lastVerdictUtc)
    {
        if (lastVerdictUtc == null)
            return false;

        if ((DateTime.UtcNow - lastVerdictUtc.Value).TotalSeconds < LEDGER_GRACE_SECONDS)
            return false;

        var planFile = paths.Get_PlanFile(orchId);

        if (!File.Exists(planFile))
            return true;

        return File.GetLastWriteTimeUtc(planFile) < lastVerdictUtc.Value;
    }

    /// <summary>Raises or clears the flag the hook reads; returns whether it is now raised.</summary>
    public static bool Sync_Flag(ISupervisionPaths paths, string orchId, bool isBehind)
    {
        var flagFile = Build_FlagFilePath(paths, orchId);

        try
        {
            if (isBehind)
            {
                if (!File.Exists(flagFile))
                    File.WriteAllText(flagFile, "The task ledger (PLAN.md) is behind the verdicts posted to implementer channels.");

                return true;
            }

            if (File.Exists(flagFile))
                File.Delete(flagFile);

            return false;
        }
        catch
        {
            // A flag we cannot write only costs enforcement, never correctness.
            return isBehind;
        }
    }
}
