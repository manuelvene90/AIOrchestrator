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
/// Resume semantics (owner request 2026-09-10): a SUPERVISOR or SOLO respawn continues its OWN
/// conversation with 'claude --resume &lt;session-id&gt;' when the slot's probe file names a transcript
/// that still exists (see <see cref="ResumableSession_Resolver"/>). The id names one conversation
/// exactly — '--continue' would have guessed the most recent one in a repo directory several
/// sessions share, which is why resuming was ruled out before. The first spawn has no probe file
/// and starts fresh; so does any respawn whose transcript is gone. Implementers and reviewers
/// re-enter through their role command (the channels are their durable state), and the GENERAL
/// supervisor is stateless by owner directive (see <see cref="Build_ForGeneralSupervisor"/>).
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

    /// <summary>
    /// resumeSessionId is the conversation this slot ran before, when there is one to pick up
    /// (see <see cref="ResumableSession_Resolver"/>); null or blank starts fresh.
    /// </summary>
    public static ISpawnCommand Build_ForSupervisor(string orchId, string repoPath, string? model, string? effort, string? resumeSessionId, string pidFilePath, string? displayName)
    {
        Validate_OrchId(orchId);

        var script = Build_SessionScript("supervisor", orchId, "sup", $"{Build_ClaudeInvocation(resumeSessionId, model, effort)} '/supervisor {orchId}'", pidFilePath);

        return Build_WindowsTerminalCommand(SessionWindowTitle_Builder.Build_Title(SessionWindowTitle_Builder.Build_ForSupervisor(orchId), displayName), SUPERVISOR_TAB_COLOR, repoPath, script);
    }

    /// <summary>
    /// A reviewer session: adversarial review by default, no worktree (it reads the repo and the
    /// implementers' branches), and no ability to edit or commit.
    /// </summary>
    public static ISpawnCommand Build_ForReviewer(string orchId, string memberId, string repoPath, string? model, string? effort, string pidFilePath, string? displayName)
    {
        Validate_OrchId(orchId);

        // The '--' is LOAD-BEARING. --disallowedTools is variadic (<tools...>), so without a
        // terminator it swallows the prompt that follows it: the CLI parsed "/reviewer orch/rev-1"
        // as TOOL NAMES and started a session with no prompt at all. Every reviewer therefore came
        // up blank — never booted, never wrote to its channel, and was nudged then respawned on a
        // loop. Verified against the real CLI, which reports "Permission deny rule ... matches no
        // known tool" for each swallowed word. The model and effort flags sit BEFORE it for the
        // same reason — placed after, they would be eaten as tool names too.
        var claudeCommand = $"{Build_ClaudeInvocation(null, model, effort)} {REVIEWER_LAUNCH_FLAGS} -- '/reviewer {orchId}/{memberId}'";
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
    public static ISpawnCommand Build_ForSolo(string orchId, string memberId, string repoPath, string? model, string? effort, string? resumeSessionId, string pidFilePath, string? displayName)
    {
        Validate_OrchId(orchId);

        var script = Build_SessionScript("solo", orchId, memberId, $"{Build_ClaudeInvocation(resumeSessionId, model, effort)} '/solo {orchId}'", pidFilePath);

        return Build_WindowsTerminalCommand(SessionWindowTitle_Builder.Build_Title(SessionWindowTitle_Builder.Build_ForMember(memberId, orchId), displayName), SOLO_TAB_COLOR, repoPath, script);
    }

    /// <summary>
    /// The orchestration's green press-secretary voice: narrates, never works (see communicator.md).
    ///
    /// <para>
    /// IT TAKES AN EFFORT LIKE EVERY OTHER ROLE since 2026-09-12 (task-7 fix round 1). It did not, and
    /// hardcoded null into <see cref="Build_ClaudeInvocation"/> — so from the moment the launcher began
    /// resolving <c>effort.communicator</c>, an owner who wrote that key had it resolved correctly and
    /// then dropped here: no flag, no warning, no log line. Silent discard is the failure class this
    /// project refuses (CLAUDE.md decision 21). Both shipped presets leave the key null, so the emitted
    /// line is unchanged for everyone who never wrote it.
    /// </para>
    /// </summary>
    public static ISpawnCommand Build_ForCommunicator(string orchId, string repoPath, string? model, string? effort, string pidFilePath, string? displayName)
    {
        Validate_OrchId(orchId);

        var script = Build_SessionScript("communicator", orchId, "com", $"{Build_ClaudeInvocation(null, model, effort)} '/communicator {orchId}'", pidFilePath);

        return Build_WindowsTerminalCommand(SessionWindowTitle_Builder.Build_Title(SessionWindowTitle_Builder.Build_ForCommunicator(orchId), displayName), COMMUNICATOR_TAB_COLOR, repoPath, script);
    }

    public static ISpawnCommand Build_ForImplementer(string orchId, string memberId, string repoPath, string? model, string? effort, string pidFilePath, string? displayName)
    {
        Validate_OrchId(orchId);

        var script = Build_SessionScript("implementer", orchId, memberId, $"{Build_ClaudeInvocation(null, model, effort)} '/implementer {orchId}/{memberId}'", pidFilePath);

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
    ///
    /// <para>
    /// STATELESS IS ABOUT THE CONVERSATION, NOT ABOUT THE DIALS: it takes an effort like every other
    /// role since 2026-09-12 (task-7 fix round 1), for the reason given on
    /// <see cref="Build_ForCommunicator"/> — the launcher resolved <c>effort.general</c> and this
    /// method hardcoded null over it, which is a setting that answers and is then thrown away.
    /// </para>
    /// </summary>
    public static ISpawnCommand Build_ForGeneralSupervisor(string generalHomeFolder, string? model, string? effort, string pidFilePath)
    {
        var script = Build_SessionScript("general", "general", "general", $"{Build_ClaudeInvocation(null, model, effort)} '/general-supervisor'", pidFilePath);

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

    /// <summary>
    /// All three parts are OPTIONAL and independent. A null or blank resume id starts a fresh
    /// conversation; a null or blank effort emits NO --effort flag at all, so the CLI applies its own
    /// default, which is not a level called "default" and cannot be named.
    ///
    /// <para>
    /// THIS BUILDER DECIDES NO EFFORT OF ITS OWN — it carries what it is handed, and it stopped
    /// deciding on 2026-09-12. It used to own a <c>SUPERVISION_EFFORT_LEVEL = "xhigh"</c> constant
    /// applied to the supervisor and the solo whenever the caller passed none: the owner's directive of
    /// 2026-09-09 ("XHigh effort in each solo and sup session"), living in CODE while the model on the
    /// same command line was read live from config.json — so moving it needed a rebuilt app actually
    /// running (CLAUDE.md decision 23). That role default is a catalogue entry now
    /// (<c>effort.supervisor</c> / <c>effort.solo</c>, carried at xhigh by the <c>classic</c> preset)
    /// and is resolved by <c>OrchestrationLauncherModel</c>, the one place that holds both the
    /// per-orchestration <c>/effort</c> override and the config provider. The emitted line is
    /// byte-identical under classic; only the decision moved. The levels the installed CLI accepts are
    /// the ones <c>EffortLevels</c> lists — its --help gives (low, medium, high, xhigh, max).
    /// </para>
    /// <para>
    /// Order is resume, then model, then effort, then the launch flags, always ahead of the prompt
    /// — and ahead of the reviewer's variadic --disallowedTools, which would otherwise swallow them
    /// as tool names. The dials come AFTER the resume on purpose: a model or effort the owner turned
    /// while the session was down applies to the resumed conversation (verified against the
    /// installed CLI, whose --help lists --model and --effort as per-session, alongside --resume).
    /// </para>
    /// </summary>
    static string Build_ClaudeInvocation(string? resumeSessionId, string? model, string? effort)
    {
        var resumePart = string.IsNullOrWhiteSpace(resumeSessionId) ? string.Empty : $" --resume {Validate_ResumeSessionId(resumeSessionId)}";
        var modelPart = string.IsNullOrWhiteSpace(model) ? string.Empty : $" --model {Validate_Model(model)}";
        var effortPart = string.IsNullOrWhiteSpace(effort) ? string.Empty : $" --effort {effort}";
        return $"claude{resumePart}{modelPart}{effortPart} {CLAUDE_LAUNCH_FLAGS}";
    }

    /// <summary>
    /// The id was read from a file another process wrote and is quoted into a PowerShell command
    /// line: only the CLI's own id shape (a UUID in 8-4-4-4-12 form) is ever emitted. The resolver
    /// refuses anything else upstream; this is the second lock on the same door.
    /// </summary>
    static string Validate_ResumeSessionId(string resumeSessionId)
    {
        if (!ResumableSession_Resolver.Is_ClaudeSessionId(resumeSessionId))
            throw new ArgumentException($"Resume session id '{resumeSessionId}' is not a UUID in 8-4-4-4-12 form — it travels through a shell command, so only the CLI's own id shape is accepted");

        return resumeSessionId;
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
    /// <para>
    /// IT RETURNS THE WORD IT CHECKED, the shape <see cref="Validate_ResumeSessionId"/> above already
    /// uses, so the check sits INSIDE the interpolation that emits the flag. A separate statement
    /// guarded by its own <c>IsNullOrWhiteSpace</c> would be a second copy of the emission's condition,
    /// and the two could drift apart — which is the whole failure mode this method exists to close.
    /// </para>
    /// </summary>
    static string Validate_Model(string model)
    {
        var invalid = First_InvalidModelCharacter_OrNull(model);

        if (invalid != null)
            throw new ArgumentException($"Model '{model}' contains invalid character '{invalid}' — a model name may hold letters, digits, '-', '_' or '.' (it travels through a shell command)");

        return model;
    }

    /// <summary>
    /// The first character of <paramref name="model"/> that a model word may not hold, or null when
    /// every character is legal: letters, digits, '-', '_' and '.'. PUBLIC and separate from
    /// <see cref="Validate_Model"/> because there are now two reactions to the same fact and only one
    /// may own the charset (CLAUDE.md decision 12, the rule about a second copy of a formatter): a
    /// SPAWN throws, because a model word travels through a shell command line and a spawn that would
    /// break out of its quoting must not happen; a SETTINGS RENDERER returns a message naming the
    /// character, because refusing the owner's typing without saying why is the silence decision 21 is
    /// about. Added 2026-09-12 with the settings catalogue, whose `models.*` entries validate through it.
    /// </summary>
    public static char? First_InvalidModelCharacter_OrNull(string model)
    {
        foreach (var character in model)
        {
            var valid = char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_' || character == '.';

            if (!valid)
                return character;
        }

        return null;
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
