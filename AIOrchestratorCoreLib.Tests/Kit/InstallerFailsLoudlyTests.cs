using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE INSTALLER IS RUN, NOT READ. Its neighbours <c>InstallersRefreshAStaleCacheTests</c> and
/// <c>BothInstallersCheckForJqTests</c> assert `install.sh` BY SHAPE, which is the honest ceiling for
/// the PowerShell twin and far below what is possible for the POSIX one: it is bash, this machine has
/// bash, and the failure it shipped was a failure of BEHAVIOUR that every shape assertion in the file
/// would have passed.
///
/// <para>
/// THE INCIDENT. On a production VPS the script printed "=== Setup complete ===" having installed
/// nothing: `claude` lives in <c>~/.local/bin</c> there, a login shell has that on PATH and
/// `ssh host 'bash kit/install.sh'` does not, and the entire plugin block sat inside
/// `if command -v claude`. One yellow line, exit 0, and the failure surfaced hours later as a daemon
/// refusing to spawn any session against the kit it found installed — read out of the journal, not
/// out of the installer. CLAUDE.md decision 20 is this rule from the reading side ("a harness that
/// cannot find what it tests must REFUSE TO RUN"); an installer that cannot find the tool it exists
/// to drive owes the same refusal.
/// </para>
/// <para>
/// HOW IT IS EXERCISED. A sandbox HOME, a PATH built from scratch, and a FAKE `claude` on it whose
/// failures are chosen per case. Nothing here touches the real Claude home, the real plugin cache or
/// the real settings file: every path the script writes to hangs off the sandbox HOME, and the fake
/// CLI is the only `claude` any of these runs can reach. The kit folder is the REAL one, read-only —
/// the script only reads it, and pinning against a copy would prove the copy.
/// </para>
/// </summary>
public class InstallerFailsLoudlyTests
{
    const string SETUP_COMPLETE = "Setup complete";

