using Xunit;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// THE ONE ANSWER to "can an open file handle block a rename or delete of that file on this
/// machine". On Windows, opening a file without <see cref="FileShare.Delete"/> makes
/// <c>File.Move</c>/<c>File.Delete</c> throw a sharing-violation <see cref="IOException"/> — that is
/// the mechanism half a dozen durability tests provoke to prove a failed write leaves the original
/// untouched. On macOS/Linux, <c>rename(2)</c> and <c>unlink(2)</c> do not consult other processes'
/// open file descriptors at all — POSIX has no sharing-violation concept, and being uid 0 would not
/// change that, since it is not a permissions check being defeated — so the same setup lets the
/// write succeed and the assertion that it threw fails.
/// <para>
/// Probed once, ACTUALLY on this machine, never assumed from <c>RuntimeInformation</c> or
/// <c>OperatingSystem.IsWindows()</c> — the whole point is not to guess an OS's filesystem semantics
/// from its name (a case-insensitive filesystem, a container, a future POSIX-on-Windows layer could
/// all disagree with the platform label).
/// </para>
/// </summary>
public static class FileShareEnforcement
{
    static readonly Lazy<bool> _isEnforced = new(Probe);

    /// <summary>
    /// True when an open handle that withholds <see cref="FileShare.Delete"/> actually blocks a
    /// rename/delete of that file on this machine — the precondition every "the target cannot be
    /// replaced" test needs in order to provoke anything at all.
    /// </summary>
    public static bool IsEnforced => _isEnforced.Value;

    static readonly Lazy<bool> _isExclusiveOpenEnforced = new(Probe_ExclusiveOpen);

    /// <summary>
    /// True when a handle opened with <see cref="FileShare.None"/> makes ANOTHER open of the file for
    /// reading fail on this machine — even one that grants every share (the flags
    /// <c>Tolerant_FileReader</c> opens with). A DIFFERENT question from <see cref="IsEnforced"/>, and it
    /// has a different answer across OSes: Windows refuses the open through its share modes, and .NET on
    /// Linux and macOS emulates <see cref="FileShare.None"/> with an advisory <c>flock(LOCK_EX)</c> that
    /// every other <see cref="FileStream"/> open (it takes <c>LOCK_SH</c>) runs into — whereas nothing on
    /// POSIX stops a rename. Probed, never assumed: <c>DOTNET_SYSTEM_IO_DISABLEFILELOCKING</c>, or a
    /// filesystem where flock is unsupported, turns the emulation off, and a test that then asserted a
    /// refused read would be asserting on a read that simply succeeded (plan 04 Task 2b, 2026-09-23).
    /// </summary>
    public static bool IsExclusiveOpenEnforced => _isExclusiveOpenEnforced.Value;

    static bool Probe_ExclusiveOpen()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aiorch-exclusive-open-probe-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, "probe");

        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try
                {
                    using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        // Opened beside an exclusive handle: this machine does not enforce it against a reader.
                        return false;
                    }
                }
                catch (IOException)
                {
                    return true;
                }
            }
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup, as in Probe: the answer is already decided and must not be changed by this.
            }
        }
    }

    static bool Probe()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aiorch-fileshare-probe-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, "probe");

        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    File.Delete(path);

                    // Deleted while a handle without FileShare.Delete was still open: this OS does
                    // not enforce sharing via open handles against delete/rename.
                    return false;
                }
                catch
                {
                    return true;
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup of the probe file; nothing downstream depends on it existing
                // or not, and swallowing this must never turn into a false read of IsEnforced.
            }
        }
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that runs only where an open file handle can actually block a
/// rename/delete of that file — see <see cref="FileShareEnforcement"/>. Elsewhere (this machine,
/// today: macOS — POSIX <c>rename(2)</c>/<c>unlink(2)</c> ignore other descriptors' share flags,
/// unlike Windows' <c>CreateFile</c> share mode) the provoking setup cannot provoke anything, so
/// running the test would assert on a write that trivially succeeded. Skipping says so by name
/// instead of turning the suite permanently red for a mismatch nothing on the test side can fix —
/// the assertions themselves are untouched and still run in full wherever this CAN work.
/// </summary>
public sealed class RequiresFileShareEnforcementFactAttribute : FactAttribute
{
    public RequiresFileShareEnforcementFactAttribute()
    {
        if (!FileShareEnforcement.IsEnforced)
        {
            Skip = "This OS's filesystem does not enforce FileShare via an open handle against " +
                   "rename/delete (POSIX rename(2)/unlink(2) ignore other file descriptors' share " +
                   "flags entirely, unlike Windows CreateFile share-mode — confirmed by probe, not " +
                   "by platform name; uid does not change this) — cannot provoke 'the target cannot " +
                   "be replaced' on this machine.";
        }
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that runs only where a <see cref="FileShare.None"/> handle actually makes
/// another read of the file fail — see <see cref="FileShareEnforcement.IsExclusiveOpenEnforced"/>. Expected
/// to RUN on Windows, Linux and macOS alike (the last two through .NET's flock emulation); it skips, by name,
/// only where the probe finds that emulation switched off, because there "the file is in use" cannot be
/// provoked and the test would pass on a read that never failed.
/// </summary>
public sealed class RequiresExclusiveOpenEnforcementFactAttribute : FactAttribute
{
    public RequiresExclusiveOpenEnforcementFactAttribute()
    {
        if (!FileShareEnforcement.IsExclusiveOpenEnforced)
        {
            Skip = "On this machine a FileShare.None handle does not stop another FileStream from opening the " +
                   "file for reading (no Windows share mode, and .NET's flock emulation is off — " +
                   "DOTNET_SYSTEM_IO_DISABLEFILELOCKING, or a filesystem without flock; confirmed by probe, not " +
                   "by platform name) — cannot provoke 'config.json is in use' here.";
        }
    }
}
