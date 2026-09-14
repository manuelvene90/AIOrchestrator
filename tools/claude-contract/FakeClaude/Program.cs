using System.Text;
using FakeClaude;

// The fake `claude`. Only PRINT mode is simulated — the interactive TUI, --bg and the daemon are
// not, because nothing the bridge automates goes through them (the Live contract tests cover those
// against the real binary). Exit codes and error texts mirror the real CLI where they were
// measured; where they were not, the fake is STRICTER (it refuses combinations whose real
// behaviour is unknown), so a bridge that passes the fake never relies on an unmeasured shape.

// UTF-8 ON ALL THREE STREAMS, EXPLICITLY, BEFORE ANYTHING IS READ OR WRITTEN.
//
// The bridge hands this process UTF-8 (PrintTurnRunnerModel and StreamSessionProcess both set
// StandardInputEncoding), and the real `claude` reads UTF-8. Console.In does not: on Windows it
// decodes with the console's code page, so "REPORT — two" arrived here as "REPORT ÔÇö two" and the
// fake answered a prompt nobody sent. Measured 2026-09-11, the first Windows run of this suite —
// it is the same fact as the statusline's output encoding, on the input side.
//
// The stream path hid it: System.Text.Json escapes non-ASCII to \uXXXX, so those prompts are pure
// ASCII on the wire and decode identically under any code page. Only the print path, which sends
// the prompt as plain text, showed the damage — which is why exactly one test carried it.
//
// THE INSTRUMENT, NOT THE SUBJECT: nothing in the app was wrong here. A fake CLI that mangles what
// it is given reports a failure the product does not have, and would just as happily report a pass.
// Streams are replaced rather than Console.InputEncoding set, because the setter needs a console and
// this process is always spawned with all three handles redirected.
Console.SetIn(new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)));
Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });

var rawArgs = args.ToList();
var parsed = FakeClaudeArguments.Parse(rawArgs);

if (parsed.Version)
{
    Console.Out.WriteLine($"{FakeClaudeScenario.CLI_VERSION} (Claude Code) [FakeClaude]");
    return 0;
}

if (!parsed.Print)
{
    Console.Error.WriteLine("FakeClaude: only --print (-p) mode is simulated; an interactive session cannot be faked");
    return 2;
}

if (parsed.OutputFormat != null && parsed.OutputFormat != "json" && parsed.OutputFormat != "text" && parsed.OutputFormat != StreamJson_Responder.OUTPUT_FORMAT)
{
    Console.Error.WriteLine($"Error: --output-format must be one of text, json, stream-json (got '{parsed.OutputFormat}')");
    return 1;
}

if (parsed.InputFormat != null && parsed.InputFormat != "text" && parsed.InputFormat != StreamJson_Responder.INPUT_FORMAT)
{
    Console.Error.WriteLine($"Error: --input-format must be one of text, stream-json (got '{parsed.InputFormat}')");
    return 1;
}

if (parsed.SessionId != null && !Guid.TryParse(parsed.SessionId, out _))
{
    Console.Error.WriteLine("Error: --session-id must be a valid UUID");
    return 1;
}

if (parsed.SessionId != null && parsed.Resume != null)
{
    // Unmeasured on the real CLI. Refused here so the bridge never sends both.
    Console.Error.WriteLine("FakeClaude: --session-id and --resume together is not a measured combination — refusing");
    return 1;
}

if (parsed.Unknown.Count > 0)
{
    Console.Error.WriteLine($"error: unknown option '{parsed.Unknown[0]}'");
    return 1;
}

// THE PERSISTENT SHAPE — one process, many turns, messages on stdin. Everything below this line
// is the transient one: it reads ONE prompt, answers it and exits.
if (parsed.InputFormat == StreamJson_Responder.INPUT_FORMAT)
    return StreamJson_Responder.Run(parsed, rawArgs, Directory.GetCurrentDirectory());

// THE REAL CLI'S RULE FOR "BOTH" IS NOT MEASURED (live test pending, weekly limit 2026-09-08). The
// fake keeps its old rule — the positional prompt wins — and RECORDS what arrived on stdin beside
// it, so a test can pin what the bridge sent without the fake pretending to know what the model saw.
var stdinText = Read_StdinPrompt();
var prompt = parsed.PositionalPrompt ?? stdinText;

if (string.IsNullOrWhiteSpace(prompt))
{
    Console.Error.WriteLine("Error: Input must be provided either through stdin or as a prompt argument when using --print");
    return 1;
}

var workingDirectory = Directory.GetCurrentDirectory();
var scenario = FakeClaudeScenario.Load(workingDirectory);
var logPath = Invocation_Logger.Resolve_LogPath(workingDirectory);

