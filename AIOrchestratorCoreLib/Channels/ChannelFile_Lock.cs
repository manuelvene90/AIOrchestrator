namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// The cross-process half of the channel append protocol: a lock every writer takes before it
/// touches a channel file, whether that writer is this app or an agent session.
/// <para>
/// The primitive is a lock DIRECTORY beside the channel, because creating a directory is the one
/// exclusive-create both sides can perform without either emulating the other. bash does it with
/// <c>mkdir</c>, a single atomic syscall that fails when the target exists; .NET has no exclusive
/// <c>CreateDirectory</c>, so this fills a uniquely-named directory and MOVES it into place — a
/// move onto an existing directory fails, which is the same exclusivity by a different route.
/// <c>flock</c> was rejected deliberately: msys <c>flock</c> and Windows <c>LockFileEx</c> are
/// different mechanisms, and assuming msys/Windows equivalence is exactly how this repo has
/// already produced silent failures in both directions.
/// </para>
/// <para>
/// NOT REENTRANT, and it cannot be: the holder is a directory on disk, not a thread identity, so
/// nothing distinguishes "this caller again" from "somebody else". A nested acquire on the same
/// channel does not deadlock outright — it burns its whole budget and returns false — but the
/// symptom is a mysterious failed write, so take the lock at ONE level and call unlocked helpers
/// inside it. <c>ChannelAppender</c> locks internally; do not wrap it in another acquire.
/// <para>
/// WHAT IT GUARANTEES, exactly: writers that TAKE this lock are serialised against every other
/// writer that takes it, including across processes. It cannot bind a writer that does not ask —
/// a session is an agent running arbitrary commands and no filesystem location is beyond its
/// reach. So "channel appends are atomic" remains false; "writers using the protocol cannot
/// collide with each other" is the true statement, and the one to put in a comment.
/// </para>
/// </summary>
public static class ChannelFile_Lock
{
    /// <summary>Suffix of the lock directory, appended to the channel file's own name.</summary>
    public const string LOCK_DIRECTORY_SUFFIX = ".lock";

    /// <summary>Names the holder, for a human staring at a lock that will not clear.</summary>
    public const string OWNER_FILE_NAME = "owner";

    /// <summary>
    /// How old a lock must be before another writer may break it.
    /// <para>
    /// THIS NUMBER IS A GUESS and should be treated as one. It is a bet that no honest writer stays
    /// inside the critical section for a minute — the section is a read, an index scan and one
    /// append, so a minute is orders of magnitude of headroom. What would invalidate it is a writer
    /// doing something slow while holding the lock, which is precisely why nothing slow belongs in
    /// there. Conservative and reviewable beats tuned and quiet.
    /// </para>
    /// </summary>
    public const int STALE_SECONDS = 60;

    const int RETRY_INITIAL_MILLISECONDS = 50;
    const int RETRY_MAXIMUM_MILLISECONDS = 400;

    /// <summary>Where the lock for <paramref name="channelFilePath"/> lives.</summary>
    public static string Build_LockDirectoryPath(string channelFilePath)
    {
        return $"{channelFilePath}{LOCK_DIRECTORY_SUFFIX}";
    }

    /// <summary>
    /// The owner file's contents. Public because it IS the cross-language contract: the bash helper
    /// parses this exact shape, so a test proving the two sides agree has to be able to produce it
    /// from the production code rather than restating the format and proving only that the author
    /// can copy a string twice.
    /// </summary>
    public static string Build_OwnerFileContent(int processId, DateTime heldSinceUtc, string role)
    {
        return $"pid={processId}\nutc={heldSinceUtc:yyyy-MM-ddTHH:mm:ssZ}\nrole={role}\n";
    }

    /// <summary>
    /// Runs <paramref name="write"/> holding the channel's cross-process lock, and returns whether
    /// the lock was obtained. Returns false — WITHOUT running the write — when the budget expires
    /// while another writer holds it.
    /// <para>
    /// False must never be treated as "write anyway": an unlocked append under contention is the
    /// collision this exists to prevent, and doing it silently would make the protocol a
    /// decoration. The caller's job is to retry or to report, never to bypass.
    /// </para>
    /// <paramref name="waited"/> reports how long was spent trying, so the caller can log a
    /// contended channel rather than merely a failed one.
    /// </summary>
    public static bool Try_Run_WithLock(string channelFilePath, TimeSpan budget, Action write, out TimeSpan waited)
    {
        var lockDirectory = Build_LockDirectoryPath(channelFilePath);
        var startedUtc = DateTime.UtcNow;
        var delayMilliseconds = RETRY_INITIAL_MILLISECONDS;

        while (true)
        {
            if (Try_Acquire(lockDirectory))
            {
                waited = DateTime.UtcNow - startedUtc;

                try
                {
                    write();
                }
                finally
                {
                    Release_BestEffort(lockDirectory);
                }

                return true;
            }

            Break_IfStale(lockDirectory);

            waited = DateTime.UtcNow - startedUtc;

            if (waited >= budget)
                return false;

            Thread.Sleep(delayMilliseconds);
            delayMilliseconds = Math.Min(delayMilliseconds * 2, RETRY_MAXIMUM_MILLISECONDS);
        }
    }

