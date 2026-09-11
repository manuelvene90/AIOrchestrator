using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace ClaudeContract.Tests;

/// <summary>
/// A LIVE `claude -p --input-format stream-json` process, driven the way the bridge drives one: the
/// process is started once, every message is one JSON line on stdin, and the turn is over when a
/// <c>result</c> event comes back. Stdout is drained by its own thread into a queue — a reader that
/// waits for the result while the pipe fills is the deadlock this transport is most likely to hit,
/// and it is the same shape the bridge's runner must use.
///
/// Every raw line is kept, so a test can save the whole stream as evidence (what
/// <c>last-run/</c> holds after a Live run) and a formatter can be pinned against real bytes.
/// </summary>
public sealed class StreamProcess : IDisposable
{
    public const string TYPE_RESULT = "result";

    readonly Process _process;
    readonly BlockingCollection<string> _lines = [];
    readonly List<string> _rawLines = [];
    readonly StringBuilder _stderr = new();
    readonly Lock _lock = new();

    public IReadOnlyList<string> RawLines
    {
        get
        {
            lock (_lock)
                return [.. _rawLines];
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

    public int Pid => _process.Id;
    public bool HasExited => _process.HasExited;

    StreamProcess(Process process)
    {
        _process = process;
    }

    public static StreamProcess Start(
        (string Executable, IReadOnlyList<string> LeadingArguments) cli,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = cli.Executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var argument in cli.LeadingArguments.Concat(arguments))
            startInfo.ArgumentList.Add(argument);

        foreach (var key in startInfo.Environment.Keys.ToList())
        {
            if (key.StartsWith("CLAUDECODE", StringComparison.Ordinal) || key.StartsWith("CLAUDE_CODE_", StringComparison.Ordinal))
                startInfo.Environment.Remove(key);
        }

        if (environment != null)
        {
            foreach (var pair in environment)
                startInfo.Environment[pair.Key] = pair.Value;
        }

        var process = Process.Start(startInfo) ?? throw new Exception($"Process.Start returned null for '{cli.Executable}'");
        var stream = new StreamProcess(process);

        new Thread(stream.Pump_Stdout) { IsBackground = true }.Start();
        new Thread(stream.Pump_Stderr) { IsBackground = true }.Start();

        return stream;
    }

    /// <summary>Writes one user message and returns every event up to and including its <c>result</c>.</summary>
    public IReadOnlyList<JsonObject> Send_AndReadUntilResult(string text, TimeSpan timeout)
    {
        Send(text);
        return Read_UntilResult(timeout);
    }

    public void Send(string text)
    {
        var message = new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            },
        };

        _process.StandardInput.Write(message.ToJsonString() + "\n");
        _process.StandardInput.Flush();
    }

    /// <summary>Writes a raw line — for the malformed-input measure, which a typed sender could not express.</summary>
    public void Send_RawLine(string line)
    {
        _process.StandardInput.Write(line + "\n");
        _process.StandardInput.Flush();
    }

    public IReadOnlyList<JsonObject> Read_UntilResult(TimeSpan timeout)
    {
        List<JsonObject> events = [];
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (!_lines.TryTake(out var line, (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds)))
                break;

            var json = Parse_OrNull(line);

            // A line that is not JSON is not an error here: the measure is what the bridge must
            // survive, and the bridge skips it too.
            if (json == null)
                continue;

            events.Add(json);

            if (json["type"]?.GetValue<string>() == TYPE_RESULT)
                return events;
        }

        throw new Exception($"no 'result' event within {timeout.TotalSeconds:F0}s. Events so far:\n{string.Join("\n", RawLines)}\n--- stderr ---\n{Stderr}");
    }

    static JsonObject? Parse_OrNull(string line)
    {
        try
        {
            return JsonNode.Parse(line) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    public void Close_Stdin()
    {
        try
        {
            _process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Already gone — the exit code says why.
        }
    }

    /// <summary>
    /// SIGTERM on Unix, <c>Kill</c> on Windows: there is no SIGTERM there, so a test asserting the
    /// 143 exit code is meaningful only off Windows. Stated rather than papered over.
    /// </summary>
    public bool Request_Termination()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _process.Kill(entireProcessTree: true);
            return false;
        }

        using var kill = Process.Start(new ProcessStartInfo("/bin/kill", ["-TERM", _process.Id.ToString()]) { UseShellExecute = false })
            ?? throw new Exception("could not start /bin/kill");

        kill.WaitForExit();
        return true;
    }

    public int Wait_ForExit(TimeSpan timeout)
    {
        if (!_process.WaitForExit((int)timeout.TotalMilliseconds))
            throw new Exception($"the process did not exit within {timeout.TotalSeconds:F0}s");

        return _process.ExitCode;
    }

    public string Describe()
    {
        return $"pid={Pid} exited={_process.HasExited}\n--- stdout ({RawLines.Count} lines) ---\n{string.Join("\n", RawLines)}\n--- stderr ---\n{Stderr}";
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Exited between the check and the kill.
        }

        _process.Dispose();
    }

    void Pump_Stdout()
    {
        try
        {
            string? line;

            while ((line = _process.StandardOutput.ReadLine()) != null)
            {
                lock (_lock)
                    _rawLines.Add(line);

                _lines.Add(line);
            }
        }
        catch
        {
            // The process died; the exit code is the report.
        }
        finally
        {
            _lines.CompleteAdding();
        }
    }

    void Pump_Stderr()
    {
        try
        {
            string? line;

            while ((line = _process.StandardError.ReadLine()) != null)
            {
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