    /// <summary>
    /// NO CLI ANYWHERE: the incident itself. The assertion is not merely "non-zero" — a script can
    /// exit non-zero for a dozen reasons, and one that passed for any of them would pin nothing. It
    /// must exit non-zero, NOT reach its own success banner, and NAME the thing it could not find
    /// together with somewhere it looked.
    /// </summary>
    [RequiresNoSystemClaudeFact]
    public void WithNoClaudeAnywhere_ItRefusesAndNeverReachesSetupComplete()
    {
        using var sandbox = new InstallerSandbox();

        var run = sandbox.Run_Installer(claudeOnPath: null);

        Assert.False(run.ExitCode == 0, $"the installer exited 0 with no claude CLI anywhere.\n{run.Output}");
        Assert.DoesNotContain(SETUP_COMPLETE, run.Output);

        // It says WHAT is missing and WHERE it looked. "ERROR" alone is the "hook error" of decision
        // 21 in an installer: true, and not something the reader can act on.
        Assert.Contains("claude", run.Output, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(sandbox.Home, ".local", "bin", "claude"), run.Output, StringComparison.Ordinal);
        Assert.Contains("NOTHING WAS INSTALLED", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE VPS'S OWN LAYOUT: the CLI exists, at <c>~/.local/bin/claude</c>, and is not on the PATH of
    /// the shell running the script. Before the fix this was indistinguishable from the case above —
    /// both were one yellow line and exit 0. Now one refuses and this one installs.
    ///
    /// <para>
    /// Pinned by the resolved path it prints, not by the exit code alone: a script that stopped
    /// looking for the CLI and simply never called it would also exit 0.
    /// </para>
    /// </summary>
    [RequiresPosixInstallerFact]
    public void WhenTheCliIsOnlyInTheUsersLocalBin_ItIsFoundAndTheSetupCompletes()
    {
        using var sandbox = new InstallerSandbox();

        var cli = sandbox.Write_FakeClaude(
            Path.Combine(sandbox.Home, ".local", "bin"),
            installPath: sandbox.Copy_KitInto("cache"));

        var run = sandbox.Run_Installer(claudeOnPath: null);

        Assert.True(run.ExitCode == 0, $"the installer failed with the CLI at {cli}.\n{run.Output}");
        Assert.Contains($"claude CLI: {cli}", run.Output, StringComparison.Ordinal);
        Assert.Contains(SETUP_COMPLETE, run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A FAILING `plugin install` IS FATAL. It used to be a warning followed by every remaining step
    /// and the success banner, which on a machine that already had an older kit installed is the
    /// worst of the three outcomes: the sessions keep loading the OLD protocols and the operator has
    /// been told the setup succeeded.
    /// </summary>
    [RequiresPosixInstallerFact]
    public void WhenThePluginInstallFails_ItRefusesAndNeverReachesSetupComplete()
    {
        using var sandbox = new InstallerSandbox();

        // THE CACHE ON THIS MACHINE IS HEALTHY, and that is the whole fixture: with nothing
        // installed the script would stop a few lines later anyway, over the empty version, and
        // the case would pass whether or not this step refuses. MEASURED: the "warn and carry on"
        // mutation survived the first version of this test for exactly that reason. With a cache
        // that checks out, the only thing between this failure and "Setup complete" is the exit.
        sandbox.Write_FakeClaude(sandbox.FakeBin, installPath: sandbox.Copy_KitInto("cache"), failing: "install");

        var run = sandbox.Run_Installer(claudeOnPath: sandbox.FakeBin);

        Assert.False(run.ExitCode == 0, $"the installer exited 0 after `plugin install` failed.\n{run.Output}");
        Assert.DoesNotContain(SETUP_COMPLETE, run.Output);

        // THE CLI'S OWN WORDS, because "install failed" is not something a reader can act on: the
        // script re-runs the command visibly for exactly this.
        Assert.Contains("fake claude: install refused", run.Output, StringComparison.Ordinal);

        // AND IT STOPPED THERE. Without this the case passes for a second reason: an installer that
        // only WARNED here would still reach the version check below, find nothing installed, and
        // exit non-zero without the banner — green on every assertion above while behaving exactly
        // as it did during the incident.
        Assert.DoesNotContain("Configured Claude Code status line", run.Output);
        Assert.False(sandbox.Config_Written, "the installer went on to write config.json after the install failed");
    }

    /// <summary>
    /// And the step before it: without the local marketplace there is nothing to install FROM, so a
    /// failure here made the install below fail for a reason the reader could not see.
    /// </summary>
    [RequiresPosixInstallerFact]
    public void WhenTheMarketplaceRegistrationFails_ItRefusesAndNeverReachesSetupComplete()
    {
        using var sandbox = new InstallerSandbox();

        // THE CACHE ON THIS MACHINE IS HEALTHY, and that is the whole fixture: with nothing
        // installed the script would stop a few lines later anyway, over the empty version, and
        // the case would pass whether or not this step refuses. MEASURED: the "warn and carry on"
        // mutation survived the first version of this test for exactly that reason. With a cache
        // that checks out, the only thing between this failure and "Setup complete" is the exit.
        sandbox.Write_FakeClaude(sandbox.FakeBin, installPath: sandbox.Copy_KitInto("cache"), failing: "marketplace");

        var run = sandbox.Run_Installer(claudeOnPath: sandbox.FakeBin);

        Assert.False(run.ExitCode == 0, $"the installer exited 0 after `marketplace add` failed.\n{run.Output}");
        Assert.DoesNotContain(SETUP_COMPLETE, run.Output);
        Assert.Contains("fake claude: marketplace add refused", run.Output, StringComparison.Ordinal);

        // It stopped at the marketplace, not four steps later — same reasoning as the case above.
        Assert.DoesNotContain("Installed the aiorch plugin", run.Output);
        Assert.False(sandbox.Config_Written, "the installer went on to write config.json after the marketplace registration failed");
    }

    /// <summary>
    /// THE CONTENT COMPARE MUST BE ABLE TO SAY "MATCHES", and for as long as it could not, it said
    /// nothing at all.
    ///
    /// <para>
    /// MEASURED 2026-09-17 on CLI 2.1.274: the Claude Code CLI writes a <c>.in_use</c> FOLDER (one
    /// file per live session pid) into every plugin cache it loads — all seven caches on this machine
    /// carry one, and <c>kit/</c> has nothing of the sort. So the script's `diff -rq` answered
    /// "differs" on every machine where a session had ever loaded the plugin: it reinstalled on every
    /// single run, and then reported "CONTENT STILL DIFFERS" about the cache it had just rewritten
    /// correctly. The fixture here is exactly that: a cache identical to the checkout but for the
    /// marker folder.
    /// </para>
    /// </summary>
    [RequiresPosixInstallerFact]
    public void ACacheIdenticalButForTheCliOwnInUseFolder_CountsAsMatching()
    {
        using var sandbox = new InstallerSandbox();

        var cache = sandbox.Copy_KitInto("cache");
        Directory.CreateDirectory(Path.Combine(cache, ".in_use"));
        File.WriteAllText(Path.Combine(cache, ".in_use", "37651"), "a live session's pid file\n");

        sandbox.Write_FakeClaude(sandbox.FakeBin, installPath: cache);

        var run = sandbox.Run_Installer(claudeOnPath: sandbox.FakeBin);

        Assert.True(run.ExitCode == 0, $"a cache differing only by .in_use was rejected.\n{run.Output}");
        Assert.Contains("content matches this checkout", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("REINSTALLING", run.Output);
    }

    /// <summary>
    /// THE NEGATIVE CONTROL, and the case above is worth nothing without it: excluding a name from a
    /// comparison is one keystroke away from excluding everything, and a guard that answers "matches"
    /// unconditionally would pass the sibling test and catch no stale cache ever again.
    ///
    /// <para>
    /// One real file differs; the fake CLI's "install" copies nothing, so the cache is still stale
    /// after the reinstall — which is the shape of the 2026-09-07 VPS incident, and the installer
    /// must end on a refusal rather than on its banner.
    /// </para>
    /// </summary>
    [RequiresPosixInstallerFact]
    public void ACacheThatReallyDiffers_IsStillCaught_AndTheScriptRefuses()
    {
        using var sandbox = new InstallerSandbox();

        var cache = sandbox.Copy_KitInto("cache");
        Directory.CreateDirectory(Path.Combine(cache, ".in_use"));
        File.AppendAllText(Path.Combine(cache, "skills", "supervisor", "SKILL.md"), "\nstale text the checkout does not have\n");

        sandbox.Write_FakeClaude(sandbox.FakeBin, installPath: cache);

        var run = sandbox.Run_Installer(claudeOnPath: sandbox.FakeBin);

        Assert.Contains("REINSTALLING", run.Output, StringComparison.Ordinal);
        Assert.Contains("CONTENT STILL DIFFERS", run.Output, StringComparison.Ordinal);
        Assert.False(run.ExitCode == 0, $"the installer exited 0 over a cache it had just called stale.\n{run.Output}");
        Assert.DoesNotContain(SETUP_COMPLETE, run.Output);
    }
}

/// <summary>
/// A throwaway HOME, a PATH built from scratch and a fake `claude` — everything one run of
/// `kit/install.sh` can reach. The sandbox owns the temp folder and deletes it.
/// </summary>
sealed class InstallerSandbox : IDisposable
{
    /// <summary>
    /// The PATH every run gets, plus the fake's folder when the case wants the CLI discoverable there.
    /// Written out rather than inherited: the point of half these cases is that `claude` is NOT
    /// reachable, and inheriting the runner's PATH would import whatever this machine happens to have.
    /// jq, diff, sed, git and md5 all live in these.
    /// </summary>
    static readonly string[] SYSTEM_PATH = ["/usr/bin", "/bin", "/usr/sbin", "/sbin", "/opt/homebrew/bin", "/usr/local/bin"];

    readonly string _root;

    public InstallerSandbox()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aiorch-installer-{Guid.NewGuid():N}");
        Home = Path.Combine(_root, "home");
        FakeBin = Path.Combine(_root, "bin");

        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(FakeBin);
    }

    public string Home { get; }

    public string FakeBin { get; }

    /// <summary>Whether the run reached section 4 — the evidence that it did not stop where it should.</summary>
    public bool Config_Written => File.Exists(Path.Combine(Home, ".claude", "supervision", "config.json"));

    public InstallerRun Run_Installer(string? claudeOnPath)
    {
        var path = claudeOnPath == null
            ? string.Join(':', SYSTEM_PATH)
            : string.Join(':', [claudeOnPath, .. SYSTEM_PATH]);

        var start = new ProcessStartInfo
        {
            FileName = Bash_Locator.Find_OrFail(),
            ArgumentList = { InstallerPath.Value! },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // CLOSED, not inherited: `install.sh` prompts only on a tty, and a run that inherited the
            // test runner's terminal would block on the first `read -p` for ever.
            RedirectStandardInput = true,
            UseShellExecute = false,
            WorkingDirectory = _root,
        };

        start.Environment["HOME"] = Home;
        start.Environment["PATH"] = path;

        using var process = Process.Start(start)
            ?? throw new Exception("could not start bash for the installer");

        process.StandardInput.Close();

        var output = new StringBuilder();
        output.Append(process.StandardOutput.ReadToEnd());
        output.Append(process.StandardError.ReadToEnd());

        Assert.True(process.WaitForExit(120_000), "the installer did not finish within two minutes");

        return new InstallerRun(process.ExitCode, output.ToString());
    }

    /// <summary>
    /// A stand-in for the CLI answering the four subcommands the installer uses. `plugin list --json`
    /// reports the version this checkout actually ships, read from plugin.json rather than typed here:
    /// a literal would be the second copy that goes stale the day the kit is versioned.
    /// </summary>
    public string Write_FakeClaude(string folder, string? installPath, string? failing = null)
    {
        Directory.CreateDirectory(folder);

        var record = installPath == null
            ? "[]"
            : $"[{{\"id\":\"aiorch@aiorch-local\",\"version\":\"{Shipped_Version()}\",\"scope\":\"user\",\"enabled\":true,\"installPath\":\"{installPath}\"}}]";

        // $$ AND {{…}}: in a raw interpolated string a doubled brace is NOT an escape, so the bash
        // ${1:-} below would be read as a hole. Two dollars make {{…}} the hole and leave every
        // single brace as the shell's own.
        var script = $$"""
            #!/usr/bin/env bash
            # A stand-in for the claude CLI, written by InstallerFailsLoudlyTests.
            case "${1:-} ${2:-}" in
                "plugin list")
                    printf '%s\n' '{{record}}'
                    ;;
                "plugin marketplace")
                    if [ '{{failing}}' = 'marketplace' ]; then
                        printf '%s\n' 'fake claude: marketplace add refused' >&2
                        exit 1
                    fi
                    ;;
                "plugin install")
                    if [ '{{failing}}' = 'install' ]; then
                        printf '%s\n' 'fake claude: install refused' >&2
                        exit 1
                    fi
                    ;;
            esac
            exit 0

            """;

        var path = Path.Combine(folder, "claude");
        File.WriteAllText(path, script.Replace("\r\n", "\n"));

        // Guarded although every case here skips on Windows: the compiler cannot see the skip, and a
        // platform warning nobody can act on is noise of exactly the kind decision 15 is about.
        if (OperatingSystem.IsWindows())
            return path;

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        return path;
    }

    /// <summary>A copy of the real kit, standing in for the CLI's plugin cache.</summary>
    public string Copy_KitInto(string name)
    {
        var target = Path.Combine(_root, name);

        Copy_Folder(KitFolder.Value!, target);

        return target;
    }

    static string Shipped_Version()
    {
        var manifest = File.ReadAllText(Path.Combine(KitFolder.Value!, ".claude-plugin", "plugin.json"));
        var match = Regex.Match(manifest, "\"version\"\\s*:\\s*\"([^\"]+)\"");

        Assert.True(match.Success, "kit/.claude-plugin/plugin.json carries no version, so the fake CLI could not report one");

        return match.Groups[1].Value;
    }

    static void Copy_Folder(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));

        foreach (var folder in Directory.GetDirectories(source))
            Copy_Folder(folder, Path.Combine(target, Path.GetFileName(folder)));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // A leftover temp folder is not worth failing a green run over.
        }
    }

    internal static readonly Lazy<string?> InstallerPath =
        new(() => KitRepoFiles.Find(Path.Combine("kit", "install.sh")));

    /// <summary>
    /// The kit folder, taken from the INSTALLER'S OWN parent and never from `Find("kit")`. macOS is
    /// case-insensitive by default, so that lookup matches this very test folder —
    /// `AIOrchestratorCoreLib.Tests/Kit` — and the harness would then compare a plugin cache against
    /// a folder of C# files. `SupervisionRootHonoursOverrideTests` carries the same warning; this is
    /// the second place to have been bitten by it.
    /// </summary>
    internal static readonly Lazy<string?> KitFolder =
        new(() => InstallerPath.Value == null ? null : Path.GetDirectoryName(InstallerPath.Value));
}

record InstallerRun(int ExitCode, string Output);

/// <summary>
/// Runs only where `kit/install.sh` can actually be invoked, and SKIPS BY NAME otherwise — the same
/// rule as <c>RequiresChannelToolFactAttribute</c>: a green that ran nothing is the failure, and a
/// skip is visible in the suite's count.
/// </summary>
public class RequiresPosixInstallerFactAttribute : FactAttribute
{
    public RequiresPosixInstallerFactAttribute()
    {
        var reason = Find_Reason();

        if (reason != null)
            Skip = reason;
    }

