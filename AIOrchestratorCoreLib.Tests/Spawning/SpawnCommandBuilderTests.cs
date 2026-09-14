using AIOrchestratorCoreLib.Spawning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Spawning;

public class SpawnCommandBuilderTests
{
    const string PID_FILE = @"C:\Users\x\.claude\supervision\arb-fix\.supervisor.pid";

    [Fact]
    public void Build_ForSupervisor_CarriesTitleColorDirectoryPidFileAndScript()
    {
        var command = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "opus", "xhigh", null, PID_FILE, null);

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
    /// THIS PINS THE CARRYING, NOT THE CHOOSING (2026-09-12). The owner asked for xhigh in the two
    /// roles that talk to them and decide (2026-09-09), and until this date that ROLE DEFAULT was a
    /// constant inside this builder; it is a catalogue entry now (<c>effort.supervisor</c> /
    /// <c>effort.solo</c>, carried at xhigh by the <c>classic</c> preset) and the LAUNCHER resolves
    /// it. What is still this builder's own is PLACEMENT: the flag sits before
    /// --dangerously-skip-permissions and before the slash command, or the CLI reads it as part of
    /// the prompt.
    /// </summary>
    [Fact]
    public void Build_SupervisorAndSolo_CarryTheEffortTheyAreHanded()
    {
        var supervisor = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "claude-fable-5-1", "xhigh", null, PID_FILE, null);
        var solo = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", "claude-fable-5-1", "xhigh", null, PID_FILE, null);

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
        var communicator = SpawnCommand_Builder.Build_ForCommunicator("arb-fix", @"C:\repos\arb", "sonnet", null, PID_FILE, null);
        var general = SpawnCommand_Builder.Build_ForGeneralSupervisor(@"C:\Users\x\.claude\supervision\general", "sonnet", null, PID_FILE);

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
    /// THE NEW TRUTH OF THE BUILDER (2026-09-12): handed no effort it emits NO FLAG FOR ANY ROLE,
    /// supervisor and solo included. It has no role default of its own any more — the one the owner
    /// named is resolved by the launcher from <c>effort.supervisor</c> / <c>effort.solo</c> — so a
    /// null arriving here really does mean "the CLI's own default". A blank counts as unset for the
    /// reason it always did: `--effort ` with nothing after it would make the CLI eat the next token.
    /// </summary>
    [Fact]
    public void Build_WithoutAnEffort_EmitsNoFlagForAnyRole()
    {
        var supervisor = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "opus", "   ", null, PID_FILE, null);
        var implementer = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-2", @"C:\repos\arb", "opus", "   ", PID_FILE, null);
        var solo = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", null, null, null, PID_FILE, null);
        var reviewer = SpawnCommand_Builder.Build_ForReviewer("arb-fix", "rev-1", @"C:\repos\arb", null, string.Empty, PID_FILE, null);

