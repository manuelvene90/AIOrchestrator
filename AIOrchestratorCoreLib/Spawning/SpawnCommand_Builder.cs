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

    /// <summary>
    /// Orchestrated sessions run unattended (the owner may be on their phone) — a permission
    /// prompt would hang the whole loop, so every session skips them (owner directive).
    /// </summary>
    public const string CLAUDE_LAUNCH_FLAGS = "--dangerously-skip-permissions";

    public static ISpawnCommand Build_ForSupervisor(string orchId, string repoPath, string? model, string pidFilePath)
    {
        Validate_OrchId(orchId);

        var script = Build_SessionScript("supervisor", orchId, "sup", $"{Build_ClaudeInvocation(model)} '/supervisor {orchId}'", pidFilePath);

        return Build_WindowsTerminalCommand($"SUP · {orchId}", SUPERVISOR_TAB_COLOR, repoPath, script);
    }

    /// <summary>The orchestration's green press-secretary voice: narrates, never works (see communicator.md).</summary>
    public static ISpawnCommand Build_ForCommunicator(string orchId, string repoPath, string? model, string pidFilePath)
    {
        Validate_OrchId(orchId);

        var script = Build_SessionScript("communicator", orchId, "com", $"{Build_ClaudeInvocation(model)} '/communicator {orchId}'", pidFilePath);

        return Build_WindowsTerminalCommand($"COM · {orchId}", COMMUNICATOR_TAB_COLOR, repoPath, script);
    }

    public static ISpawnCommand Build_ForImplementer(string orchId, string memberId, string repoPath, string? model, string pidFilePath)
    {
        Validate_OrchId(orchId);

        var script = Build_SessionScript("implementer", orchId, memberId, $"{Build_ClaudeInvocation(model)} '/implementer {orchId}/{memberId}'", pidFilePath);

        return Build_WindowsTerminalCommand($"{memberId.ToUpperInvariant()} · {orchId}", IMPLEMENTER_TAB_COLOR, repoPath, script);
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

        return Build_WindowsTerminalCommand("GENERAL", GENERAL_TAB_COLOR, generalHomeFolder, script);
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
        var modelPart = string.IsNullOrWhiteSpace(model) ? string.Empty : $" --model {model}";
        return $"claude{modelPart} {CLAUDE_LAUNCH_FLAGS}";
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
