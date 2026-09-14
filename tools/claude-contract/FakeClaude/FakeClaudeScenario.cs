using System.Text.Json.Nodes;

namespace FakeClaude;

/// <summary>
/// One simulated turn: what the fake answers, how long it takes, how it exits, which hooks it
/// pretends to fire. Every field has a default so a scenario file names only what it wants to
/// inject — a delay, an exit code, an <c>api_error_status</c>, a missing field.
/// </summary>
public sealed class FakeClaudeTurn
{
    public string Result { get; init; } = "DONE";
    public int DelayMilliseconds { get; init; }
    public int? ExitCode { get; init; }
    public bool IsError { get; init; }
    public int? ApiErrorStatus { get; init; }
    public double TotalCostUsd { get; init; } = 0.0127;
    public int DurationMilliseconds { get; init; } = 3547;
    public string Stderr { get; init; } = string.Empty;
    public string StdoutPrefix { get; init; } = string.Empty;
    public IReadOnlyList<string> OmitFields { get; init; } = [];
    public IReadOnlyList<string> Hooks { get; init; } = FakeClaudeScenario.ALL_HOOKS;
    public JsonObject? ExtraJson { get; init; }

    /// <summary>
    /// The <c>rate_limit_info</c> a stream turn emits before its result, or null for no event.
    /// Null is the COMMON case on purpose: measured over six turns and again over three, the real
    /// CLI emitted one <c>rate_limit_event</c> per run, at the change — so a bridge that expects one
    /// per turn is wrong, and the fake is what proves it.
    /// </summary>
    public JsonObject? RateLimit { get; init; }

    /// <summary>
    /// THE ASSISTANT MESSAGES OF THIS TURN, in order — one text-only <c>assistant</c> event each,
    /// before the <c>result</c>. Empty (the common case) means the measured shape: ONE assistant
    /// event carrying the same text as the result.
    ///
    /// <para>
    /// It exists because the fake could not script the defect it is now tested against: a session
    /// writes its report, a background sub-agent returns, the CLI re-opens the turn and a LATER
    /// message becomes the result — so the report is lost. With this, a turn emits N finals and the
    /// bridge's rule can be observed offline instead of only in a channel.
    /// </para>
    /// <para>
    /// The <c>result</c> event still carries <see cref="Result"/>, which is what the real CLI does:
    /// the result text is the LAST message, so a scenario that means "superseded" gives a different
    /// <c>result</c> from its last assistant message, and one that means "ordinary" repeats it.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> AssistantMessages { get; init; } = [];

    /// <summary>
    /// Emits one tool_use assistant event between consecutive <see cref="AssistantMessages"/> — the
    /// "further events followed it" half of the rule, and what a returning sub-agent actually looks
    /// like on the wire.
    /// </summary>
    public bool ToolUseBetween { get; init; }

    /// <summary>
    /// A WHOLE TURN THE SESSION RUNS ON ITS OWN, right after this one's result — a background task
    /// it started finishing and waking it. Its text, or null for none. The bridge's next message
    /// finds it already waiting in the pipe (fincanva-5, 2026-09-11).
    /// </summary>
    public string? UnpromptedResultAfter { get; init; }

    /// <summary>
    /// The same, but emitted when this turn's MESSAGE arrives and before the turn itself: the
    /// unprompted turn finished while the bridge's message sat queued, so its result reaches the
    /// bridge after the message was written and before the message's echo.
    /// </summary>
    public string? UnpromptedResultBefore { get; init; }

    /// <summary>What an unprompted turn adds to the process's running cost.</summary>
    public double UnpromptedCostUsd { get; init; } = 0.0042;

    /// <summary>Suppresses the <c>--replay-user-messages</c> echo: a CLI that does not honour the flag.</summary>
    public bool NoReplay { get; init; }

    public int Resolve_ExitCode()
    {
        if (ExitCode != null)
            return ExitCode.Value;

        return IsError ? 1 : 0;
    }

