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
/// </para>
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
        return Build_OwnerFileContent(processId, heldSinceUtc, role, Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// With an explicit ownership token. <paramref name="token"/> identifies THIS acquisition, not
    /// this process: a pid is reused and a path is reused, but a token is minted per acquire, which
    /// is what lets release tell our lock from the one a later holder took after ours was broken.
    /// </summary>
    public static string Build_OwnerFileContent(int processId, DateTime heldSinceUtc, string role, string token)
    {
        return $"pid={processId}\nutc={heldSinceUtc:yyyy-MM-ddTHH:mm:ssZ}\nrole={role}\ntoken={token}\n";
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
            var ownershipToken = Try_Acquire(lockDirectory);

            if (ownershipToken != null)
            {
                waited = DateTime.UtcNow - startedUtc;

                try
                {
                    write();
                }
                finally
                {
                    Release_IfStillOurs(lockDirectory, ownershipToken);
                }

                return true;
            }

            Break_IfStale(lockDirectory);

            waited = DateTime.UtcNow - startedUtc;

            if (waited >= budget)
            {
                ChannelLock_Diagnostics.Report(
                    $"Channel lock: could not acquire '{Path.GetFileName(channelFilePath)}' after {waited.TotalMilliseconds:F0} ms — "
                    + $"NOTHING WAS WRITTEN. Held by {Describe_Holder(lockDirectory)}.");

                return false;
            }

            Thread.Sleep(delayMilliseconds);
            delayMilliseconds = Math.Min(delayMilliseconds * 2, RETRY_MAXIMUM_MILLISECONDS);
        }
    }

    /// <summary>
    /// Who holds the lock, for the human reading a wedged channel. Never throws and never returns
    /// nothing useful: "unknown" is itself the diagnosis when the metadata cannot be read.
    /// </summary>
    static string Describe_Holder(string lockDirectory)
    {
        try
        {
            var ownerFile = Path.Combine(lockDirectory, OWNER_FILE_NAME);

            if (!File.Exists(ownerFile))
                return "a lock directory with NO OWNER FILE (a writer killed mid-acquire)";

            return File.ReadAllText(ownerFile).Replace("\n", " ").Trim();
        }
        catch
        {
            return "a holder whose owner file could not be read";
        }
    }

    /// <summary>
    /// Returns the ownership token written into the lock, or null if the lock was not obtained.
    /// The token exists so release can tell OUR lock from a later holder's — see
    /// <see cref="Release_IfStillOurs"/>.
    /// </summary>
    static string? Try_Acquire(string lockDirectory)
    {
        // Fill a uniquely-named directory, then move it into place. Directory.CreateDirectory on
        // the target itself would NOT do: it succeeds when the directory already exists, so two
        // writers would both believe they hold the lock. The move is the exclusive step.
        var stagingDirectory = $"{lockDirectory}.{Guid.NewGuid():N}.staging";
        var ownershipToken = Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            File.WriteAllText(
                Path.Combine(stagingDirectory, OWNER_FILE_NAME),
                Build_OwnerFileContent(Environment.ProcessId, DateTime.UtcNow, "app", ownershipToken));

            Directory.Move(stagingDirectory, lockDirectory);
            return ownershipToken;
        }
        catch
        {
            // Either somebody else holds it, or the move lost the race. Both mean "not mine".
            Delete_BestEffort(stagingDirectory);
            return null;
        }
    }

    /// <summary>
    /// Breaks a lock whose holder looks dead, by RENAMING it aside rather than deleting it.
    /// <para>
    /// The rename is load-bearing: two writers can both decide the same lock is stale, and if both
    /// DELETED it both would then acquire — exactly the collision the protocol exists to prevent.
    /// </para>
    /// <para>
    /// WHAT SERIALISES THE TWO BREAKERS IS THE SOURCE DISAPPEARING, NOT THE DESTINATION. This
    /// comment used to say "only one rename can succeed", and that is FALSE: each breaker renames to
    /// its own <c>.broken.{guid}</c> path, so they never compete for a destination at all. The
    /// second breaker fails because <c>lockDirectory</c> is already gone. The distinction is not
    /// pedantic — it is the whole reason the window below exists, and the wrong version reads as
    /// settled, which is what stopped anyone looking. (rev-6, DEEP review of 6ef7c09, finding F.)
    /// </para>
    /// <para>
    /// THE RESIDUAL WINDOW, stated because it is real: staleness is judged, and then the move
    /// happens. If the holder releases and a NEW writer acquires in between, the move succeeds and
    /// breaks a live lock that is zero seconds old. rev-6 measured 0 natural hits in 400 trials and
    /// reproduced it with a forced scheduling point, so it is narrow and it is not impossible.
    /// There is no compare-and-swap on a directory, so it cannot be closed by ordering alone — it is
    /// instead DETECTED, by carrying the token of the lock that was judged and checking the lock
    /// that was actually broken still carries it. A break that hits the wrong lock is put back and
    /// reported rather than proceeding silently.
    /// </para>
    /// </summary>
    static void Break_IfStale(string lockDirectory)
    {
        if (!Is_Stale(lockDirectory))
            return;

        // Captured BEFORE the move: afterwards the path is gone, and a report that cannot say whose
        // lock was broken is not evidence of anything. The token identifies the acquisition we
        // judged — a pid is reused and a path is reused, but a token is minted per acquire.
        var judgedToken = Read_OwnerField_OrNull(Path.Combine(lockDirectory, OWNER_FILE_NAME), "token");

        Try_BreakStale(lockDirectory, judgedToken);
    }

    /// <summary>
    /// Breaks the lock at <paramref name="lockDirectory"/>, verifying it is still the acquisition
    /// identified by <paramref name="judgedToken"/>, and returns whether it was broken.
    /// <para>
    /// Public as an additive overload because the identity check is the whole substance of the fix
    /// and there is no <c>InternalsVisibleTo</c> here: a rule decided in a private method is
    /// unreachable from a test, and this one has to be provable without racing the scheduler.
    /// Pass the token read from the lock BEFORE judging it stale.
    /// </para>
    /// <para>
    /// A null <paramref name="judgedToken"/> means the lock had no readable token — a bash-created
    /// or half-written lock. The break then proceeds unverified, because refusing would make a
    /// metadata-less lock unbreakable, which is the defect fixed in d39ad14 and must not come back.
    /// </para>
    /// </summary>
    public static bool Try_BreakStale(string lockDirectory, string? judgedToken)
    {
        var holder = Describe_Holder(lockDirectory);
        var brokenPath = $"{lockDirectory}.broken.{Guid.NewGuid():N}";

        try
        {
            // The broken lock is kept, not deleted: a lock that had to be broken is evidence about
            // a writer that died holding it, and that is worth more on disk than a tidy folder.
            Directory.Move(lockDirectory, brokenPath);
        }
        catch
        {
            // Another writer broke it first, or it cleared on its own. Either way it is no longer
            // this caller's problem and the next attempt will find out.
            return false;
        }

        if (Is_TheLockWeJudged(brokenPath, judgedToken))
        {
            ChannelLock_Diagnostics.Report(
                $"Channel lock: broke a stale lock on '{Path.GetFileName(lockDirectory)}' — it was held by {holder} "
                + $"for more than {STALE_SECONDS}s and never released. The broken lock is kept beside the channel.");

            return true;
        }

        // We broke somebody else's LIVE lock: the holder we judged released, and a new writer
        // acquired, between the judgement and the move. Put it back.
        var restored = Restore_AfterBreakingTheWrongLock(brokenPath, lockDirectory);

        // "Broken" means the path is free for the next acquire. A successful restore means it is
        // occupied again by its rightful holder, so nothing was broken.
        return !restored;
    }

    /// <summary>
    /// Whether the directory just broken still carries the token that was judged stale.
    /// <para>
    /// A NULL JUDGED TOKEN — the lock we judged had no readable metadata — verifies nothing and
    /// returns a match, which is what keeps a metadata-less lock breakable (see
    /// <see cref="Try_BreakStale"/>; refusing would revive d39ad14's unbreakable lock).
    /// </para>
    /// <para>
    /// A NULL TOKEN ON THE BROKEN SIDE IS NOT A MATCH, and this used to say it was. That second null
    /// route accepted a lock that is a DIFFERENT acquisition by construction: <c>kit/channel-append.sh</c>
    /// does <c>mkdir "$LOCK_DIR"</c> and writes the owner file on the NEXT line, so a session that has
    /// just acquired holds a real lock with no owner file for that instant. Judged stale, released,
    /// re-acquired by that session, and the break destroyed a live lock while reporting "broke a stale
    /// lock" — a true-sounding line about a writer that was very much alive. Both sides then wrote,
    /// and <c>File.AppendAllText</c> opens deny-write, so one entry was lost. (rev-10, F1 on 106047b.)
    /// </para>
    /// <para>
    /// The metadata-less case never needed it: that returns at the top on the judged token, so this
    /// route was load-bearing for nothing. Treating an unreadable broken side as a mismatch also errs
    /// in the safe direction — a needless restore puts back a lock the next tick judges stale again,
    /// while a needless break destroys one that is live.
    /// </para>
    /// </summary>
    static bool Is_TheLockWeJudged(string brokenPath, string? judgedToken)
    {
        if (judgedToken == null)
            return true;

        return Read_OwnerField_OrNull(Path.Combine(brokenPath, OWNER_FILE_NAME), "token") == judgedToken;
    }

    /// <summary>
    /// Moves a wrongly-broken lock back and reports it, returning whether the restore succeeded.
    /// <para>
    /// Restoring can itself fail — a third writer may have acquired the free path in the interval.
    /// That case is UNRECOVERABLE and is reported as such rather than swallowed: two writers now
    /// believe they hold the channel, and the only thing that helps whoever reads the log is being
    /// told exactly that. The release-by-token check in <see cref="Release_IfStillOurs"/> is what
    /// stops the displaced holder from deleting the newcomer's lock on its way out.
    /// </para>
    /// </summary>
    static bool Restore_AfterBreakingTheWrongLock(string brokenPath, string lockDirectory)
    {
        var channelName = Path.GetFileName(lockDirectory);

        try
        {
            Directory.Move(brokenPath, lockDirectory);

            // Says what was OBSERVED, not what is assumed. The usual cause is a release and a
            // re-acquire in the gap, but an owner file that could not be read reaches here too — and
            // claiming a re-acquire that did not happen would be the confident-wrong-diagnosis
            // failure this subsystem keeps paying for.
            ChannelLock_Diagnostics.Report(
                $"Channel lock: NEAR MISS on '{channelName}' — the lock at that path was NOT the acquisition judged "
                + "stale (it was released and re-acquired in the gap, or its owner file could not be read), so the "
                + "break hit a lock that may be live. It has been put back and no writer lost its lock. This is the "
                + "narrow window documented on Break_IfStale.");

            return true;
        }
        catch
        {
            ChannelLock_Diagnostics.Report(
                $"Channel lock: BROKE A LIVE LOCK on '{channelName}' and COULD NOT PUT IT BACK — the path was taken "
                + $"again in the interval. A live writer has lost its lock; its entry may be written unserialised. The "
                + $"displaced lock is at '{Path.GetFileName(brokenPath)}'.");

            return false;
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
            var heldSinceUtc = Read_UsableHeldSinceUtc_OrNull(ownerFile);

            // ONE condition, not a row of special cases. The metadata is either usable or it is not,
            // and every way of not being usable has the same answer: fall back to the age of the
            // directory, the one clock this process can vouch for.
            //
            // This was three sequential guards that happened to share a recovery, which is not one
            // condition — it is three, and the fourth route gets added beside them by whoever comes
            // next. That is not hypothetical: this defect reached production by four separate routes
            // (absent, unparseable, empty, future-stamped) and each was repaired on its own.
            if (heldSinceUtc == null)
                return Is_DirectoryOlderThanStale(lockDirectory);

            return (DateTime.UtcNow - heldSinceUtc.Value).TotalSeconds > STALE_SECONDS;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The one recovery path for "the owner file cannot be trusted", whatever the reason — absent,
    /// unparseable, or stamped in the future. The directory's own age is the only clock this process
    /// can vouch for, and a live acquire is microseconds old.
    /// </summary>
    static bool Is_DirectoryOlderThanStale(string lockDirectory)
    {
        return (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(lockDirectory)).TotalSeconds > STALE_SECONDS;
    }

    /// <summary>One field out of the owner file, or null. Never throws.</summary>
    static string? Read_OwnerField_OrNull(string ownerFile, string fieldName)
    {
        try
        {
            var prefix = $"{fieldName}=";

            foreach (var line in File.ReadAllLines(ownerFile))
            {
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return line[prefix.Length..].Trim();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The moment the holder took the lock, or null when the metadata cannot be TRUSTED — which is
    /// deliberately one answer covering every reason: the file is missing, it cannot be read, it has
    /// no <c>utc</c> line, the stamp will not parse, or the stamp is in the FUTURE.
    /// <para>
    /// The future case belongs here rather than at the call site because it is not a different kind
    /// of problem. Staleness is <c>now - held</c>, so a future stamp makes that negative, it never
    /// exceeds the threshold, the lock is never stale, and a dead holder wedges the channel forever
    /// — the same outcome as a stamp that will not parse. It is the defect that once rendered "on
    /// task under a minute" for hours: a future stamp turning a duration into a number that means
    /// nothing. There the fix was to refuse to DISPLAY it; here it is to refuse to TRUST it.
    /// </para>
    /// <para>
    /// Null is never "the holder is dead" on its own — a false "dead" breaks a live lock and
    /// corrupts the file the lock was protecting. It means "ask the directory instead".
    /// </para>
    /// </summary>
    static DateTime? Read_UsableHeldSinceUtc_OrNull(string ownerFile)
    {
        if (!File.Exists(ownerFile))
            return null;

        var heldSinceUtc = Read_HeldSinceUtc_OrNull(ownerFile);

        if (heldSinceUtc == null)
            return null;

        if (heldSinceUtc.Value > DateTime.UtcNow)
            return null;

        return heldSinceUtc;
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

    /// <summary>
    /// Releases the lock ONLY if it is still the one this caller took.
    /// <para>
    /// Deleting by path alone was a correctness defect in the core primitive, and the recovery path
    /// armed it: writer A acquires; A overruns STALE_SECONDS; writer B legitimately breaks A's lock
    /// and acquires its own; A then finishes and deletes B's lock while B is mid-write, letting C
    /// acquire alongside B. Every guarantee above this was conditional on that not happening.
    /// </para>
    /// <para>
    /// Honest about what remains: reading the token and deleting are two operations, so a break
    /// landing between them can still cost the new holder its lock. That window is microseconds
    /// against the STALE_SECONDS it takes to become breakable at all, where the old one was the
    /// entire duration of the write.
    /// </para>
    /// </summary>
    static void Release_IfStillOurs(string lockDirectory, string ownershipToken)
    {
        try
        {
            if (!Directory.Exists(lockDirectory))
                return;

            var heldToken = Read_OwnerField_OrNull(Path.Combine(lockDirectory, OWNER_FILE_NAME), "token");

            if (heldToken != ownershipToken)
            {
                ChannelLock_Diagnostics.Report(
                    $"Channel lock: NOT releasing '{Path.GetFileName(lockDirectory)}' — it is no longer the lock this writer took "
                    + "(it was broken as stale and someone else holds it now). The write it protected overran STALE_SECONDS.");

                return;
            }

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
