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

        var deadline = DateTime.UtcNow + timeout;

        // Per TURN, deliberately: the event is emitted at the CHANGE, not every turn (measured 1 in
        // 6 and 1 in 3), so remembering the last value seen is the caller's job — here it must be
        // possible to say "nothing new arrived in this turn" and mean it.
        JsonObject? rateLimitInfo = null;

        // EVERY MESSAGE THIS TURN COULD HAVE ENDED ON, in order. Kept because this loop is the last
        // place they are visible: once the result event arrives, the earlier ones are gone from
        // everything downstream, and a background sub-agent returning after the report re-opens the
        // turn and hands the entry to a later message. SupersededFinals_Rule decides which of these
        // the result did not keep.
        List<string> finalLooking = [];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_lines.Reader.TryRead(out var line))
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
                    continue;

                if (StreamEvent_Reader.Is_RateLimitEvent(json))
                {
                    rateLimitInfo = StreamEvent_Reader.Read_RateLimitInfo_OrNull(json) ?? rateLimitInfo;
                    continue;
                }

                if (!StreamEvent_Reader.Is_Result(json))
                {
                    if (TurnResult.SupersededFinals_Rule.Is_FinalLooking(json))
                        finalLooking.Add(StreamEvent_Reader.Read_AssistantText(json));

                    continue;
                }

                stopwatch.Stop();

                var result = Build_Result(0, timedOut: false, json, stopwatch.Elapsed, finalLooking);

                json[StreamSessionProcess_Words.TURN_COST_KEY] = result.TotalCostUsd;
                _onRawLine(json.ToJsonString());

                return StreamTurnOutcome.Completed(result, rateLimitInfo);
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

    ITurnResult Build_Result(int exitCode, bool timedOut, JsonObject? resultJson, TimeSpan elapsed, IReadOnlyList<string>? finalLooking = null)
    {
        if (resultJson == null)
            return TurnResult_Parser.Parse(exitCode, timedOut, string.Empty, Stderr, elapsed);

        var parsed = TurnResult_Parser.Parse(exitCode, timedOut, resultJson.ToJsonString(), Stderr, elapsed, finalLooking);

        // THE COST IS THE PROCESS'S RUNNING TOTAL, not this turn's — measured, and the reason this
        // class keeps a baseline at all. Reported straight through, one turn would carry the sum of
        // every turn before it; and because a NEW process resuming the same transcript starts again
        // from zero, the baseline belongs to the process rather than to the session.
        if (parsed.TotalCostUsd == null)
            return parsed;

        var turnCost = Math.Max(0, parsed.TotalCostUsd.Value - _costBaselineUsd);
        _costBaselineUsd = parsed.TotalCostUsd.Value;

        return TurnResult_Factory.Create(
            parsed.ExitCode, parsed.TimedOut, parsed.IsError, parsed.Subtype, parsed.ResultText, parsed.SessionId,
            turnCost, parsed.DurationMs, parsed.DurationApiMs, parsed.NumTurns, parsed.ApiErrorStatus,
            parsed.RawStdout, parsed.RawStderr, parsed.Elapsed, supersededFinals: parsed.SupersededFinals);
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
