using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE KIT A HOST SHIPS MUST CONTAIN EVERY FOLDER THE HOST'S OWN CHECK DIGESTS — AND THE LIST OF WHAT
/// SHIPS MUST EXIST EXACTLY ONCE.
///
/// <para>
/// MEASURED ON THE PRODUCTION VPS, 2026-09-18 (orch@159.195.254.120): after the deploy of
/// <c>59d942c</c> the daemon refused every spawn — *"Kit NOT up to date — sessions cannot start"* —
/// and told the operator to run <c>bash kit/install.sh</c>. The installer could never have fixed it:
/// the plugin cache was RIGHT (it carried <c>grammar/</c>), the daemon's own <c>/opt/aiorchestrator/kit/</c>
/// was WRONG (no <c>grammar/</c>), because <c>AIOrchestrator.Daemon.csproj</c> declared "Same Link
/// layout as AIOrchestrator.csproj" and was not: the WPF project copied <c>..\kit\grammar\*.json</c>,
/// the daemon's copy-pasted list lacked that one line. Invisible while <see cref="KitContent_Digest"/>
/// did not read the grammar; a permanent, self-inflicted mismatch the day <c>2880f67</c> added it.
/// </para>
/// <para>
/// TWO GUARDS, both structural. (1) The ship list lives in ONE file, <c>KitContent.props</c>, and
/// both hosts import it — so "same layout" is a fact of the build, not a comment. (2) That one list
/// covers every folder <see cref="KitContent_Digest.DIGESTED_FOLDERS"/> names — so adding a folder
/// to the check without shipping it is red here, on the developer's machine, not on the VPS after
/// the service has been stopped.
/// </para>
/// </summary>
public class KitShipsEveryDigestedFolderTests
{
    const string SHIP_LIST = "KitContent.props";

    static readonly string[] HOST_PROJECTS =
    [
        Path.Combine("AIOrchestrator", "AIOrchestrator.csproj"),
        Path.Combine("AIOrchestrator.Daemon", "AIOrchestrator.Daemon.csproj"),
    ];

    [Fact]
    public void TheOneShipListCoversEveryFolderTheDigestReads()
    {
        var shipList = Read_RepoFile(SHIP_LIST);

        var shippedFolders = Regex.Matches(shipList, @"Include=""\$\(MSBuildThisFileDirectory\)kit\\(?<folder>[^\\""]+)\\")
            .Select(match => match.Groups["folder"].Value)
            .Distinct()
            .ToList();

        // THE HARNESS PROVES ITSELF FIRST: a list that matched nothing would make every
        // Assert.Contains below fail for the wrong reason, or — if the digest list were ever empty —
        // pass in silence.
        Assert.True(shippedFolders.Count >= 2, $"the ship list matched {shippedFolders.Count} kit folders — the scan is not reading {SHIP_LIST}");
        Assert.NotEmpty(KitContent_Digest.DIGESTED_FOLDERS);

        foreach (var folder in KitContent_Digest.DIGESTED_FOLDERS)
        {
            Assert.True(
                shippedFolders.Contains(folder),
                $"KitContent_Digest digests kit/{folder}/ but {SHIP_LIST} does not ship it — every host built from this tree would refuse to spawn against a correctly installed plugin, and `kit/install.sh` cannot fix a build");
        }

        Assert.Contains(".claude-plugin", shippedFolders);
    }

    [Fact]
    public void EveryHostImportsTheOneShipListAndCarriesNoKitListOfItsOwn()
    {
        foreach (var project in HOST_PROJECTS)
        {
            var csproj = Read_RepoFile(project);

            Assert.True(
                Regex.IsMatch(csproj, @"<Import\s+Project=""[^""]*" + Regex.Escape(SHIP_LIST) + @"""\s*/>"),
                $"{project} does not import {SHIP_LIST} — its kit would be whatever it copies itself, which is how the daemon shipped without grammar/");

            Assert.False(
                csproj.Contains(@"Include=""..\kit\", StringComparison.Ordinal),
                $"{project} still carries its own `..\\kit\\` Content items — two ship lists is the defect this test exists for; the list lives in {SHIP_LIST} only");
        }
    }

    static string Read_RepoFile(string relativePath)
    {
        var path = KitRepoFiles.Find(relativePath);

        Assert.True(path != null, $"{relativePath} was not found walking up from {AppContext.BaseDirectory} — the test cannot prove anything about a file it cannot read");

        return File.ReadAllText(path!);
    }
}
