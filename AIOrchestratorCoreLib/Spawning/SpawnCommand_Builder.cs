using System.Text;
using AIOrchestratorCoreLib.Spawning.SpawnCommand;

namespace AIOrchestratorCoreLib.Spawning;

/// <summary>
/// Builds the Windows Terminal command that opens a Claude Code session with its role, id and
/// visual identity (tab title + color; red family = supervisor, blue family = implementer,
/// amber = general). The launched PowerShell sets the AIORCH_* env vars (read by the status line
/// script), writes ITS OWN pid to the session's pid file (the watchdog's liveness source — the
/// wt.exe pid is useless, wt delegates to an existing window and exits), and runs claude with the
/// role slash command as its initial prompt. No -NoExit: the shell dies with claude, so a dead pid
/// means a dead session.
///
/// Resume semantics: the GENERAL supervisor runs in the supervision root (a directory only it uses),
/// so a restart safely resumes its previous conversation via 'claude --continue' (falling back to
/// the role command on first-ever start). Orchestration supervisors and implementers share the
/// repo directory, where --continue could resume the WRONG session's conversation — they restart
/// through their role command instead, whose boot sequence re-reads the channels (the channels ARE
/// the durable state by design).
/// </summary>
public static class SpawnCommand_Builder
{
    public const string SUPERVISOR_TAB_COLOR = "#E5484D";
    public const string IMPLEMENTER_TAB_COLOR = "#3B82F6";
    public const string GENERAL_TAB_COLOR = "#F5A623";
    public const string COMMUNICATOR_TAB_COLOR = "#22C55E";
    /// <summary>Green, inherited from the retired communicator (owner's call) — reviewers own it now.</summary>
    public const string REVIEWER_TAB_COLOR = "#22C55E";

    /// <summary>
    /// A reviewer is READ-ONLY BY CONSTRUCTION: the CLI itself refuses it the editing tools, so
    /// "investigate only, no edits" stops being a sentence in a brief that it must remember to
    /// honour. Its Bash access is additionally guarded by a PreToolUse hook (mutating commands and
    /// writes outside its own channel are blocked) — it still needs Bash for git log/diff, grep and
    /// for appending its report.
    /// </summary>
    public const string REVIEWER_LAUNCH_FLAGS = "--disallowedTools \"Write\" \"Edit\" \"NotebookEdit\"";

    /// <summary>
    /// Orchestrated sessions run unattended (the owner may be on their phone) — a permission
    /// prompt would hang the whole loop, so every session skips them (owner directive).
    /// </summary>
    public const string CLAUDE_LAUNCH_FLAGS = "--dangerously-skip-permissions";

    public static ISpawnCommand Build_ForSupervisor(string orchId, string repoPath, string? model, string pidFilePath, string? displayName)
    {
        Validate_OrchId(orchId);

        var script = Build_SessionScript("supervisor", orchId, "sup", $"{Build_ClaudeInvocation(model)} '/supervisor {orchId}'", pidFilePath);

        return Build_WindowsTerminalCommand(SessionWindowTitle_Builder.Build_Title(SessionWindowTitle_Builder.Build_ForSupervisor(orchId), displayName), SUPERVISOR_TAB_COLOR, repoPath, script);
    }

    /// <summary>
    /// A reviewer session: adversarial review by default, no worktree (it reads the repo and the
    /// implementers' branches), and no ability to edit or commit.
    /// </summary>
    public static ISpawnCommand Build_ForReviewer(string orchId, string memberId, string repoPath, string? model, string pidFilePath, string? displayName)
    {
        Validate_OrchId(orchId);

        // The '--' is LOAD-BEARING. --disallowedTools is variadic (<tools...>), so without a
        // terminator it swallows the prompt that follows it: the CLI parsed "/reviewer orch/rev-1"
        // as TOOL NAMES and started a session with no prompt at all. Every reviewer therefore came
        // up blank — never booted, never wrote to its channel, and was nudged then respawned on a
        // loop. Verified against the real CLI, which reports "Permission deny rule ... matches no
        // known tool" for each swallowed word.
        var claudeCommand = $"{Build_ClaudeInvocation(model)} {REVIEWER_LAUNCH_FLAGS} -- '/reviewer {orchId}/{memberId}'";
        var script = Build_SessionScript("reviewer", orchId, memberId, claudeCommand, pidFilePath);

        return Build_WindowsTerminalCommand(SessionWindowTitle_Builder.Build_Title(SessionWindowTitle_Builder.Build_ForMember(memberId, orchId), displayName), REVIEWER_TAB_COLOR, repoPath, script);
    }

