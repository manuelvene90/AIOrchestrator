using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// BOTH INSTALLERS HAVE TO REFRESH A STALE CACHE, and only one of them can be run here.
///
/// <para>
/// `install.sh` was exercised end to end on macOS on 2026-09-07 in a throwaway HOME: fresh install,
/// a second run that changed nothing, a committed change to the kit that triggered the reinstall, and
/// an UNCOMMITTED change that triggered it too. `install.ps1` cannot be run on this machine — there
/// is no PowerShell on it — so its twin is asserted here by SHAPE. That is weaker than running it and
/// it is not nothing: the one thing this fix cannot survive is a deletion, and a deletion is exactly
/// what reading the file that no longer does it cannot notice.
/// </para>
/// <para>
/// Asserted on capability words rather than on sentences: `claude plugin uninstall` is the only
/// command that refreshes the cache (measured — update and marketplace update both report success and
/// change nothing), and each script must compare the installed copy against this checkout by CONTENT.
/// A rewording stays green; removing either stops being green.
/// </para>
/// </summary>
public class InstallersRefreshAStaleCacheTests
{
    [Theory]
    [InlineData("install.sh")]
    [InlineData("install.ps1")]
    public void EachInstaller_UninstallsBeforeInstalling_BecauseUpdateDoesNotRefreshTheCache(string installer)
    {
        var text = Read_Installer(installer);

        Assert.Contains("plugin uninstall aiorch", text);
        Assert.Contains("plugin install", text);
    }

    /// <summary>
    /// The comparison itself, per script's own tool: the POSIX one walks the two trees with `diff`,
    /// the PowerShell one hashes every file. Both catch what a commit comparison cannot — a checkout
    /// with uncommitted edits, where HEAD has not moved and the text has.
    /// </summary>
    [Theory]
    [InlineData("install.sh", "diff -rq")]
    [InlineData("install.ps1", "Get-FileHash")]
    public void EachInstaller_ComparesTheInstalledCopyByContent_NotByVersionNumber(string installer, string marker)
    {
        Assert.Contains(marker, Read_Installer(installer));
    }

    static string Read_Installer(string installer)
    {
        var path = KitRepoFiles.Find(Path.Combine("kit", installer));

        // Refuses rather than passes: a content test that found no content passes by finding nothing.
        Assert.True(path != null, $"could not find kit/{installer} from {AppContext.BaseDirectory}");

        return File.ReadAllText(path!);
    }
}
