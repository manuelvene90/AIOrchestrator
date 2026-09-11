using Xunit;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// Makes a folder refuse new files, so a test can provoke "the write failed" against real IO rather
/// than against a mock of it.
///
/// <para>
/// WHY A FOLDER AND NOT THE FILE: every state file in this app is written by
/// <c>Atomic_FileWriter</c>, which fills a SIBLING temp file and renames it over the target. A
/// read-only target is therefore not enough — the create has to be the thing that fails.
/// </para>
/// <para>
/// PROBED, NOT ASSUMED FROM THE PLATFORM NAME. POSIX mode bits are the mechanism here and Windows
/// has none (its equivalent is an ACL, and a test that quietly did nothing there would be the
/// harness that certifies without running — CLAUDE.md decision 20). The probe runs the exact
/// operation the test depends on; where it cannot work, the fact SKIPS and says why.
/// </para>
/// </summary>
public static class UnwritableFolder
{
    static readonly UnixFileMode READ_AND_ENTER = UnixFileMode.UserRead | UnixFileMode.UserExecute;

    public static bool IsSupported { get; } = Probe();

    /// <summary>
    /// Takes away the folder's write bit until the returned scope is disposed. Throws where
    /// <see cref="IsSupported"/> is false rather than doing nothing — a caller that skipped the check
    /// must find out, not get a green test out of a setup that never happened.
    /// </summary>
    public static IDisposable Take_WritePermission(string folder)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("POSIX mode bits do not exist on Windows — gate the test on UnwritableFolder.IsSupported");

        var restore = File.GetUnixFileMode(folder);

        File.SetUnixFileMode(folder, READ_AND_ENTER);

        // The guard is repeated inside the closure because the analyser cannot follow the one above
        // across a lambda — and a suppression here would be a claim rather than a check.
        return new Restore_Scope(() =>
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(folder, restore);
        });
    }

    static bool Probe()
    {
        if (OperatingSystem.IsWindows())
            return false;

        var folder = Path.Combine(Path.GetTempPath(), $"aiorch-unwritable-probe-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(folder);

            var restore = File.GetUnixFileMode(folder);

            File.SetUnixFileMode(folder, READ_AND_ENTER);

            try
            {
                File.WriteAllText(Path.Combine(folder, "probe.txt"), "x");
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // Exactly the failure the test needs to provoke.
                return true;
            }
            finally
            {
                File.SetUnixFileMode(folder, restore);
            }
        }
        catch (Exception)
        {
            // A host where the probe itself cannot run (no temp folder, root, an exotic filesystem)
            // is a host where the test cannot be trusted either.
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch
            {
                // The OS temp folder is not precious.
            }
        }
    }

    sealed class Restore_Scope(Action restore) : IDisposable
    {
        public void Dispose()
        {
            restore();
        }
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that runs only where a folder can actually be made unwritable —
/// see <see cref="UnwritableFolder"/>. Elsewhere the provoking setup provokes nothing, so the test
/// would assert against a write that trivially succeeded; skipping says so by name instead.
/// </summary>
public sealed class RequiresUnwritableFolderFactAttribute : FactAttribute
{
    public RequiresUnwritableFolderFactAttribute()
    {
        if (!UnwritableFolder.IsSupported)
        {
            Skip = "This host cannot make a folder refuse new files through POSIX mode bits (Windows has " +
                   "ACLs instead, and root ignores the bits) — confirmed by probe, not by platform name — so " +
                   "'the state file could not be written' cannot be provoked here.";
        }
    }
}
