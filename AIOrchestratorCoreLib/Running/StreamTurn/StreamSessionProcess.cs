using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.TurnResult;

namespace AIOrchestratorCoreLib.Running.StreamTurn;

/// <summary>
/// ONE LIVING <c>claude</c>, AND THE PIPE INTO IT. The whole difference between this stage and the
/// print runner is here: the process is started once and kept, every message is a line on its
/// stdin, and a turn ends when a <c>result</c> event comes back. Measured p50 1.27 s against 5.77 s,
/// because the ~4.25 s the print runner pays is process start-up and nothing else.
///
/// <para>
/// STDOUT IS DRAINED BY ITS OWN TASK, ALWAYS. A reader that only reads while awaiting a result lets
/// the pipe fill between turns, and a full pipe blocks the CLI mid-sentence — the deadlock this
/// transport is most likely to die of, and the one the study named as its specific failure mode
/// ("backpressure if the bridge does not read stdout"). The drain also stamps
/// <see cref="LastByteUtc"/>, which is the heartbeat: not "is the process alive" (a hung process is
/// alive) but "has it said anything".
/// </para>
/// <para>
/// EVERY LINE IS HANDED ON RAW, before parsing, so the turn log holds what actually arrived rather
/// than what this class understood — that is what /tail and /log read, and what a maintainer needs
/// the day the format moves under us.
/// </para>
/// </summary>
internal sealed class StreamSessionProcess : IDisposable
{
    /// <summary>How often the await loop re-checks its two deadlines while nothing is arriving.</summary>
    static readonly TimeSpan POLL_SLICE = TimeSpan.FromMilliseconds(50);

    /// <summary>After stdin is closed, how long a well-behaved process is given to exit before it is killed.</summary>
    static readonly TimeSpan CLOSE_GRACE = TimeSpan.FromSeconds(5);

    readonly Process _process;
    readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    readonly StringBuilder _stderr = new();
    readonly Lock _lock = new();
    readonly Action<string> _onRawLine;

    long _lastByteTicksUtc = DateTime.UtcNow.Ticks;
    double _costBaselineUsd;

    /// <summary>Set by the first echo of one of our messages, never cleared — see <c>Read_Line</c> in <see cref="Send_AndAwaitResult_Async"/>.</summary>
    bool _echoesOurMessages;

    public string SessionId { get; }
    public DateTime StartedUtc { get; } = DateTime.UtcNow;

    /// <summary>When the process last produced a byte — the heartbeat a mute session is caught by.</summary>
    public DateTime LastByteUtc => new(Interlocked.Read(ref _lastByteTicksUtc), DateTimeKind.Utc);

