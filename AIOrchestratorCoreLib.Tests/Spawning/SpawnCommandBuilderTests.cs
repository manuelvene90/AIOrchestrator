using AIOrchestratorCoreLib.Spawning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Spawning;

public class SpawnCommandBuilderTests
{
    const string PID_FILE = @"C:\Users\x\.claude\supervision\arb-fix\.supervisor.pid";

    [Fact]
    public void Build_ForSupervisor_CarriesTitleColorDirectoryPidFileAndScript()
    {
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "opus", PID_FILE, null);

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
    /// The owner asked for xhigh in the two roles that talk to them and decide (2026-09-09). The
    /// flag must sit before --dangerously-skip-permissions and before the slash command, or the
    /// CLI reads it as part of the prompt.
    /// </summary>
    [Fact]
    public void Build_SupervisorAndSolo_ThinkAtXHighEffort()
    {
        var supervisor = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "claude-fable-5-1", PID_FILE, null);
        var solo = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", "claude-fable-5-1", PID_FILE, null);

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
        var implementer = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-1", @"C:\repos\arb", "claude-fable-5-1", PID_FILE, null);
        var reviewer = SpawnCommand_Builder.Build_ForReviewer("arb-fix", "rev-1", @"C:\repos\arb", "claude-fable-5-1", PID_FILE, null);
        var communicator = SpawnCommand_Builder.Build_ForCommunicator("arb-fix", @"C:\repos\arb", "sonnet", PID_FILE, null);
        var general = SpawnCommand_Builder.Build_ForGeneralSupervisor(@"C:\Users\x\.claude\supervision\general", "sonnet", PID_FILE);

        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(implementer));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(reviewer));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(communicator));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(general));
    }

    [Fact]
    public void Build_EverySession_SkipsPermissionPrompts_UnattendedByDesign()
    {
        var supervisor = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", null, PID_FILE, null);
        var implementer = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-1", @"C:\repos\arb", null, PID_FILE, null);
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
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", null, PID_FILE, null);

        // '-w new' → own window, whose title the app's "Show session" focuser matches on.
        Assert.Equal("-w", command.Arguments[0]);
        Assert.Equal("new", command.Arguments[1]);
    }

    [Fact]
    public void Build_ForImplementer_CarriesMemberIdentityAndSlashCommand()
    {
        var command = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-2", @"C:\repos\arb", null, PID_FILE, null);

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
        Assert.DoesNotContain("$LASTEXITCODE", script);
    }

    [Fact]
    public void Build_AnyCommand_SuppressesApplicationTitle_SoShowSessionFocusingWorks()
    {
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", null, PID_FILE, null);

        Assert.Contains("--suppressApplicationTitle", command.Arguments);
    }

    [Fact]
    public void Build_ForSupervisor_OrchIdWithShellHostileCharacters_Throws()
    {
        Assert.Throws<ArgumentException>(() => SpawnCommand_Builder.Build_ForSupervisor("arb fix'; rm -rf", @"C:\repos\arb", null, PID_FILE, null));
    }

    [Fact]
    public void Build_PowershellFallback_KeepsScriptDropsWindowsTerminalArguments()
    {
        var wtCommand = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", null, PID_FILE, null);

        var fallback = SpawnCommand_Builder.Build_PowershellFallback(wtCommand);

        Assert.Equal("powershell.exe", fallback.Executable);
        Assert.DoesNotContain("new-tab", fallback.Arguments);
        Assert.Contains("-NoProfile", fallback.Arguments);
        Assert.Equal(wtCommand.Arguments[wtCommand.Arguments.Count - 1], fallback.Arguments[fallback.Arguments.Count - 1]);
        Assert.Equal(@"C:\repos\arb", fallback.WorkingDirectory);
    }
}
