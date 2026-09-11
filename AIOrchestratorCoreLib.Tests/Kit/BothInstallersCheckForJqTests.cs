using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// BOTH INSTALLERS HAVE TO MENTION jq, and only one of them did.
///
/// <para>
/// `install.sh` has required jq from the start — it merges `~/.claude/settings.json` and writes
/// `config.json`. `install.ps1` never checked, and the asymmetry was invisible because nothing on
/// Windows NEEDED jq: until E3, the channel tool wrote entries with `printf` alone. Now a typed entry
/// reads the grammar with jq, and a Windows session runs that tool in msys bash — so a machine set up
/// by `install.ps1` could pass every check the installer makes and then refuse every typed entry a
/// session tried to write, with nothing in the setup having warned about it.
/// </para>
/// <para>
/// ASSERTED BY SHAPE, and the neighbouring <c>InstallersRefreshAStaleCacheTests</c> records why that
/// is the honest ceiling here: there is no PowerShell on this machine, so `install.ps1` cannot be
/// run. Reading it is weaker than running it and it is not nothing — the failure this guards against
/// is a DELETION, and a deletion is exactly what reading the file cannot miss.
/// </para>
/// <para>
/// THEY ARE ALLOWED TO DIFFER IN SEVERITY, and they do. install.sh EXITS: without jq it cannot merge
/// the settings file it is there to merge. install.ps1 WARNS: the thing that needs jq is the TOOL, so
/// a machine without it still gets a working app, working role commands and untyped appends. This
/// asserts that each one says the word and names the fix, not that they behave identically.
/// </para>
/// </summary>
public class BothInstallersCheckForJqTests
{
    [Theory]
    [InlineData("install.sh")]
    [InlineData("install.ps1")]
    public void EachInstaller_ChecksForJq_AndSaysHowToGetIt(string installer)
    {
        var text = Read_Installer(installer);

        Assert.Contains("jq", text);

        // It must CHECK, not merely mention: a comment naming jq in a dependency list is what
        // install.sh already had at the top of the file, and it is not a check.
        var checks = text.Contains("command -v jq", StringComparison.Ordinal)
            || text.Contains("Get-Command jq", StringComparison.Ordinal);

        Assert.True(checks, $"kit/{installer} names jq but never checks for it, so a machine without it is set up silently.");

        // And it names a way to get it, in the installer's own idiom — a warning the reader cannot
        // act on is the "hook error" of decision 21, in an installer.
        var namesAFix = text.Contains("install jq", StringComparison.OrdinalIgnoreCase)
            || text.Contains("jqlang.jq", StringComparison.OrdinalIgnoreCase);

        Assert.True(namesAFix, $"kit/{installer} checks for jq but does not say how to install it.");
    }

    /// <summary>
    /// AND THE TOOL REFUSES ON ITS OWN, because an installer's warning is read once and the tool is
    /// run every turn. A session on a machine that was set up before this check existed gets the
    /// refusal, not a malformed entry.
    ///
    /// One line, naming the fix, and saying nothing was written — asserted here because the
    /// end-to-end probes in <c>ChannelAppendTypedEntriesTests</c> SKIP where jq is missing and so can
    /// never exercise this path on a machine that has it.
    /// </summary>
    [Fact]
    public void TheToolRefusesInOneLineWhenJqIsMissing()
    {
        var path = KitRepoFiles.Find(Path.Combine("kit", "bin", "channel-append.sh"));

        Assert.True(path != null, $"could not find kit/bin/channel-append.sh from {AppContext.BaseDirectory}");

        var text = File.ReadAllText(path!);

        Assert.Contains("command -v jq", text);

        var refusal = text
            .Replace("\r\n", "\n")
            .Split('\n')
            .FirstOrDefault(line => line.Contains("needs jq", StringComparison.Ordinal));

        Assert.False(refusal == null, "the tool no longer refuses a typed entry when jq is missing.");
        Assert.Contains("REFUSED", refusal!);
        Assert.Contains("NOTHING WAS WRITTEN", refusal!);
        Assert.Contains("--body-file", refusal!);
    }

    static string Read_Installer(string installer)
    {
        var path = KitRepoFiles.Find(Path.Combine("kit", installer));

        // Refuses rather than passes: a content test that found no content passes by finding nothing.
        Assert.True(path != null, $"could not find kit/{installer} from {AppContext.BaseDirectory}");

        return File.ReadAllText(path!);
    }
}