    public bool IsAlive
    {
        get
        {
            try
            {
                return !_process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    public string Stderr
    {
        get
        {
            lock (_lock)
                return _stderr.ToString();
        }
    }

    StreamSessionProcess(Process process, string sessionId, Action<string> onRawLine)
    {
        _process = process;
        SessionId = sessionId;
        _onRawLine = onRawLine;
    }

    public static StreamSessionProcess Start(
        IClaudeInvocation invocation,
        IReadOnlyList<string> arguments,
        string sessionId,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        Action<string> onRawLine)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.Executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // All three, and stdin WITHOUT a BOM: a byte-order mark on the first line of stdin is
            // not JSON, and the first message of every session would be refused.
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var leading in invocation.LeadingArguments)
            startInfo.ArgumentList.Add(leading);

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        // A bridge started from inside a Claude Code session would otherwise spawn sessions that
        // refuse to nest — the same scrub the print runner does, for the same reason.
        foreach (var key in startInfo.Environment.Keys.ToList())
        {
            if (key.StartsWith("CLAUDECODE", StringComparison.Ordinal) || key.StartsWith("CLAUDE_CODE_", StringComparison.Ordinal))
                startInfo.Environment.Remove(key);
        }

        foreach (var pair in environment)
            startInfo.Environment[pair.Key] = pair.Value;

        var process = Process.Start(startInfo) ?? throw new Exception($"Process.Start returned null for '{invocation.Executable}'");
        var session = new StreamSessionProcess(process, sessionId, onRawLine);

        _ = Task.Run(session.Drain_Stdout_Async);
        _ = Task.Run(session.Drain_Stderr_Async);

        return session;
    }

    /// <summary>
    /// Writes one message and waits for the turn's <c>result</c>. Three ways it can end and they are
    /// deliberately distinguishable by the caller: the result (success or the CLI's own error), the
    /// process dying mid-conversation (a STRUCTURAL failure — the transport, not the turn), and
    /// silence — either past <paramref name="timeout"/> or past <paramref name="silenceLimit"/>
    /// with nothing arriving at all, which catches a process that is alive and hung long before the
    /// turn timeout would.
    /// </summary>
    public async Task<StreamTurnOutcome> Send_AndAwaitResult_Async(string prompt, TimeSpan timeout, TimeSpan silenceLimit, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // Per TURN, deliberately: the event is emitted at the CHANGE, not every turn (measured 1 in
        // 6 and 1 in 3), so remembering the last value seen is the caller's job — here it must be
        // possible to say "nothing new arrived in this turn" and mean it.
        JsonObject? rateLimitInfo = null;

        // EVERY MESSAGE THIS TURN COULD HAVE ENDED ON, in order. Kept because this loop is the last
        // place they are visible: once the result event arrives, the earlier ones are gone from
        // everything downstream, and a background sub-agent returning after the report re-opens the
        // turn and hands the entry to a later message. SupersededFinals_Rule decides which of these
        // the result did not keep.
        List<(string? MessageId, string Text)> finalLooking = [];

        // WHAT THE SESSION SAID WHEN NOBODY HAD ASKED — see Read_Line below. Filed ahead of this
        // turn's answer, never as it.
        List<string> unprompted = [];
        var promptWritten = false;
        var ourMessageEchoed = false;

        // (1) Whatever the process wrote since the last turn ended was written while no message of
        // ours was outstanding, so none of it can be this message's answer.
        while (_lines.Reader.TryRead(out var waiting))
            Read_Line(waiting);

        try
        {
            await _process.StandardInput.WriteAsync(StreamUserMessage_Json.Build_Line(prompt).AsMemory(), cancellationToken);
            await _process.StandardInput.WriteAsync("\n".AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
            // The pipe is gone: the process died before it could be asked. Structural, not a turn.
            return StreamTurnOutcome.Died(Build_Result(exitCode: Read_ExitCode(), timedOut: false, resultJson: null, stopwatch.Elapsed), Stderr);
        }

        // THE SILENCE CLOCK FOR THIS TURN STARTS HERE, not whenever the process last happened to
        // say something. Left at the previous turn's heartbeat, an idle gap between two prompts
        // longer than silenceLimit made the first check below true before the read loop had even
        // run once — measured on production as 26 of 29 silence kills, every one within 10 s of
        // start. Silence means "nothing since we asked", never "nothing since the turn before".
        Interlocked.Exchange(ref _lastByteTicksUtc, DateTime.UtcNow.Ticks);

        promptWritten = true;
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_lines.Reader.TryRead(out var line))
            {
                var json = Read_Line(line);

                if (json == null)
                    continue;

                stopwatch.Stop();

                var result = Build_Result(0, timedOut: false, json, stopwatch.Elapsed, TurnResult.SupersededFinals_Rule.Texts(finalLooking), unprompted);

                json[StreamSessionProcess_Words.TURN_COST_KEY] = result.TotalCostUsd;
                _onRawLine(json.ToJsonString());

                return StreamTurnOutcome.Completed(result, rateLimitInfo, unprompted.Count);
            }

            if (!IsAlive)
            {
                // Drained to the end and no result: the process died mid-turn.
                stopwatch.Stop();
                return StreamTurnOutcome.Died(Build_Result(Read_ExitCode(), timedOut: false, resultJson: null, stopwatch.Elapsed), Stderr);
            }

            var now = DateTime.UtcNow;

            if (now >= deadline || now - LastByteUtc > silenceLimit)
            {
                stopwatch.Stop();
                var mute = now < deadline;

                return StreamTurnOutcome.Silent(Build_Result(-1, timedOut: true, resultJson: null, stopwatch.Elapsed), mute, now - LastByteUtc);
            }

            await Wait_ForALine_Async(cancellationToken);
        }

        // THE RESULT EVENT WHEN IT ANSWERS THE MESSAGE WE WROTE, null for every other line.
        //
        // A SESSION CAN ANSWER SOMETHING WE NEVER SENT. A background task it started finishes, the
        // CLI wakes it, and it runs a whole turn on its own — init, text, result — with nobody
        // awaiting. Observed 2026-09-11 on fincanva-5: a staging watcher finished at 12:51, the
        // supervisor wrote "Staging now serves the corrected table", and that result sat in the pipe
        // until the owner's next message, whose turn took it as its answer. Each turn after that
        // returned the reply to the message before it, sixteen in a row, until the daemon restarted —
        // and the last real answer was never posted at all.
        //
        // So a result is this message's only once the CLI has echoed the message back (the echo is
        // taken at the moment the message enters a turn, so a message merged into a running
        // unprompted turn is echoed inside it and the one result that follows answers both).
        // A result before the echo answered something else: it is set aside, and filed ahead of the
        // real answer so the owner still gets what the session wrote.
        //
        // A CLI THAT NEVER ECHOES KEEPS THE OLD RULE — first result wins — because demanding an echo
        // it will never send would turn every answer into an unprompted one. _echoesOurMessages is
        // set by the first echo this process sends; before it, only (1) above protects the turn.
        //
        // The unprompted turn's cost is not differenced here: the baseline stays where it was, so it
        // lands in this turn's figure — spent by this session, reported once, never lost.
        JsonObject? Read_Line(string line)
        {
            var json = StreamEvent_Reader.Parse_OrNull(line);

            // The result is logged ENRICHED, everything else verbatim. The event states the
            // PROCESS's running cost, and a reader of the log (/tail, /log) has no baseline to
            // difference it against — so the turn's own figure is stamped on here, beside the
            // raw one, where it is known. CLAUDE.md decision 10: one reader, one number. Without
            // it /tail showed 0.0366 then 0.0422 for two turns the channel recorded as 0.0366
            // and 0.0056, which is the kind of disagreement that costs an afternoon.
            if (json == null || !StreamEvent_Reader.Is_Result(json))
                _onRawLine(line);

            // Not JSON, or JSON this version does not know: skipped, never fatal. The format is
            // undocumented and a line we cannot read is the expected cost of that.
            if (json == null)
                return null;

            if (StreamEvent_Reader.Is_UserReplay(json))
            {
                if (promptWritten)
                {
                    ourMessageEchoed = true;
                    _echoesOurMessages = true;
                }

                return null;
            }

            if (StreamEvent_Reader.Is_RateLimitEvent(json))
            {
                rateLimitInfo = StreamEvent_Reader.Read_RateLimitInfo_OrNull(json) ?? rateLimitInfo;
                return null;
            }

            if (!StreamEvent_Reader.Is_Result(json))
            {
                TurnResult.SupersededFinals_Rule.Track(finalLooking, json);
                return null;
            }

            if (promptWritten && (ourMessageEchoed || !_echoesOurMessages))
                return json;

            // An error result answered nobody either, and its text is the CLI's ("Not logged in"),
            // not the session's: it is logged, never filed as something the session said.
            if (!StreamEvent_Reader.Is_ErrorResult(json))
            {
                var text = StreamEvent_Reader.Read_ResultText(json);

                unprompted.AddRange(TurnResult.SupersededFinals_Rule.Select_Superseded(TurnResult.SupersededFinals_Rule.Texts(finalLooking), text));

                if (text.Trim().Length > 0)
                    unprompted.Add(text);
            }

            finalLooking.Clear();

            json[StreamSessionProcess_Words.UNPROMPTED_KEY] = true;
            _onRawLine(json.ToJsonString());

            return null;
        }
    }