    /// <summary>Basic orchestrations: one session, orange, talking straight to the owner.</summary>
    public const string SOLO_TAB_COLOR = "#F97316";

    /// <summary>
    /// A BASIC orchestration's only session. It reads and writes owner-channel.md directly — the
    /// same file a supervisor would own — so the owner's Telegram topic reaches it with no routing
    /// changes anywhere. No supervisor, no reviewer, no worktree assignment.
    /// </summary>
    public static ISpawnCommand Build_ForSolo(string orchId, string memberId, string repoPath, string? model, string pidFilePath, string? displayName)
    {
        Validate_OrchId(orchId);

        var script = Build_SessionScript("solo", orchId, memberId, $"{Build_ClaudeInvocation(model)} '/solo {orchId}'", pidFilePath);

        return Build_WindowsTerminalCommand(SessionWindowTitle_Builder.Build_Title(SessionWindowTitle_Builder.Build_ForMember(memberId, orchId), displayName), SOLO_TAB_COLOR, repoPath, script);
    }

    /// <summary>The orchestration's green press-secretary voice: narrates, never works (see communicator.md).</summary>
    public static ISpawnCommand Build_ForCommunicator(string orchId, string repoPath, string? model, string pidFilePath, string? displayName)
    {
        Validate_OrchId(orchId);

        var script = Build_SessionScript("communicator", orchId, "com", $"{Build_ClaudeInvocation(model)} '/communicator {orchId}'", pidFilePath);

        return Build_WindowsTerminalCommand(SessionWindowTitle_Builder.Build_Title(SessionWindowTitle_Builder.Build_ForCommunicator(orchId), displayName), COMMUNICATOR_TAB_COLOR, repoPath, script);
    }

    public static ISpawnCommand Build_ForImplementer(string orchId, string memberId, string repoPath, string? model, string pidFilePath, string? displayName)
    {
        Validate_OrchId(orchId);

        var script = Build_SessionScript("implementer", orchId, memberId, $"{Build_ClaudeInvocation(model)} '/implementer {orchId}/{memberId}'", pidFilePath);

        return Build_WindowsTerminalCommand(SessionWindowTitle_Builder.Build_Title(SessionWindowTitle_Builder.Build_ForMember(memberId, orchId), displayName), IMPLEMENTER_TAB_COLOR, repoPath, script);
    }

    /// <summary>
    /// generalHomeFolder is the general supervisor's PERMANENT working directory (its CLAUDE.md home).
    ///
    /// The general supervisor is STATELESS ACROSS LAUNCHES by design (owner directive): every
    /// launch is a FRESH conversation. Its only memory is its CLAUDE.md (role/repo knowledge,
    /// auto-loaded from the working directory) and the channel file its boot re-reads as a LOG.
    /// A '--continue' resume proved harmful: the restored conversation re-executed its own
    /// in-flight plans (a failed start-orchestration was retried on boot → duplicate
    /// orchestrations).
    /// </summary>
    public static ISpawnCommand Build_ForGeneralSupervisor(string generalHomeFolder, string? model, string pidFilePath)
    {
        var script = Build_SessionScript("general", "general", "general", $"{Build_ClaudeInvocation(model)} '/general-supervisor'", pidFilePath);

        return Build_WindowsTerminalCommand(SessionWindowTitle_Builder.GENERAL_TITLE, GENERAL_TAB_COLOR, generalHomeFolder, script);
    }

    /// <summary>Fallback when Windows Terminal (wt.exe) is not installed: a plain PowerShell window.</summary>
    public static ISpawnCommand Build_PowershellFallback(ISpawnCommand windowsTerminalCommand)
    {
        // The wt argument list ends with: "powershell" "-NoProfile" "-ExecutionPolicy" "Bypass" "-Command" <script>
        var arguments = windowsTerminalCommand.Arguments;
        var powershellIndex = Find_PowershellIndex(arguments);

        return SpawnCommand_Factory.Create(
            "powershell.exe",
            [.. arguments.Skip(powershellIndex + 1)],
            windowsTerminalCommand.WorkingDirectory);
    }

