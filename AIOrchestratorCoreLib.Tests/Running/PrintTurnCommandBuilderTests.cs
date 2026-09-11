using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>The argument list the Live contract tests pin, built the way the dispatcher builds it.</summary>
public class PrintTurnCommandBuilderTests
{
    const string SESSION_ID = "11111111-2222-3333-4444-555555555555";

    static IPrintSessionState State(SessionRoles role, string? model = "opus")
    {
        return PrintSessionState_Factory.Create_New(SESSION_ID, role, "orch-1", role == SessionRoles.General ? "general" : "imp-1", "/repo", model, "/repo/channel.md", []);
    }

    [Fact]
    public void FirstTurn_UsesSessionId_AndTheRoleCommandAsPositionalPrompt()
    {
        var arguments = PrintTurnCommand_Builder.Build_Arguments(State(SessionRoles.Implementer), RoleRunnerConfig_Factory.Create_Default(SessionRoles.Implementer), SESSION_ID, resumeTranscript: false, null);

        Assert.Equal(["-p", "--output-format", "stream-json", "--verbose", "--name", "orch-1-imp-1", "--session-id", SESSION_ID, "--model", "opus", "--dangerously-skip-permissions", "/implementer orch-1/imp-1"], arguments);
    }

    [Fact]
    public void ResumedTurn_UsesResume_AndNoPositionalPrompt()
    {
        var arguments = PrintTurnCommand_Builder.Build_Arguments(State(SessionRoles.Implementer), RoleRunnerConfig_Factory.Create_Default(SessionRoles.Implementer), SESSION_ID, resumeTranscript: true, null);

        Assert.Equal(["-p", "--output-format", "stream-json", "--verbose", "--name", "orch-1-imp-1", "--resume", SESSION_ID, "--model", "opus", "--dangerously-skip-permissions"], arguments);
        Assert.DoesNotContain("--session-id", arguments);
    }

    [Fact]
    public void Reviewer_IsDeniedTheEditingTools_WithTheTerminatorBeforeThePrompt()
    {
        var arguments = PrintTurnCommand_Builder.Build_Arguments(State(SessionRoles.Reviewer), RoleRunnerConfig_Factory.Create_Default(SessionRoles.Reviewer), SESSION_ID, resumeTranscript: false, null);

        var tail = arguments.Skip(arguments.Count - 6).ToList();
        Assert.Equal(["--disallowedTools", "Write", "Edit", "NotebookEdit", "--", "/reviewer orch-1/imp-1"], tail);
    }

    [Fact]
    public void PermissionMode_ReplacesTheSkipFlag_AndSettingsArePassedEveryTurn()
    {
        var roleConfig = RoleRunnerConfig_Factory.Create(SessionRunners.Print, ResumeModes.Transcript, "acceptEdits");

        var arguments = PrintTurnCommand_Builder.Build_Arguments(State(SessionRoles.Implementer, model: null), roleConfig, SESSION_ID, resumeTranscript: true, "/tmp/settings.json").ToList();

        Assert.DoesNotContain("--dangerously-skip-permissions", arguments);
        Assert.DoesNotContain("--model", arguments);
        Assert.Contains("--permission-mode", arguments);
        Assert.Equal("acceptEdits", arguments[arguments.IndexOf("--permission-mode") + 1]);
        Assert.Equal("/tmp/settings.json", arguments[arguments.IndexOf("--settings") + 1]);
    }

    [Fact]
    public void General_RunsUnderItsOwnName()
    {
        Assert.Equal("general", PrintTurnCommand_Builder.Build_SessionName(State(SessionRoles.General)));
    }
}