    /// <summary>Ends the process the way the app ends any of them, and never leaves the pipe half-open.</summary>
    public void Stop()
    {
        try
        {
            if (!_process.HasExited)
                _process.StandardInput.Close();
        }
        catch
        {
            // Already gone.
        }

        try
        {
            if (!_process.WaitForExit((int)CLOSE_GRACE.TotalMilliseconds))
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Exited between the wait and the kill.
        }
    }

    public void Dispose()
    {
        Stop();
        _process.Dispose();
    }

    async Task Wait_ForALine_Async(CancellationToken cancellationToken)
    {
        using var slice = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        slice.CancelAfter(POLL_SLICE);

        try
        {
            await _lines.Reader.WaitToReadAsync(slice.Token);
        }
        catch (OperationCanceledException)
        {
            // The slice lapsed, or the app is shutting down; the loop's own checks decide which.
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (ChannelClosedException)
        {
            // The drain finished — the process is gone and the loop notices on its next pass.
        }
    }

    /// <remarks>
    /// <paramref name="unprompted"/> rides in FRONT of the superseded finals: both are things the
    /// session wrote that are not this turn's answer, both must be filed before it, and the
    /// unprompted ones were written first.
    /// </remarks>
    ITurnResult Build_Result(int exitCode, bool timedOut, JsonObject? resultJson, TimeSpan elapsed, IReadOnlyList<string>? finalLooking = null, IReadOnlyList<string>? unprompted = null)
    {
        if (resultJson == null)
            return TurnResult_Parser.Parse(exitCode, timedOut, string.Empty, Stderr, elapsed);

        var parsed = TurnResult_Parser.Parse(exitCode, timedOut, resultJson.ToJsonString(), Stderr, elapsed, finalLooking);
        IReadOnlyList<string> notTheAnswer = unprompted is { Count: > 0 } ? [.. unprompted, .. parsed.SupersededFinals] : parsed.SupersededFinals;

        // THE COST IS THE PROCESS'S RUNNING TOTAL, not this turn's — measured, and the reason this
        // class keeps a baseline at all. Reported straight through, one turn would carry the sum of
        // every turn before it; and because a NEW process resuming the same transcript starts again
        // from zero, the baseline belongs to the process rather than to the session.
        var turnCost = parsed.TotalCostUsd;

        if (parsed.TotalCostUsd != null)
        {
            turnCost = Math.Max(0, parsed.TotalCostUsd.Value - _costBaselineUsd);
            _costBaselineUsd = parsed.TotalCostUsd.Value;
        }

        if (parsed.TotalCostUsd == null && ReferenceEquals(notTheAnswer, parsed.SupersededFinals))
            return parsed;

        return TurnResult_Factory.Create(
            parsed.ExitCode, parsed.TimedOut, parsed.IsError, parsed.Subtype, parsed.ResultText, parsed.SessionId,
            turnCost, parsed.DurationMs, parsed.DurationApiMs, parsed.NumTurns, parsed.ApiErrorStatus,
            parsed.RawStdout, parsed.RawStderr, parsed.Elapsed, supersededFinals: notTheAnswer);
    }

    int Read_ExitCode()
    {
        try
        {
            return _process.HasExited ? _process.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }

    async Task Drain_Stdout_Async()
    {
        try
        {
            string? line;

            while ((line = await _process.StandardOutput.ReadLineAsync()) != null)
            {
                Interlocked.Exchange(ref _lastByteTicksUtc, DateTime.UtcNow.Ticks);
                await _lines.Writer.WriteAsync(line);
            }
        }
        catch
        {
            // The process died; the await loop reports it from the exit code.
        }
        finally
        {
            _lines.Writer.TryComplete();
        }
    }

    async Task Drain_Stderr_Async()
    {
        try
        {
            string? line;

            while ((line = await _process.StandardError.ReadLineAsync()) != null)
            {
                Interlocked.Exchange(ref _lastByteTicksUtc, DateTime.UtcNow.Ticks);

                lock (_lock)
                    _stderr.AppendLine(line);
            }
        }
        catch
        {
            // Same.
        }
    }
}
