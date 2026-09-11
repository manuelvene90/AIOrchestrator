namespace FakeClaude;

/// <summary>
/// The subset of the `claude` command line the bridge uses, parsed the way the real CLI parses it:
/// <c>--disallowedTools</c> is variadic and swallows everything up to the next flag or a <c>--</c>
/// terminator (the real CLI once ate a reviewer's prompt that way — see SpawnCommand_Builder), a
/// positional prompt is the first non-flag token, and no positional means the prompt is on stdin.
/// Everything is nullable: an absent flag is a fact the tests assert on, not an error.
/// </summary>
public sealed class FakeClaudeArguments
{
    public bool Print { get; private set; }
    public bool Version { get; private set; }
    public string? Resume { get; private set; }
    public string? SessionId { get; private set; }
    public string? Name { get; private set; }
    public string? OutputFormat { get; private set; }
    public string? InputFormat { get; private set; }
    public bool Verbose { get; private set; }
    public bool IncludeHookEvents { get; private set; }
    public bool ReplayUserMessages { get; private set; }
    public string? Settings { get; private set; }
    public string? Model { get; private set; }
    public string? PermissionMode { get; private set; }

    /// <summary>
    /// <c>--max-budget-usd &lt;amount&gt;</c> — present in the real CLI's help on 2.1.266 ("Maximum
    /// dollar amount to spend on API calls (only works with --print)"). Parsed so the flag's VALUE is
    /// not mistaken for the positional prompt: unparsed, the fake refused the flag as unknown and then
    /// read "2.00" as the prompt, which is the closing turn's whole command line misread twice over.
    /// What the fake does NOT simulate is the budget being reached — that behaviour is unmeasured, and
    /// this fake never pretends to know an unmeasured shape.
    /// </summary>
    public string? MaxBudgetUsd { get; private set; }
    public bool DangerouslySkipPermissions { get; private set; }
    public IReadOnlyList<string> DisallowedTools => _disallowedTools;
    public string? PositionalPrompt { get; private set; }
    public IReadOnlyList<string> Unknown => _unknown;

    readonly List<string> _disallowedTools = [];
    readonly List<string> _unknown = [];

    public static FakeClaudeArguments Parse(IReadOnlyList<string> args)
    {
        var parsed = new FakeClaudeArguments();
        var afterTerminator = false;

        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];

            if (afterTerminator)
            {
                parsed.PositionalPrompt ??= token;
                continue;
            }

            switch (token)
            {
                case "--":
                    afterTerminator = true;
                    break;
                case "-p":
                case "--print":
                    parsed.Print = true;
                    break;
                case "--version":
                case "-v":
                    parsed.Version = true;
                    break;
                case "--dangerously-skip-permissions":
                    parsed.DangerouslySkipPermissions = true;
                    break;
                case "-r":
                case "--resume":
                    parsed.Resume = Take_Value(args, ref i);
                    break;
                case "--session-id":
                    parsed.SessionId = Take_Value(args, ref i);
                    break;
                case "-n":
                case "--name":
                    parsed.Name = Take_Value(args, ref i);
                    break;
                case "--output-format":
                    parsed.OutputFormat = Take_Value(args, ref i);
                    break;
                case "--input-format":
                    parsed.InputFormat = Take_Value(args, ref i);
                    break;
                case "--verbose":
                    parsed.Verbose = true;
                    break;
                case "--include-hook-events":
                    parsed.IncludeHookEvents = true;
                    break;
                case "--replay-user-messages":
                    parsed.ReplayUserMessages = true;
                    break;
                case "--settings":
                    parsed.Settings = Take_Value(args, ref i);
                    break;
                case "--model":
                    parsed.Model = Take_Value(args, ref i);
                    break;
                case "--permission-mode":
                    parsed.PermissionMode = Take_Value(args, ref i);
                    break;
                case "--max-budget-usd":
                    parsed.MaxBudgetUsd = Take_Value(args, ref i);
                    break;
                case "--disallowedTools":
                    // Variadic: everything up to the next flag or the terminator is a tool name.
                    while (i + 1 < args.Count && !args[i + 1].StartsWith('-'))
                        parsed._disallowedTools.Add(args[++i]);
                    break;
                default:
                    if (token.StartsWith('-'))
                        parsed._unknown.Add(token);
                    else
                        parsed.PositionalPrompt ??= token;
                    break;
            }
        }

        return parsed;
    }

    static string? Take_Value(IReadOnlyList<string> args, ref int index)
    {
        if (index + 1 >= args.Count)
            return null;

        return args[++index];
    }
}
