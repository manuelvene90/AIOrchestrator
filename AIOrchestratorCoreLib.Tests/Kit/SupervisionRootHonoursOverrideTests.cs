using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE DEFECT: every kit script that resolves the supervision folder used to hardcode
/// <c>$HOME/.claude/supervision</c> (or, on Windows, <c>$env:USERPROFILE\.claude\supervision</c>) and
/// ignore <c>AIORCH_SUPERVISION_ROOT</c> — the environment variable
/// <c>HostOptions_Factory.SUPERVISION_ROOT_ENV</c> that <c>AIOrchestrator.Daemon/BridgeHost_Service.cs</c>
/// and <c>AIOrchestrator/App.xaml.cs</c> export into every spawned session, and that README-daemon.md
/// promises sessions honour (<c>--root DIR</c>). A daemon started with a non-default root spawns
/// sessions whose enforcement hooks still look at <c>~/.claude/supervision</c> — a folder for a
/// DIFFERENT orchestration, or nothing at all — so every guard silently no-ops. The supervisor
/// protocol tells the session "the app enforces this by STOPPING YOU"; on such a host that sentence
/// is false, and nothing anywhere says so.
///
/// THIS TEST ENUMERATES THE SCRIPTS FROM DISK rather than naming them, precisely so a new hook or
/// statusline script added later cannot silently reintroduce the bug: it walks every *.sh and *.ps1
/// under kit/ and inspects every LINE, so a script that resolves its root more than once (as
/// supervisor-ledger-check.sh and supervisor-awaiting-answer-check.sh both do) cannot pass by fixing
/// one site and leaving another — a half-fixed script is worse than an unfixed one, because it works
/// on some hosts and silently fails on others.
///
/// EXCLUDED: kit/install.sh and kit/install.ps1. Those are one-time MACHINE setup — they establish
/// the default `~/.claude` home itself, the same default `HostOptions_Factory.Create_Default()`
/// builds by consulting no override ("the WPF app's paths ... no overrides consulted"). They are not
/// per-session scripts a running bridge points somewhere else; a `--root` daemon still installs the
/// kit at the ordinary machine home.
///
/// Nothing here skips: a kit/ folder that cannot be found is a refusal to run, not a pass about
/// nothing (decision 20, the same rule every other Kit test in this project follows).
/// </summary>
public class SupervisionRootHonoursOverrideTests
{
    const string OVERRIDE_VAR = "AIORCH_SUPERVISION_ROOT";

    [Fact]
    public void EveryScriptThatResolvesTheSupervisionRoot_HonoursTheOverride_WithTheDocumentedFallback()
    {
        // NOT KitRepoFiles.Find("kit") directly: on a case-insensitive filesystem (macOS default)
        // that matches this very test project's own Kit/ folder — Path.Combine(x, "kit") equals
        // Path.Combine(x, "Kit") — before it ever reaches the repo's real kit/. Anchoring on
        // kit/hooks (a path with no case-colliding twin, the same anchor RoleHooksAreShippedTests
        // uses) finds the real one.
        var hooksFolder = KitRepoFiles.Find(Path.Combine("kit", "hooks"))
            ?? throw new Exception("kit/hooks was not found walking up from the test binary — REFUSING to pass about scripts this test never looked at.");
        var kitFolder = Directory.GetParent(hooksFolder)!.FullName;

        var scripts = Find_ScriptsToCheck(kitFolder);

        // A search that found nothing proves nothing (decision 20). The kit ships at least the four
        // reported hooks plus the two statusline twins, so anything short of that means the walk
        // broke, not that the kit shrank.
        Assert.True(scripts.Count >= 6, $"expected to scan at least 6 scripts under {kitFolder}, found {scripts.Count} — the enumeration is broken, not the kit");

        var violations = new List<string>();

        foreach (var script in scripts)
        {
            var isPowerShell = script.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(script);

            for (var i = 0; i < lines.Length; i++)
            {
                if (Line_HardcodesTheSupervisionHome(lines[i], isPowerShell))
                    violations.Add($"{script}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "the following line(s) build the supervision folder from $HOME / $env:USERPROFILE directly, " +
            $"ignoring {OVERRIDE_VAR}:\n" + string.Join('\n', violations));
    }

    /// <summary>
    /// Every *.sh and *.ps1 under kit/, except the two one-time machine installers (see class doc).
    /// Walking the disk — not naming files — is the point: a script added tomorrow is scanned too.
    /// </summary>
    static List<string> Find_ScriptsToCheck(string kitFolder)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(kitFolder, "install.sh"),
            Path.Combine(kitFolder, "install.ps1"),
        };

        return [.. Directory.EnumerateFiles(kitFolder, "*.sh", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(kitFolder, "*.ps1", SearchOption.AllDirectories))
            .Where(file => !excluded.Contains(file))
            .OrderBy(file => file, StringComparer.Ordinal)];
    }

    /// <summary>
    /// A line "resolves a supervision root" when it builds the DEFAULT path
    /// (`$HOME/.claude/supervision` in bash, `$env:USERPROFILE ... .claude\supervision` in
    /// PowerShell) — the exact shape every reported defect site had. Such a line is only correct when
    /// it also names the override on the SAME line, which is what the standard fallback form
    /// (`${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}`, or the PowerShell if/else equivalent)
    /// guarantees; a bare hardcode never mentions the variable at all.
    /// </summary>
    static bool Line_HardcodesTheSupervisionHome(string line, bool isPowerShell)
    {
        if (line.Contains(OVERRIDE_VAR, StringComparison.Ordinal))
            return false;

        return isPowerShell
            ? line.Contains("USERPROFILE", StringComparison.Ordinal) && line.Contains(@".claude\supervision", StringComparison.Ordinal)
            : line.Contains("$HOME", StringComparison.Ordinal) && line.Contains(".claude/supervision", StringComparison.Ordinal);
    }
}