        Assert.Contains("claude --model opus --dangerously-skip-permissions '/supervisor arb-fix'", SpawnCommand_Builder.Decode_SessionScript(supervisor));
        Assert.Contains("claude --dangerously-skip-permissions '/solo arb-fix'", SpawnCommand_Builder.Decode_SessionScript(solo));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(supervisor));
        Assert.DoesNotContain("--effort", SpawnCommand_Builder.Decode_SessionScript(solo));
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
        var general = SpawnCommand_Builder.Build_ForGeneralSupervisor(@"C:\Users\x\.claude\supervision\general", null, null, PID_FILE);

        Assert.Contains(SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS, SpawnCommand_Builder.Decode_SessionScript(supervisor));
        Assert.Contains(SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS, SpawnCommand_Builder.Decode_SessionScript(implementer));
        Assert.Contains(SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS, SpawnCommand_Builder.Decode_SessionScript(general));
    }

    [Fact]
    public void Build_AnyCommand_NeverPassesRawScriptText_WtSplitsTabsOnSemicolons()
    {
        var command = SpawnCommand_Builder.Build_ForGeneralSupervisor(@"C:\Users\x\.claude\supervision\general", "sonnet", null, PID_FILE);

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
        var command = SpawnCommand_Builder.Build_ForGeneralSupervisor(@"C:\Users\x\.claude\supervision\general", "sonnet", null, PID_FILE);

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
        var solo = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", null, "xhigh", RESUME_ID, PID_FILE, null);

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
        var supervisor = SpawnCommand_Builder.Build_ForSupervisor("arb-fix", @"C:\repos\arb", "opus", "xhigh", null, PID_FILE, null);
        var solo = SpawnCommand_Builder.Build_ForSolo("arb-fix", "solo-1", @"C:\repos\arb", "opus", "xhigh", "   ", PID_FILE, null);

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

    /// <summary>
    /// THE ORDER IS THE CONTRACT: the resume names the conversation, then the model and the effort
    /// the owner may have turned while the session was down apply TO that conversation, then the
    /// launch flags, then the prompt. The launch flags are part of the asserted string on purpose —
    /// a flag emitted after them still reads as a flag to a person and as prompt text to the CLI.
    /// </summary>
    [Fact]
    public void ASupervisorRespawn_ResumesItsOwnConversation_AndCarriesTheRoleEffort()
    {
        var command = SpawnCommand_Builder.Build_ForSupervisor("repo-3", @"C:\repo", "claude-fable-5-1", effort: "xhigh",
            resumeSessionId: "0f1e2d3c-4b5a-6978-8a9b-0c1d2e3f4a5b", pidFilePath: @"C:\pid", displayName: null);

        var script = SpawnCommand_Builder.Decode_SessionScript(command);

        Assert.Contains(
            $"claude --resume 0f1e2d3c-4b5a-6978-8a9b-0c1d2e3f4a5b --model claude-fable-5-1 --effort xhigh {SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS} '/supervisor repo-3'",
            script);
    }

    /// <summary>
    /// The mirror of the case above, and the reason resume is a parameter of two builders rather
    /// than of all of them: an implementer re-enters through its role command (the channel is its
    /// durable state, CLAUDE.md decision 8), and effort is billed thinking the owner named two roles
    /// for. Both absences are asserted, because either one appearing by default is a bill.
    /// </summary>
    [Fact]
    public void AnImplementer_NeverResumes_AndHasNoEffortUnlessOverridden()
    {
        var command = SpawnCommand_Builder.Build_ForImplementer("repo-3", "imp-1", @"C:\repo", "sonnet", effort: null, pidFilePath: @"C:\pid", displayName: null);

        var script = SpawnCommand_Builder.Decode_SessionScript(command);

        Assert.DoesNotContain("--resume", script);
        Assert.DoesNotContain("--effort", script);
        Assert.Contains("--model sonnet", script);
    }

    /// <summary>
    /// THE MODEL WORD CANNOT BECOME POWERSHELL. Everything else this script interpolates is
    /// single-quoted; the model was bare until 2026-09-10, so a value carrying a quote or a semicolon
    /// would have run as a command in every session spawned with it. The alphabet is the same one the
    /// orchestration id is held to, and for the same stated reason: it travels through a shell.
    /// </summary>
    [Theory]
    [InlineData("opus'; Remove-Item C:\\x; '")]
    [InlineData("sonnet; whoami")]
    [InlineData("opus`nwhoami")]
    [InlineData("opus $(id)")]
    [InlineData("opus|tee /tmp/x")]
    public void AModelCarryingShellPunctuation_IsRefused_NamingTheValue(string model)
    {
        var refusal = Assert.Throws<ArgumentException>(() =>
            SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-1", @"C:\repos\arb", model, null, PID_FILE, null));

        Assert.Contains(model, refusal.Message);
        Assert.Contains("travels through a shell command", refusal.Message);
    }

    [Theory]
    [InlineData("opus")]
    [InlineData("sonnet")]
    [InlineData("claude-opus-5")]
    [InlineData("claude-sonnet-4.5")]
    [InlineData("some_internal.alias-9")]
    public void AModelSpelledTheWayModelsAreSpelled_IsAccepted(string model)
    {
        var command = SpawnCommand_Builder.Build_ForImplementer("arb-fix", "imp-1", @"C:\repos\arb", model, null, PID_FILE, null);
        var script = SpawnCommand_Builder.Decode_SessionScript(command);

        Assert.Contains($"claude --model {model} ", script);
    }
}
