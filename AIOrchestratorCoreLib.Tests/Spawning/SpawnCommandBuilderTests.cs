using AIOrchestratorCoreLib.Spawning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Spawning;

public class SpawnCommandBuilderTests
{
    const string PID_FILE = @"C:\Users\x\.claude\supervision\arb-fix\.supervisor.pid";

    [Fact]
    public void Build_ForSupervisor_CarriesTitleColorDirectoryPidFileAndScript()
    {
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "opus", null, null, PID_FILE, null);

        Assert.Equal("wt.exe", command.Executable);
        Assert.Contains("SUP · arb-fix", command.Arguments);
        Assert.Contains(SpawnCommand_Builder.SUPERVISOR_TAB_COLOR, command.Arguments);
        Assert.Contains(@"C:\repos\arb", command.Arguments);

        var script = SpawnCommand_Builder.Decode_SessionScript(command);
        Assert.Contains("$env:AIORCH_ROLE='supervisor'", script);
        Assert.Contains("$env:AIORCH_ID='arb-fix'", script);
        Assert.Contains($"Set-Content -LiteralPath '{PID_FILE}' -Value $PID", script);
        Assert.Contains("claude --model opus --effort xhigh --dangerously-skip-permissions '/supervisor arb-fix'", script);
    }

    /// <summary>
    /// The owner asked for xhigh in the two roles that talk to them and decide (2026-09-09). With
    /// NO override set, that is the role default, and it sits before --dangerously-skip-permissions
    /// and before the slash command, or the CLI reads it as part of the prompt.
    /// </summary>
    [Fact]
    public void Build_SupervisorAndSolo_ThinkAtXHighEffort_ByDefault()
    {
        var supervisor = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "claude-fable-5-1", null, null, PID_FILE, null);
        var solo = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", "claude-fable-5-1", null, null, PID_FILE, null);

        Assert.Contains(
            "claude --model claude-fable-5-1 --effort xhigh --dangerously-skip-permissions '/supervisor arb-fix'",
            SpawnCommand_Builder.Decode_SessionScript(supervisor));
        Assert.Contains(
            "claude --model claude-fable-5-1 --effort xhigh --dangerously-skip-permissions '/solo arb-fix'",
            SpawnCommand_Builder.Decode_SessionScript(solo));
    }

    /// <summary>
    /// Effort is billed thinking, so the roles the owner did NOT name keep the CLI's own default.
    /// The reviewer is the one that would break loudly: its --disallowedTools is variadic, so an
    /// effort flag emitted after it would be eaten as a tool name.
    /// </summary>
    [Fact]
    public void Build_RolesTheOwnerDidNotName_CarryNoEffortFlag()
    {
        var implementer = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-1", @"C:\repos\arb", "claude-fable-5-1", null, PID_FILE, null);
        var reviewer = SpawnCommand_Builder.Build_ForReviewer("arb-fix", "rev-1", @"C:\repos\arb", "claude-fable-5-1", null, PID_FILE, null);
        var communicator = SpawnCommand_Builder.Build_ForCommunicator("arb-fix", @"C:\repos\arb", "sonnet", PID_FILE, null);
        var general = SpawnCommand_Builder.Build_ForGeneralSupervisor(@"C:\Users\x\.claude\supervision\general", "sonnet", PID_FILE);

        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(implementer));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(reviewer));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(communicator));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(general));
    }

    /// <summary>
    /// The per-orchestration effort override lands as `--effort`, AFTER the model and BEFORE the
    /// launch flags and the slash command — anything after the prompt would be read as part of it.
    /// </summary>
    [Fact]
    public void Build_ForSupervisor_WithEffortOverride_EmitsEffortRightAfterTheModel()
    {
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "opus", "xhigh", null, PID_FILE, null);

        var script = SpawnCommand_Builder.Decode_SessionScript(command);
        Assert.Contains("claude --model opus --effort xhigh --dangerously-skip-permissions '/supervisor arb-fix'", script);
    }

    [Fact]
    public void Build_EveryOverridableRole_WithEffort_CarriesTheFlagBeforeTheLaunchFlags()
    {
        var implementer = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-2", @"C:\repos\arb", "opus", "xhigh", PID_FILE, null);
        var solo = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", "opus", "xhigh", null, PID_FILE, null);
        var reviewer = SpawnCommand_Builder.Build_ForReviewer("arb-fix", "rev-1", @"C:\repos\arb", "opus", "xhigh", PID_FILE, null);

        Assert.Contains(
            "claude --model opus --effort xhigh --dangerously-skip-permissions '/implementer arb-fix/imp-2'",
            SpawnCommand_Builder.Decode_SessionScript(implementer));
        Assert.Contains(
            "claude --model opus --effort xhigh --dangerously-skip-permissions '/solo arb-fix'",
            SpawnCommand_Builder.Decode_SessionScript(solo));

        // The reviewer's '--' terminator stays AFTER every flag: --disallowedTools is variadic and
        // would otherwise swallow the prompt (or an effort flag placed after it) as tool names.
        Assert.Contains(
            $"claude --model opus --effort xhigh --dangerously-skip-permissions {SpawnCommand_Builder.REVIEWER_LAUNCH_FLAGS} -- '/reviewer arb-fix/rev-1'",
            SpawnCommand_Builder.Decode_SessionScript(reviewer));
    }

    /// <summary>
    /// With no override, the two roles the owner named get the ROLE DEFAULT and the others get NO
    /// FLAG AT ALL, so the CLI applies its own. A blank override counts as unset: `--effort ` with
    /// nothing after it would make the CLI eat the next token.
    /// </summary>
    [Fact]
    public void Build_WithoutEffortOverride_TheRoleDefaultDecides()
    {
        var supervisor = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "opus", "   ", null, PID_FILE, null);
        var implementer = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-2", @"C:\repos\arb", "opus", "   ", PID_FILE, null);
        var solo = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", null, null, null, PID_FILE, null);
        var reviewer = SpawnCommand_Builder.Build_ForReviewer("arb-fix", "rev-1", @"C:\repos\arb", null, string.Empty, PID_FILE, null);

        Assert.Contains("claude --model opus --effort xhigh --dangerously-skip-permissions '/supervisor arb-fix'", SpawnCommand_Builder.Decode_SessionScript(supervisor));
        Assert.Contains("claude --effort xhigh --dangerously-skip-permissions '/solo arb-fix'", SpawnCommand_Builder.Decode_SessionScript(solo));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(implementer));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(reviewer));
    }

    /// <summary>
    /// The dial the owner turns from the phone (/effort) BEATS the role default — otherwise a solo
    /// could never be told to think at anything but xhigh.
    /// </summary>
    [Fact]
    public void Build_SupervisorAndSolo_AnOverrideBeatsTheRoleDefault()
    {
        var supervisor = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "opus", "medium", null, PID_FILE, null);
        var solo = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", "opus", "low", null, PID_FILE, null);

        Assert.Contains("claude --model opus --effort medium --dangerously-skip-permissions '/supervisor arb-fix'", SpawnCommand_Builder.Decode_SessionScript(supervisor));
        Assert.Contains("claude --model opus --effort low --dangerously-skip-permissions '/solo arb-fix'", SpawnCommand_Builder.Decode_SessionScript(solo));
        Assert.DoesNotContain("xhigh", SpawnCommand_Builder.Decode_SessionScript(supervisor));
        Assert.DoesNotContain("xhigh", SpawnCommand_Builder.Decode_SessionScript(solo));
    }

    /// <summary>The two overrides are independent: an effort with no model override is a real case.</summary>
    [Fact]
    public void Build_ForImplementer_EffortWithoutModel_StillEmitsTheEffortFlag()
    {
        var command = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-2", @"C:\repos\arb", null, "high", PID_FILE, null);

        var script = SpawnCommand_Builder.Decode_SessionScript(command);
        Assert.Contains("claude --effort high --dangerously-skip-permissions '/implementer arb-fix/imp-2'", script);
        Assert.DoesNotContain("--model", script);
    }

    [Fact]
    public void Build_EverySession_SkipsPermissionPrompts_UnattendedByDesign()
    {
        var supervisor = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", null, null, null, PID_FILE, null);
        var implementer = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-1", @"C:\repos\arb", null, null, PID_FILE, null);
        var general = SpawnCommand_Builder.Build_ForGeneralSupervisor(@"C:\Users\x\.claude\supervision\general", null, PID_FILE);

        Assert.Contains(SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS, SpawnCommand_Builder.Decode_SessionScript(supervisor));
        Assert.Contains(SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS, SpawnCommand_Builder.Decode_SessionScript(implementer));
        Assert.Contains(SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS, SpawnCommand_Builder.Decode_SessionScript(general));
    }

    [Fact]
    public void Build_AnyCommand_NeverPassesRawScriptText_WtSplitsTabsOnSemicolons()
    {
        var command = SpawnCommand_Builder.Build_ForGeneralSupervisor(@"C:\Users\x\.claude\supervision\general", "sonnet", PID_FILE);

        Assert.Contains("-EncodedCommand", command.Arguments);
        Assert.DoesNotContain(command.Arguments, argument => argument.Contains(';', StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ForSupervisor_SpawnsInItsOwnTerminalWindow()
    {
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", null, null, null, PID_FILE, null);

        // '-w new' → own window, whose title the app's "Show session" focuser matches on.
        Assert.Equal("-w", command.Arguments[0]);
        Assert.Equal("new", command.Arguments[1]);
    }

    [Fact]
    public void Build_ForImplementer_CarriesMemberIdentityAndSlashCommand()
    {
        var command = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-2", @"C:\repos\arb", null, null, PID_FILE, null);

        Assert.Contains("IMP-2 · arb-fix", command.Arguments);
        Assert.Contains(SpawnCommand_Builder.IMPLEMENTER_TAB_COLOR, command.Arguments);

        var script = SpawnCommand_Builder.Decode_SessionScript(command);
        Assert.Contains("$env:AIORCH_MEMBER='imp-2'", script);
        Assert.Contains("claude --dangerously-skip-permissions '/implementer arb-fix/imp-2'", script);
        Assert.DoesNotContain("--model", script);
    }

    [Fact]
    public void Build_ForGeneralSupervisor_AlwaysStartsFresh_StatelessAcrossLaunches()
    {
        var command = SpawnCommand_Builder.Build_ForGeneralSupervisor(@"C:\Users\x\.claude\supervision\general", "sonnet", PID_FILE);

        Assert.Contains("GENERAL", command.Arguments);
        Assert.Contains(SpawnCommand_Builder.GENERAL_TAB_COLOR, command.Arguments);

        var script = SpawnCommand_Builder.Decode_SessionScript(command);

        // Fresh conversation every launch — a --continue resume re-executed its own in-flight
        // plans (a failed start was retried on boot → duplicate orchestrations). Memory lives in
        // its CLAUDE.md and the channel file, never in the conversation.
        Assert.Contains("claude --model sonnet --dangerously-skip-permissions '/general-supervisor'", script);
        Assert.DoesNotContain("--continue", script);
        Assert.DoesNotContain("--resume", script);
        Assert.DoesNotContain("$LASTEXITCODE", script);
    }

    [Fact]
    public void Build_AnyCommand_SuppressesApplicationTitle_SoShowSessionFocusingWorks()
    {
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", null, null, null, PID_FILE, null);

        Assert.Contains("--suppressApplicationTitle", command.Arguments);
    }

    [Fact]
    public void Build_ForSupervisor_OrchIdWithShellHostileCharacters_Throws()
    {
        Assert.Throws<ArgumentException>(() => SpawnCommand_Builder.Build_ForSupervisor("arb fix'; rm -rf", @"C:\repos\arb", null, null, null, PID_FILE, null));
    }

    [Fact]
    public void Build_PowershellFallback_KeepsScriptDropsWindowsTerminalArguments()
    {
        var wtCommand = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", null, null, null, PID_FILE, null);

        var fallback = SpawnCommand_Builder.Build_PowershellFallback(wtCommand);

        Assert.Equal("powershell.exe", fallback.Executable);
        Assert.DoesNotContain("new-tab", fallback.Arguments);
        Assert.Contains("-NoProfile", fallback.Arguments);
        Assert.Equal(wtCommand.Arguments[wtCommand.Arguments.Count - 1], fallback.Arguments[fallback.Arguments.Count - 1]);
        Assert.Equal(@"C:\repos\arb", fallback.WorkingDirectory);
    }

    const string RESUME_ID = "504fb5ee-d79d-410e-9876-8fb937949dfe";

    /// <summary>
    /// Owner request 2026-09-10: a restarted solo or supervisor picks up its OWN conversation rather
    /// than booting as a new session. The id names one conversation exactly — unlike `--continue`,
    /// which guesses the most recent one in a directory several sessions share, and which is why
    /// resuming was ruled out before. It sits FIRST, ahead of the model and effort dials the owner
    /// may have turned in the meantime (they still apply to the resumed conversation), and ahead of
    /// the launch flags and the prompt like every other flag.
    /// </summary>
    [Fact]
    public void Build_SupervisorAndSolo_WithAResumableConversation_ResumeIt_AheadOfEveryOtherFlag()
    {
        var supervisor = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "opus", "medium", RESUME_ID, PID_FILE, null);
        var solo = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", null, null, RESUME_ID, PID_FILE, null);

        Assert.Contains(
            $"claude --resume {RESUME_ID} --model opus --effort medium --dangerously-skip-permissions '/supervisor arb-fix'",
            SpawnCommand_Builder.Decode_SessionScript(supervisor));
        Assert.Contains(
            $"claude --resume {RESUME_ID} --effort xhigh --dangerously-skip-permissions '/solo arb-fix'",
            SpawnCommand_Builder.Decode_SessionScript(solo));
    }

    /// <summary>Nothing to resume — the first spawn, or a transcript that is gone — is the command line exactly as it always was.</summary>
    [Fact]
    public void Build_SupervisorAndSolo_WithNothingToResume_StartFresh_ExactlyAsBefore()
    {
        var supervisor = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "opus", null, null, PID_FILE, null);
        var solo = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", "opus", null, "   ", PID_FILE, null);

        var supervisorScript = SpawnCommand_Builder.Decode_SessionScript(supervisor);
        var soloScript = SpawnCommand_Builder.Decode_SessionScript(solo);

        Assert.Contains("claude --model opus --effort xhigh --dangerously-skip-permissions '/supervisor arb-fix'", supervisorScript);
        Assert.Contains("claude --model opus --effort xhigh --dangerously-skip-permissions '/solo arb-fix'", soloScript);
        Assert.DoesNotContain("--resume", supervisorScript);
        Assert.DoesNotContain("--resume", soloScript);
    }

    /// <summary>
    /// The id is read from a file another process wrote and is quoted into a PowerShell command
    /// line, so only the CLI's own id shape — a UUID in 8-4-4-4-12 form — is ever emitted. The
    /// resolver refuses anything else upstream; this is the second lock on the same door.
    /// </summary>
    [Fact]
    public void Build_WithAResumeIdThatIsNotAUuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", null, null, "7f34ac2b", PID_FILE, null));
        Assert.Throws<ArgumentException>(() => SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", null, null, "abc'; Remove-Item -Recurse C:\\", PID_FILE, null));
        Assert.Throws<ArgumentException>(() => SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", null, null, "{504fb5ee-d79d-410e-9876-8fb937949dfe}", PID_FILE, null));
    }
}
