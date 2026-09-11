using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace ClaudeContract.Tests.Live;

/// <summary>
/// The probe folder of MEASUREMENTS.md §M0, rebuilt for every Live run: a project-level
/// <c>.claude/settings.json</c> wiring six hooks and the status line to two tiny shell scripts that
/// log what they receive, plus the <c>/probe-hello</c> slash command. Everything the CLI writes
/// during the run stays under this folder, which sits inside the gitignored
/// <c>tools/claude-contract/last-run/</c>.
/// </summary>
public sealed class ProbeWorkspace
{
    public static readonly IReadOnlyList<string> HOOK_EVENTS =
        ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "Stop", "SessionEnd"];

    public string RunFolder { get; }
    public string ProbeFolder { get; }
    public string HooksLog => Path.Combine(ProbeFolder, "hooks.log");
    public string HookPayloadsLog => Path.Combine(ProbeFolder, "hook-payloads.log");
    public string StatuslineLog => Path.Combine(ProbeFolder, "statusline.log");

    public ProbeWorkspace(string runLabel)
    {
        RunFolder = Repo_Locator.Create_LastRunFolder(runLabel);
        ProbeFolder = Path.Combine(RunFolder, "probe");
        Directory.CreateDirectory(Path.Combine(ProbeFolder, ".claude", "commands"));

        var hookScript = Path.Combine(ProbeFolder, "hook.sh");
        var statuslineScript = Path.Combine(ProbeFolder, "statusline.sh");

        File.WriteAllText(hookScript,
            "#!/bin/sh\n" +
            $"printf '=== %s %s pid=%s ppid=%s socket=%s\\n' \"$1\" \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\" \"$$\" \"$PPID\" \"$CLAUDE_CODE_MESSAGING_SOCKET\" >> \"{HooksLog}\"\n" +
            $"printf '=== %s ' \"$1\" >> \"{HookPayloadsLog}\"\n" +
            $"cat >> \"{HookPayloadsLog}\"\n" +
            $"printf '\\n' >> \"{HookPayloadsLog}\"\n" +
            "exit 0\n");

        File.WriteAllText(statuslineScript,
            "#!/bin/sh\n" +
            $"cat >> \"{StatuslineLog}\"\n" +
            $"printf '\\n' >> \"{StatuslineLog}\"\n" +
            "echo probe-status\n");

        Make_Executable(hookScript);
        Make_Executable(statuslineScript);

        var hooks = new JsonObject();

        foreach (var hookEvent in HOOK_EVENTS)
        {
            var entry = new JsonObject
            {
                // Single-quoted: the hook command is a SHELL line, and a checkout path with a space
                // ("Visual Studio") split it into "/Users/x/Visual" — every hook then "failed to fire".
                ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = $"'{hookScript}' {hookEvent}" }),
            };

            if (hookEvent is "PreToolUse" or "PostToolUse")
                entry["matcher"] = "Bash";

            hooks[hookEvent] = new JsonArray(entry);
        }

        var settings = new JsonObject
        {
            ["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = $"'{statuslineScript}'" },
            ["hooks"] = hooks,
        };

        File.WriteAllText(Path.Combine(ProbeFolder, ".claude", "settings.json"), settings.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllText(Path.Combine(ProbeFolder, ".claude", "commands", "probe-hello.md"),
            "---\n" +
            "description: probe slash command\n" +
            "---\n" +
            "You are the PROBE role. Immediately use the Bash tool to run exactly: echo \"SLASH_ROLE_OK $ARGUMENTS\" > slash-out.txt\n" +
            "Then reply with the single word DONE and stop.\n");
    }

    public int Count_HookEvents(string hookEvent)
    {
        if (!File.Exists(HooksLog))
            return 0;

        return File.ReadAllLines(HooksLog).Count(line => line.StartsWith($"=== {hookEvent} ", StringComparison.Ordinal));
    }

    public string Read_ProbeFile_OrEmpty(string fileName)
    {
        var path = Path.Combine(ProbeFolder, fileName);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
    }

    /// <summary>Saves one piece of evidence into the run folder (verbatim; the report quotes from here).</summary>
    public void Save_Evidence(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(RunFolder, fileName), content);
    }

    /// <summary>
    /// The transcript folder Claude Code keeps for this cwd (~/.claude/projects/&lt;slug&gt;), removed
    /// at the end of a run as MEASUREMENTS.md §M9 did — only the probe's own, never another cwd's.
    /// </summary>
    public void Delete_TranscriptFolder_BestEffort()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var projects = Path.Combine(home, ".claude", "projects");

            if (!Directory.Exists(projects))
                return;

            var realProbe = Path.GetFullPath(ProbeFolder);
            var slugCandidates = new[] { realProbe, Path.Combine("/private", realProbe.TrimStart('/')) }
                .Select(path => path.Replace(Path.DirectorySeparatorChar, '-').Replace('/', '-').Replace('.', '-'))
                .ToList();

            foreach (var slug in slugCandidates)
            {
                var folder = Path.Combine(projects, slug);

                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
        }
        catch
        {
            // Leftover transcripts are a tidiness issue, never a test failure.
        }
    }

    static void Make_Executable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}
