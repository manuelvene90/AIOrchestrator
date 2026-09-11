using System.Text.Json.Nodes;

namespace FakeClaude;

/// <summary>
/// Records every invocation as one JSON line — the flags, the prompt, the working directory, the
/// AIORCH_* environment — so a test can assert what the bridge actually sent, not what it meant
/// to. The line count is also the invocation counter the scenario's turn list is indexed by.
/// Resolved from <c>FAKE_CLAUDE_LOG</c>, else <c>fake-claude-invocations.jsonl</c> in the working
/// directory.
/// </summary>
public static class Invocation_Logger
{
    /// <summary>
    /// WHAT a log line records, under the key <c>line_kind</c>. Separate from the long-standing
    /// <c>prompt_source</c> (which says argument-or-stdin and is asserted on by the existing suite):
    /// one process can now write several lines, and the counter has to tell them apart.
    /// </summary>
    public const string KIND_INVOCATION = "invocation";

    /// <summary>A persistent stream process starting: it carries the flags and claims the session id, but no prompt yet.</summary>
    public const string KIND_STREAM_START = "stream-start";

    /// <summary>One user message written on a live stream process's stdin. These are what a stream turn counter counts.</summary>
    public const string KIND_STREAM_MESSAGE = "stream-message";

    public const string LINE_KIND_KEY = "line_kind";

    static readonly string[] LOGGED_ENVIRONMENT_PREFIXES = ["AIORCH_", "CLAUDECODE", "CLAUDE_CODE_"];

    /// <summary>
    /// Five seconds of waiting out a busy log — one policy for the reader and the writer, because
    /// they contend for the same handle and an asymmetry between them is what the crash below was.
    /// </summary>
    const int SHARING_RETRIES = 200;

    /// <summary>Paired with <see cref="SHARING_RETRIES"/>; the two are only ever read together.</summary>
    const int SHARING_RETRY_MILLISECONDS = 25;