    static bool Try_Acquire(string lockDirectory)
    {
        // Fill a uniquely-named directory, then move it into place. Directory.CreateDirectory on
        // the target itself would NOT do: it succeeds when the directory already exists, so two
        // writers would both believe they hold the lock. The move is the exclusive step.
        var stagingDirectory = $"{lockDirectory}.{Guid.NewGuid():N}.staging";

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            File.WriteAllText(
                Path.Combine(stagingDirectory, OWNER_FILE_NAME),
                Build_OwnerFileContent(Environment.ProcessId, DateTime.UtcNow, "app"));

            Directory.Move(stagingDirectory, lockDirectory);
            return true;
        }
        catch
        {
            // Either somebody else holds it, or the move lost the race. Both mean "not mine".
            Delete_BestEffort(stagingDirectory);
            return false;
        }
    }

    /// <summary>
    /// Breaks a lock whose holder looks dead, by RENAMING it aside rather than deleting it.
    /// <para>
    /// The rename is load-bearing. Two writers can both decide the same lock is stale; if both
    /// delete it, both then acquire and the protocol has produced exactly the collision it exists
    /// to prevent. Only one rename can succeed, so only one breaker wins and the other simply
    /// finds the lock gone and races for it normally.
    /// </para>
    /// </summary>
    static void Break_IfStale(string lockDirectory)
    {
        if (!Is_Stale(lockDirectory))
            return;

        try
        {
            // The broken lock is kept, not deleted: a lock that had to be broken is evidence about
            // a writer that died holding it, and that is worth more on disk than a tidy folder.
            Directory.Move(lockDirectory, $"{lockDirectory}.broken.{Guid.NewGuid():N}");
        }
        catch
        {
            // Another writer broke it first, or it cleared on its own. Either way it is no longer
            // this caller's problem and the next attempt will find out.
        }
    }

    static bool Is_Stale(string lockDirectory)
    {
        var ownerFile = Path.Combine(lockDirectory, OWNER_FILE_NAME);

        try
        {
            if (!Directory.Exists(lockDirectory))
                return false;

            // A lock directory with no owner file is USUALLY a writer part-way through acquiring,
            // and breaking one microseconds old would be worse than waiting. But "no metadata ever
            // counts as stale" made the state permanently unbreakable by BOTH sides, and the bash
            // helper can create it: it mkdirs the lock and then writes the metadata, so a hard kill
            // in between — the app tree-kills every session on exit, which does not run bash's EXIT
            // trap — abandons an empty directory that wedges the channel forever.
            //
            // So fall back to the DIRECTORY's own age. A live acquire is microseconds old; anything
            // older than the same STALE_SECONDS had a holder that is not coming back.
            if (!File.Exists(ownerFile))
                return (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(lockDirectory)).TotalSeconds > STALE_SECONDS;

            var heldSinceUtc = Read_HeldSinceUtc_OrNull(ownerFile);

            // Unreadable or unparseable metadata means the same thing: this code cannot show the
            // holder is dead, so it does not get to claim that it is. A false "alive" costs a wait;
            // a false "dead" breaks a live lock and corrupts the file the lock was protecting.
            if (heldSinceUtc == null)
                return false;

            return (DateTime.UtcNow - heldSinceUtc.Value).TotalSeconds > STALE_SECONDS;
        }
        catch
        {
            return false;
        }
    }

    static DateTime? Read_HeldSinceUtc_OrNull(string ownerFile)
    {
        foreach (var line in File.ReadAllLines(ownerFile))
        {
            if (!line.StartsWith("utc=", StringComparison.OrdinalIgnoreCase))
                continue;

            var stamp = line["utc=".Length..].Trim();

            if (DateTime.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var parsed))
                return parsed;

            return null;
        }

        return null;
    }

    static void Release_BestEffort(string lockDirectory)
    {
        try
        {
            if (Directory.Exists(lockDirectory))
                Directory.Delete(lockDirectory, recursive: true);
        }
        catch
        {
            // A lock that cannot be released will be broken as stale by the next writer after
            // STALE_SECONDS. Throwing here would replace the caller's real exception — if the write
            // failed, that is the one worth surfacing — with a cleanup detail.
        }
    }

    static void Delete_BestEffort(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Debris beside the channel is a diagnostic, never damage.
        }
    }
}