    /// <summary>The absolute candidates in the installer's fallback list that a sandbox cannot hide.</summary>
    protected static readonly IReadOnlyList<string> SYSTEM_CLAUDE_PATHS = ["/usr/local/bin/claude", "/opt/homebrew/bin/claude"];

    static readonly string[] SANDBOX_PATH = ["/usr/bin", "/bin", "/usr/sbin", "/sbin", "/opt/homebrew/bin", "/usr/local/bin"];

    static string? Find_Reason()
    {
        if (OperatingSystem.IsWindows())
            return "install.sh is the POSIX installer; this harness builds a POSIX PATH and runs on macOS and Linux.";

        if (InstallerSandbox.InstallerPath.Value == null)
            return "kit/install.sh was not found from the test binary, so nothing would be exercised.";

        if (InstallerSandbox.KitFolder.Value == null)
            return "the kit folder was not found from the test binary, so nothing would be exercised.";

        if (!Bash_Locator.Is_Available())
            return "no bash on this machine — install.sh is bash and cannot be run here.";

        foreach (var tool in new[] { "jq", "diff" })
        {
            if (!SANDBOX_PATH.Any(folder => File.Exists(Path.Combine(folder, tool))))
                return $"{tool} is not in the sandbox PATH — install.sh needs it, so nothing would be exercised.";
        }

        return null;
    }
}

/// <summary>
/// For the one case that needs `claude` to be findable NOWHERE. The installer's fallback list holds
/// two ABSOLUTE paths this harness cannot hide — it controls HOME and PATH, not /usr/local — so on a
/// machine carrying a CLI at either of them the case skips by name rather than failing for a reason
/// that is not the code's. (Neither exists on the machine this was written on: the CLI is the native
/// install, at ~/.local/bin/claude.)
/// </summary>
public sealed class RequiresNoSystemClaudeFactAttribute : RequiresPosixInstallerFactAttribute
{
    public RequiresNoSystemClaudeFactAttribute()
    {
        if (Skip != null)
            return;

        var found = SYSTEM_CLAUDE_PATHS.FirstOrDefault(File.Exists);

        if (found != null)
            Skip = $"this machine has a claude CLI at {found}, which the sandbox cannot hide — the 'no CLI anywhere' case cannot be set up here.";
    }
}
