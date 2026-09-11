using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Spawning;

namespace AIOrchestratorCoreLib.Running.StreamTurn;

/// <summary>
/// The argument list of the ONE process a stream session lives in:
/// <c>-p --input-format stream-json --output-format stream-json --verbose --include-hook-events
/// --name &lt;name&gt; (--session-id | --resume) &lt;id&gt; [--model m] [--settings f]
/// (--permission-mode m | --dangerously-skip-permissions) [--disallowedTools … --]</c>.
///
/// <para>
/// THERE IS NO POSITIONAL PROMPT, and that is the one real difference from
/// <see cref="PrintTurnCommand_Builder"/>. A print session boots by taking its role command as the
/// prompt of its first invocation; a stream session has no first invocation to hang it on, so the
/// role command is sent as its first user MESSAGE instead. That a slash command works there is
/// measured, not assumed (Live: <c>/probe-hello stream-arg-1</c> ran the command and wrote its
/// file) — because the failure mode if it did not is a session that never learns its role and says
/// so to nobody.
/// </para>
/// <para>
/// <c>--verbose</c> is not decoration: the CLI refuses <c>--output-format stream-json</c> without
/// it. The reviewer's tool denial and its <c>--</c> terminator carry over unchanged from the print
/// builder, whose lesson (a variadic flag ate a prompt) applies to any argument list.
/// </para>
/// </summary>
public static class StreamTurnCommand_Builder
{
    public static IReadOnlyList<string> Build_Arguments(IPrintSessionState state, IRoleRunnerConfig roleConfig, string sessionId, bool resumeTranscript, string? settingsFile)
    {
        List<string> arguments =
        [
            "-p",
            "--input-format", StreamJson_Words.INPUT_FORMAT,
            "--output-format", StreamJson_Words.OUTPUT_FORMAT,
            "--verbose",
            "--include-hook-events",
            "--name", PrintTurnCommand_Builder.Build_SessionName(state),
        ];

        arguments.Add(resumeTranscript ? "--resume" : "--session-id");
        arguments.Add(sessionId);

        if (!string.IsNullOrWhiteSpace(state.Model))
        {
            arguments.Add("--model");
            arguments.Add(state.Model);
        }

        if (!string.IsNullOrWhiteSpace(settingsFile))
        {
            arguments.Add("--settings");
            arguments.Add(settingsFile);
        }

        if (roleConfig.PermissionMode != null)
        {
            arguments.Add("--permission-mode");
            arguments.Add(roleConfig.PermissionMode);
        }
        else
        {
            arguments.Add(SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS);
        }

        if (state.Role == SessionRoles.Reviewer)
        {
            arguments.Add("--disallowedTools");
            arguments.AddRange(PrintTurnCommand_Builder.REVIEWER_DISALLOWED_TOOLS);
            arguments.Add("--");
        }

        return arguments;
    }
}

/// <summary>The two format words of the transport, spelled once for the builder and the tests.</summary>
public static class StreamJson_Words
{
    public const string INPUT_FORMAT = "stream-json";
    public const string OUTPUT_FORMAT = "stream-json";
}