// MEASURED AGAINST THE REAL CLI on 2026-09-06: a second --session-id with a uuid it has already
// seen is refused — "Error: Session ID <uuid> is already in use.", exit 1 — while --resume of that
// same session works, even when the first attempt was killed mid-turn. The fake refused nothing
// here, so a bridge that re-claimed an id on a retry passed the suite and would have stalled live.
// The fake matches the measurement, never what the bridge wishes were true.
if (parsed.SessionId != null && Invocation_Logger.Has_ClaimedSessionId(logPath, parsed.SessionId))
{
    Console.Error.WriteLine($"Error: Session ID {parsed.SessionId} is already in use.");
    return 1;
}

// MEASURED AGAINST THE REAL CLI on 2026-09-06 (2.1.263): resuming a session id that was never
// created is refused — "No conversation found with session ID: <uuid>", exit 1. The fake accepted any
// --resume, and that permissiveness kept a real defect green: the bridge claims an id BEFORE starting
// the process, so a first turn that died before the CLI made the transcript had every retry resume
// something that never existed, and the session wedged for good. The fake matches the measurement,
// never what the bridge wishes were true.
//
// DECIDED HERE, REFUSED AFTER THE LOG. A refused invocation is still an invocation the bridge made,
// and the log is the record of what it ran: refusing before appending made the failed attempt
// invisible, so a test could not even see the retry it was written to check. The claim check above
// keeps its place because it reads the log for its own answer and would otherwise find itself.
var unknownResume = parsed.Resume != null && !Invocation_Logger.Has_ClaimedSessionId(logPath, parsed.Resume);

var (overall, forName) = Invocation_Logger.Append(logPath, rawArgs, parsed, prompt, workingDirectory, stdin: parsed.PositionalPrompt == null ? null : stdinText);

if (unknownResume)
{
    Console.Error.WriteLine($"No conversation found with session ID: {parsed.Resume}");
    return 1;
}
var turn = scenario.Resolve_Turn(parsed.Name, parsed.Name != null && scenario.TurnsByName.ContainsKey(parsed.Name) ? forName : overall);

var sessionId = parsed.SessionId ?? parsed.Resume ?? Guid.NewGuid().ToString();
var model = Model_Ids.Resolve(parsed.Model);

Simulate_Hooks(scenario, turn, workingDirectory, parsed.Resume != null);

if (turn.DelayMilliseconds > 0)
    Thread.Sleep(turn.DelayMilliseconds);

if (turn.Stderr.Length > 0)
    Console.Error.WriteLine(turn.Stderr);

if (turn.StdoutPrefix.Length > 0)
    Console.Out.Write(turn.StdoutPrefix);

// THE NDJSON SHAPE OF A ONE-SHOT PRINT TURN — what the bridge asks for since stage 19, so a print
// turn and a stream turn of the same scenario put the SAME events on the wire and the bridge's
// superseded-final rule can be observed on both. The event order is the measured one: `init`, the
// turn's assistant messages, then the `result`.
if (parsed.OutputFormat == StreamJson_Responder.OUTPUT_FORMAT)
{
    if (!parsed.Verbose)
    {
        // The real CLI refuses it; refused here so a bridge that stops sending --verbose is caught
        // offline rather than in a channel.
        Console.Error.WriteLine("Error: --output-format=stream-json requires --verbose");
        return 1;
    }

    Write_Line(StreamEventJson_Builder.Build_Init(sessionId, model, workingDirectory, parsed.PermissionMode));

    foreach (var assistantEvent in StreamEventJson_Builder.Build_AssistantSequence(turn, sessionId, model))
        Write_Line(assistantEvent);

    Write_Line(ResultJson_Builder.Build(turn, sessionId, model, numTurns: 2));
}
else if (parsed.OutputFormat == "json")
{
    Console.Out.WriteLine(ResultJson_Builder.Build(turn, sessionId, model, numTurns: 2));
}
else
{
    Console.Out.WriteLine(turn.Result);
}

return turn.Resolve_ExitCode();

static void Write_Line(string json)
{
    Console.Out.Write(json);
    Console.Out.Write('\n');
    Console.Out.Flush();
}

static string Read_StdinPrompt()
{
    if (Console.IsInputRedirected)
        return Console.In.ReadToEnd().Trim();

    // The real CLI waits 3 s for stdin and says so; the fake says so and does not wait.
    Console.Error.WriteLine("Warning: no stdin data received in 3s, proceeding without it (FakeClaude: stdin is a terminal)");
    return string.Empty;
}

/// <summary>Appends one line per hook event, in the format the probe's hook.sh writes (MEASUREMENTS.md §M1).</summary>
static void Simulate_Hooks(FakeClaudeScenario scenario, FakeClaudeTurn turn, string workingDirectory, bool isResume)
{
    if (scenario.HooksLogFile == null)
        return;

    var path = Path.IsPathRooted(scenario.HooksLogFile) ? scenario.HooksLogFile : Path.Combine(workingDirectory, scenario.HooksLogFile);
    var stamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    var lines = turn.Hooks.Select(hook =>
        $"=== {hook} {stamp} pid={Environment.ProcessId} ppid=0 tty=not a tty{(hook == "SessionStart" ? $" source={(isResume ? "resume" : "startup")}" : string.Empty)}\n");

    File.AppendAllText(path, string.Concat(lines));
}
