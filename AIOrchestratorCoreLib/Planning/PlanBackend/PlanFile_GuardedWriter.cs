using AIOrchestratorCoreLib.Storage;

namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>
/// Writes PLAN.md ONLY IF NOBODY ELSE HAS since the caller read it.
///
/// <para>
/// This is the only code in the app that rewrites an existing PLAN.md — the seed writer creates one and
/// never touches it again, and every other reader is a reader. The session that owns the file edits it
/// continuously, and a whole-file rewrite has no idea what it is discarding: a supervisor's save landing
/// between the read and the write loses its `[x]` marks silently, the owner's bar goes backwards, and
/// the movement notice announces those lines a second time when they are re-typed.
/// </para>
/// <para>
/// It is NOT a lock, and does not pretend to be: the window between this check and the rename is real,
/// just very small. What it removes is the case that actually happens — a read taken a whole tick
/// earlier, written over an edit made in between. A file that moved is left alone and the caller retries
/// on the next pass, which costs a minute and nothing else.
/// </para>
/// <para>
/// It returns the resulting mtime because that stamp is load-bearing elsewhere:
/// <see cref="LedgerHealth_Tracker.Is_LedgerBehind"/> compares mtimes, so the app must be able to
/// recognise its own write and refuse to let it pay the supervisor's ledger debt.
/// </para>
/// </summary>
public static class PlanFile_GuardedWriter
{
    /// <summary>The plan file's new last-write time, or null when somebody else got there first.</summary>
    public static DateTime? Write_IfUnchanged(string planFile, DateTime stampAtRead, string planText)
    {
        if (!File.Exists(planFile) || File.GetLastWriteTimeUtc(planFile) != stampAtRead)
            return null;

        Atomic_FileWriter.Write_AllText(planFile, planText);

        return File.GetLastWriteTimeUtc(planFile);
    }
}
