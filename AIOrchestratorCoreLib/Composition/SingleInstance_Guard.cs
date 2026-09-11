using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Composition;

/// <summary>
/// One running host per supervision root. The WPF app used a named mutex, which exists only on
/// Windows; this is the cross-platform equivalent and now the only mechanism, so the app and the
/// daemon exclude EACH OTHER when they share a root — two pollers on one bot token is the failure
/// both are guarding against, whichever host the second one is.
///
/// PER ROOT, WHICH IS NARROWER THAN THE MUTEX IT REPLACED. A session-global mutex excluded a second
/// host whatever root it used; this lock does not, so two hosts started with different `--root`
/// values run side by side. That is the intended reading — a different root is a different
/// installation, with its own config.json and its own secrets.json — but a second host pointed at a
/// different root while sharing ONE bot token would poll twice, and nothing here can see that.
///
/// The lock is an exclusive open (FileShare.None) on a file under the root: on Windows that is a
/// kernel share lock, on Linux/macOS .NET maps it to an advisory flock — both released by the OS
/// the instant the process dies, so a crashed host never leaves a stale lock behind, which a
/// pid-in-a-file scheme would.
/// </summary>
public static class SingleInstance_Guard
{
    /// <summary>The held lock, or null when it could not be taken.</summary>
    public static IDisposable? Try_Acquire(ISupervisionPaths paths)
    {
        return Try_Acquire(paths, out _);
    }

    /// <summary>
    /// The held lock, or null with <paramref name="failureReason"/> saying WHICH failure it was.
    /// The two are not the same event and the host says so to the owner: another host holding the
    /// lock is the ordinary case, while a lock file that cannot be opened at all — a root left
    /// behind by a run under another user, a read-only volume — is a misconfiguration that reads
    /// as "already running" unless it is named. Only IOException means "somebody else has it";
    /// everything else is reported as what it is.
    /// </summary>
    public static IDisposable? Try_Acquire(ISupervisionPaths paths, out string? failureReason)
    {
        // THE ROOT IS PREPARED IN ITS OWN TRY, and that is not tidiness. CreateDirectory throws
        // IOException too — "Not a directory" for a root under a file, for one — and folding it in
        // with the open below reported an unusable path as "another host is already running", which
        // sends the owner hunting for a process that does not exist. Caught in my own smoke test of
        // the fix that introduced it, with --root /dev/null/nope.
        try
        {
            Directory.CreateDirectory(paths.Root);
        }
        catch (Exception exception)
        {
            failureReason = $"the supervision root {paths.Root} could not be created ({exception.GetType().Name}: {exception.Message})";
            return null;
        }

        try
        {
            failureReason = null;

            return new FileStream(
                paths.InstanceLockFile,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            // Sharing violation on Windows, EWOULDBLOCK on POSIX: the other host is alive. This is
            // the ONLY failure that means contention — the root exists and is writable by now.
            failureReason = $"another host is already running against {paths.Root} — only one host may run per supervision root, because the Telegram bridge allows a single poller";
            return null;
        }
        catch (Exception exception)
        {
            // UnauthorizedAccessException and friends: the lock was never contended, it could not
            // be opened. Reported rather than thrown, so no host dies on a stack trace here.
            failureReason = $"the instance lock at {paths.InstanceLockFile} could not be opened ({exception.GetType().Name}: {exception.Message})";
            return null;
        }
    }
}