    static int Find_PowershellIndex(IReadOnlyList<string> arguments)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] == "powershell")
                return i;
        }

        throw new Exception($"No 'powershell' token found in wt arguments: {string.Join(" ", arguments)}");
    }

    static ISpawnCommand Build_WindowsTerminalCommand(string tabTitle, string tabColor, string workingDirectory, string script)
    {
        // '-w new' gives every session its OWN terminal window: the window title equals the session
        // title, which is what lets the app's "Show session" button find and foreground it.
        //
        // The script goes through -EncodedCommand (base64), NEVER as raw -Command text: wt.exe
        // treats ';' in its command line as a TAB SEPARATOR, so a raw PowerShell script chained
        // with ';' explodes into one broken tab per statement. Base64 contains nothing wt parses.
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        // --suppressApplicationTitle keeps OUR title: Claude Code retitles the terminal once it
        // runs, which would break the app's title-based "Show session" focusing.
        List<string> arguments =
        [
            "-w", "new",
            "new-tab",
            "--title", tabTitle,
            "--suppressApplicationTitle",
            "--tabColor", tabColor,
            "-d", workingDirectory,
            "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encodedScript,
        ];

        return SpawnCommand_Factory.Create("wt.exe", arguments, workingDirectory);
    }

    /// <summary>Decodes the -EncodedCommand payload back to the PowerShell script (used by tests).</summary>
    public static string Decode_SessionScript(ISpawnCommand command)
    {
        var encoded = command.Arguments[command.Arguments.Count - 1];
        return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
    }

    static string Build_SessionScript(string role, string orchId, string memberId, string claudeCommand, string pidFilePath)
    {
        return
            $"$env:AIORCH_ROLE='{role}'; " +
            $"$env:AIORCH_ID='{orchId}'; " +
            $"$env:AIORCH_MEMBER='{memberId}'; " +
            $"Set-Content -LiteralPath '{pidFilePath}' -Value $PID; " +
            claudeCommand;
    }

    static string Build_ClaudeInvocation(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return $"claude {CLAUDE_LAUNCH_FLAGS}";

        Validate_Model(model);

        return $"claude --model {model} {CLAUDE_LAUNCH_FLAGS}";
    }

    /// <summary>
    /// THE MODEL WORD TRAVELS THROUGH A SHELL COMMAND, exactly like the orchestration id below, and
    /// until 2026-09-10 it was the only part of this script that went in unprotected: role, id, member
    /// and pid path are all single-quoted, and the model was interpolated bare into a PowerShell string
    /// that is then base64-encoded and run. A value carrying a quote or a semicolon would have become
    /// PowerShell in every session spawned with it. Found by the re-review of the per-role model keys,
    /// which had just added two more places an owner types this word by hand.
    ///
    /// <para>
    /// VALIDATED RATHER THAN QUOTED, for the reason the id beside it is: quoting a value that may
    /// itself contain a quote only moves the problem, while the alphabet a model name actually uses —
    /// letters, digits, dots and dashes — excludes every character that could end the argument. It
    /// THROWS, naming the value: this is a spawn that must not happen, not a setting that can fall back
    /// (`.claude/rules/code-conventions.md`: invariant violations throw, naming the bad value). The
    /// bridge-driven runners are unaffected either way — they pass --model as a real argument, never
    /// through a shell — so this closes the terminal path, which is the one that builds a script.
    /// </para>
    /// </summary>
    static void Validate_Model(string model)
    {
        foreach (var character in model)
        {
            var valid = char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_' || character == '.';

            if (!valid)
                throw new ArgumentException($"Model '{model}' contains invalid character '{character}' — a model name may hold letters, digits, '-', '_' or '.' (it travels through a shell command)");
        }
    }

    static void Validate_OrchId(string orchId)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException("Orchestration id must be non-empty");

        foreach (var character in orchId)
        {
            var valid = char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_';

            if (!valid)
                throw new ArgumentException($"Orchestration id '{orchId}' contains invalid character '{character}' — use letters, digits, '-' or '_' (it travels through shell commands and folder names)");
        }
    }
}