    public static FakeClaudeTurn Parse(JsonObject node, FakeClaudeTurn defaults)
    {
        return new FakeClaudeTurn
        {
            Result = node["result"]?.GetValue<string>() ?? defaults.Result,
            DelayMilliseconds = node["delay_ms"]?.GetValue<int>() ?? defaults.DelayMilliseconds,
            ExitCode = node["exit_code"]?.GetValue<int>() ?? defaults.ExitCode,
            IsError = node["is_error"]?.GetValue<bool>() ?? defaults.IsError,
            ApiErrorStatus = node["api_error_status"]?.GetValue<int>() ?? defaults.ApiErrorStatus,
            TotalCostUsd = node["total_cost_usd"]?.GetValue<double>() ?? defaults.TotalCostUsd,
            DurationMilliseconds = node["duration_ms"]?.GetValue<int>() ?? defaults.DurationMilliseconds,
            Stderr = node["stderr"]?.GetValue<string>() ?? defaults.Stderr,
            StdoutPrefix = node["stdout_prefix"]?.GetValue<string>() ?? defaults.StdoutPrefix,
            OmitFields = Read_Strings(node["omit_fields"]) ?? defaults.OmitFields,
            Hooks = Read_Strings(node["hooks"]) ?? defaults.Hooks,
            ExtraJson = node["extra_json"] as JsonObject ?? defaults.ExtraJson,
            RateLimit = node["rate_limit"] as JsonObject ?? defaults.RateLimit,
            AssistantMessages = Read_Strings(node["assistant_messages"]) ?? defaults.AssistantMessages,
            ToolUseBetween = node["tool_use_between"]?.GetValue<bool>() ?? defaults.ToolUseBetween,
            UnpromptedResultAfter = node["unprompted_result_after"]?.GetValue<string>() ?? defaults.UnpromptedResultAfter,
            UnpromptedResultBefore = node["unprompted_result_before"]?.GetValue<string>() ?? defaults.UnpromptedResultBefore,
            UnpromptedCostUsd = node["unprompted_cost_usd"]?.GetValue<double>() ?? defaults.UnpromptedCostUsd,
            NoReplay = node["no_replay"]?.GetValue<bool>() ?? defaults.NoReplay,
        };
    }

    static IReadOnlyList<string>? Read_Strings(JsonNode? node)
    {
        if (node is not JsonArray array)
            return null;

        List<string> values = [];

        foreach (var element in array)
        {
            var value = element?.GetValue<string>();

            if (value != null)
                values.Add(value);
        }

        return values;
    }
}

/// <summary>
/// The scenario file: a default turn, an optional ordered list of turns (the k-th invocation plays
/// the k-th one, then the default), and optional per-<c>--name</c> lists so two members driven by
/// one bridge can behave differently in the same working directory. Resolved from
/// <c>FAKE_CLAUDE_SCENARIO</c>, else <c>fake-claude-scenario.json</c> in the working directory,
/// else built-in defaults — a missing file is a valid scenario, not an error.
/// </summary>
public sealed class FakeClaudeScenario
{
    public const string SCENARIO_ENV = "FAKE_CLAUDE_SCENARIO";
    public const string SCENARIO_FILE = "fake-claude-scenario.json";
    public const string LOG_ENV = "FAKE_CLAUDE_LOG";
    public const string LOG_FILE = "fake-claude-invocations.jsonl";
    public const string CLI_VERSION = "2.1.261";

    public static readonly IReadOnlyList<string> ALL_HOOKS =
        ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "Stop", "SessionEnd"];

    public FakeClaudeTurn Default { get; private init; } = new();
    public IReadOnlyList<FakeClaudeTurn> Turns { get; private init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<FakeClaudeTurn>> TurnsByName { get; private init; } =
        new Dictionary<string, IReadOnlyList<FakeClaudeTurn>>();

    /// <summary>Relative to the working directory; null = hooks are not simulated on disk.</summary>
    public string? HooksLogFile { get; private init; }

    public static FakeClaudeScenario Load(string workingDirectory)
    {
        var envPath = Environment.GetEnvironmentVariable(SCENARIO_ENV);
        var path = !string.IsNullOrWhiteSpace(envPath) ? envPath : Path.Combine(workingDirectory, SCENARIO_FILE);

        if (!File.Exists(path))
            return new FakeClaudeScenario();

        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
            throw new Exception($"FakeClaude scenario at '{path}' is not a JSON object");

        var defaults = root["default"] is JsonObject defaultNode
            ? FakeClaudeTurn.Parse(defaultNode, new FakeClaudeTurn())
            : new FakeClaudeTurn();

        Dictionary<string, IReadOnlyList<FakeClaudeTurn>> byName = [];

        if (root["sessions"] is JsonObject sessions)
        {
            foreach (var pair in sessions)
            {
                if (pair.Value is JsonObject session)
                    byName[pair.Key] = Parse_Turns(session["turns"], defaults);
            }
        }

        return new FakeClaudeScenario
        {
            Default = defaults,
            Turns = Parse_Turns(root["turns"], defaults),
            TurnsByName = byName,
            HooksLogFile = root["hooks_log"]?.GetValue<string>(),
        };
    }

    /// <summary>
    /// The turn the k-th invocation plays. A per-name list wins when the invocation carries that
    /// name; k counts invocations WITH THAT NAME then, so members interleave freely.
    /// </summary>
    public FakeClaudeTurn Resolve_Turn(string? name, int invocationNumber)
    {
        if (name != null && TurnsByName.TryGetValue(name, out var named))
            return invocationNumber <= named.Count ? named[invocationNumber - 1] : Default;

        return invocationNumber <= Turns.Count ? Turns[invocationNumber - 1] : Default;
    }

    static IReadOnlyList<FakeClaudeTurn> Parse_Turns(JsonNode? node, FakeClaudeTurn defaults)
    {
        List<FakeClaudeTurn> turns = [];

        if (node is not JsonArray array)
            return turns;

        foreach (var element in array)
        {
            if (element is JsonObject turn)
                turns.Add(FakeClaudeTurn.Parse(turn, defaults));
        }

        return turns;
    }
}