    /// <summary>
    /// Runs <paramref name="operation"/>, waiting out the sharing violation another fake's
    /// exclusive handle on the log causes. ONE policy for both callers: the appender takes the log
    /// <see cref="FileShare.None"/> while it counts, so every other access to that file has to be
    /// prepared to wait for it. When the wait is exhausted the exception escapes — a fake that
    /// cannot read its own memory must say so rather than answer from an empty log.
    /// </summary>
    static T Retry_WhileShared<T>(Func<T> operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (IOException) when (attempt < SHARING_RETRIES)
            {
                Thread.Sleep(SHARING_RETRY_MILLISECONDS);
            }
        }
    }

    /// <summary>
    /// Whether a process has already run under this <c>--session-id</c> — read from the log, which
    /// is this fake's whole memory. The real CLI refuses a re-claimed id (measured); so does this.
    ///
    /// <para>
    /// RETRIED LIKE THE APPEND, and for the same reason (measured 2026-09-09, macOS). The appender
    /// has held this file under <see cref="FileShare.None"/> since two turns first shared a working
    /// directory; this read never did, so a plain <c>File.ReadAllLines</c> landing inside another
    /// fake's append died with "used by another process" — UNHANDLED, exit 134, before a single
    /// line was logged. Seven of eighteen concurrent invocations in one folder crashed that way. The
    /// dispatcher read the corpse as an ordinary turn error and retried; the retry resumed a session
    /// id the crash had never claimed, was refused, and the third attempt started fresh and drew the
    /// scenario's DEFAULT turn — so a test that asked for a refusal silently got a success, which is
    /// the worst possible shape for a test double to fail in.
    /// </para>
    /// </summary>
    public static bool Has_ClaimedSessionId(string logPath, string sessionId)
    {
        if (!File.Exists(logPath))
            return false;

        foreach (var line in Retry_WhileShared(() => File.ReadAllLines(logPath)))
        {
            if (line.Length == 0)
                continue;

            try
            {
                if ((JsonNode.Parse(line) as JsonObject)?["session_id"]?.GetValue<string>() == sessionId)
                    return true;
            }
            catch
            {
                // A half-written line is not a claim.
            }
        }

        return false;
    }

    /// <summary>Lines written before this field existed are invocations — the only kind there was.</summary>
    static string Read_LineKind(JsonObject line)
    {
        return line[LINE_KIND_KEY]?.GetValue<string>() ?? KIND_INVOCATION;
    }

    public static string Resolve_LogPath(string workingDirectory)
    {
        var envPath = Environment.GetEnvironmentVariable(FakeClaudeScenario.LOG_ENV);

        return !string.IsNullOrWhiteSpace(envPath) ? envPath : Path.Combine(workingDirectory, FakeClaudeScenario.LOG_FILE);
    }

    /// <summary>
    /// Appends this invocation and returns its number, overall and among lines with the same name
    /// AND the same <paramref name="lineKind"/>. The second qualifier is what lets a stream
    /// process's MESSAGES be counted independently of the process starts that carry them — without
    /// it, a restart under the fallback ladder would shift every later turn's scenario entry by one.
    /// </summary>
    public static (int Overall, int ForName) Append(string logPath, IReadOnlyList<string> rawArgs, FakeClaudeArguments args, string prompt, string workingDirectory, string lineKind = KIND_INVOCATION, string? stdin = null)
    {
        // SHARED, AND CONCURRENTLY. The dispatcher allows ten turns at once and they share one
        // working directory, so two fakes write this file at the same moment — and
        // File.AppendAllText opens it FileShare.Read, which makes the second one die with an
        // IOException the suite would read as a turn error. Read and append under one exclusive
        // handle, retried, so the counters below are computed from what is really there.
        return Append_Serialised(logPath, rawArgs, args, prompt, workingDirectory, lineKind, stdin);
    }

    static (int Overall, int ForName) Append_Serialised(string logPath, IReadOnlyList<string> rawArgs, FakeClaudeArguments args, string prompt, string workingDirectory, string lineKind, string? stdin)
    {
        var folder = Path.GetDirectoryName(logPath);

        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        return Retry_WhileShared(() =>
        {
            using var stream = new FileStream(logPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return Append_ToOpenLog(stream, rawArgs, args, prompt, workingDirectory, lineKind, stdin);
        });
    }

    static (int Overall, int ForName) Append_ToOpenLog(FileStream stream, IReadOnlyList<string> rawArgs, FakeClaudeArguments args, string prompt, string workingDirectory, string lineKind, string? stdin)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var existing = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var overall = existing.Length + 1;
        var forName = 1;

        foreach (var line in existing)
        {
            if (line.Length == 0)
                continue;

            if (JsonNode.Parse(line) is not JsonObject previous)
                continue;

            if (previous["name"]?.GetValue<string>() == args.Name && Read_LineKind(previous) == lineKind)
                forName++;
        }

        var environment = new JsonObject();

        foreach (System.Collections.DictionaryEntry pair in Environment.GetEnvironmentVariables())
        {
            var key = pair.Key.ToString() ?? string.Empty;

            if (LOGGED_ENVIRONMENT_PREFIXES.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                environment[key] = pair.Value?.ToString();
        }

        var arguments = new JsonArray();

        foreach (var raw in rawArgs)
            arguments.Add(raw);

        var record = new JsonObject
        {
            ["n"] = overall,
            ["n_for_name"] = forName,
            ["at"] = DateTime.UtcNow.ToString("o"),
            ["pid"] = Environment.ProcessId,
            ["name"] = args.Name,
            ["args"] = arguments,
            ["prompt"] = prompt,
            ["stdin"] = stdin,
            ["prompt_source"] = args.PositionalPrompt != null ? "argument" : "stdin",
            [LINE_KIND_KEY] = lineKind,
            ["cwd"] = workingDirectory,
            ["resume"] = args.Resume,
            // A stream MESSAGE claims no session id: the claim belongs to the process start, and
            // recording it twice would make Has_ClaimedSessionId refuse a legitimate first turn.
            ["session_id"] = lineKind == KIND_STREAM_MESSAGE ? null : args.SessionId,
            ["settings"] = args.Settings,
            ["model"] = args.Model,
            ["permission_mode"] = args.PermissionMode,
            ["max_budget_usd"] = args.MaxBudgetUsd,
            ["env"] = environment,
        };

        stream.Seek(0, SeekOrigin.End);

        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write(record.ToJsonString() + "\n");
        writer.Flush();

        return (overall, forName);
    }
}
