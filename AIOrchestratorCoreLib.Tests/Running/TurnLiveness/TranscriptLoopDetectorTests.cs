using System.Globalization;
using System.Text;
using AIOrchestratorCoreLib.Running.TurnLiveness;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.TurnLiveness;

/// <summary>
/// The loop detector is a kill signal, so the direction that matters is the false positive: a turn
/// polling a build that changes, a call still in flight, an earlier turn's steps or a sub-agent's must
/// never read as a loop. The transcripts here are small JSONL files in the shape the CLI wrote on this
/// machine on 2026-09-11 (assistant tool_use lines, user tool_result lines, ISO timestamps).
/// </summary>
public class TranscriptLoopDetectorTests : IDisposable
{
    static readonly DateTime TURN_START = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    readonly string _folder;
    readonly string _transcript;

    public TranscriptLoopDetectorTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"aiorch-transcript-loop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
        _transcript = Path.Combine(_folder, "3f2c9a4e-1b7d-4c55-9e0a-6d8b2f1c7a90.jsonl");
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_folder);
    }

    [Fact]
    public void Describe_FourIdenticalBashStepsWithIdenticalResults_NamesBashAndTheCount()
    {
        var transcript = new Transcript();

        for (var i = 0; i < 4; i++)
            transcript.Step("Bash", """{"command":"git status"}""", "\"nothing to commit\"");

        var reason = Describe(transcript);

        Assert.NotNull(reason);
        Assert.Contains("Bash", reason);
        Assert.Contains("4 times", reason);
    }

    [Fact]
    public void Describe_ThreeIdenticalBashSteps_ReturnsNull()
    {
        var transcript = new Transcript();

        for (var i = 0; i < 3; i++)
            transcript.Step("Bash", """{"command":"git status"}""", "\"nothing to commit\"");

        Assert.Null(Describe(transcript));
    }

    [Fact]
    public void Describe_FourIdenticalCallsButOneResultDiffers_ReturnsNull()
    {
        // Polling a build whose output changes is progress, not a loop.
        var transcript = new Transcript();
        transcript.Step("Bash", """{"command":"tail build.log"}""", "\"building 1/3\"");
        transcript.Step("Bash", """{"command":"tail build.log"}""", "\"building 2/3\"");
        transcript.Step("Bash", """{"command":"tail build.log"}""", "\"building 2/3\"");
        transcript.Step("Bash", """{"command":"tail build.log"}""", "\"building 2/3\"");

        Assert.Null(Describe(transcript));
    }

    [Fact]
    public void Describe_SameEditFailingThreeTimes_NamesEditAndFailure()
    {
        // The error text differs each time (it may carry a timestamp or a temp path); the same call
        // failing is the loop.
        var transcript = new Transcript();

        for (var i = 0; i < 3; i++)
            transcript.Step("Edit", """{"file_path":"/r/a.cs","old_string":"x","new_string":"y"}""", $"\"String not found (attempt {i})\"", isError: true);

        var reason = Describe(transcript);

        Assert.NotNull(reason);
        Assert.Contains("Edit", reason);
        Assert.Contains("failed 3 times", reason);
    }

    [Fact]
    public void Describe_SameEditFailingTwiceThenSucceeding_ReturnsNull()
    {
        var transcript = new Transcript();
        transcript.Step("Edit", """{"file_path":"/r/a.cs","old_string":"x","new_string":"y"}""", "\"String not found\"", isError: true);
        transcript.Step("Edit", """{"file_path":"/r/a.cs","old_string":"x","new_string":"y"}""", "\"String not found\"", isError: true);
        transcript.Step("Edit", """{"file_path":"/r/a.cs","old_string":"x","new_string":"y"}""", "\"The file has been updated\"", isError: false);

        Assert.Null(Describe(transcript));
    }

    [Fact]
    public void Describe_PingPongThreeRoundTrips_NamesBothToolsAndTheCount()
    {
        var transcript = new Transcript();

        for (var i = 0; i < 3; i++)
        {
            transcript.Step("Read", """{"file_path":"/r/a.cs"}""", "\"class A {}\"");
            transcript.Step("Bash", """{"command":"dotnet build"}""", "\"error CS1002\"");
        }

        var reason = Describe(transcript);

        Assert.NotNull(reason);
        Assert.Contains("Read", reason);
        Assert.Contains("Bash", reason);
        Assert.Contains("3 times", reason);
    }

    [Fact]
    public void Describe_PingPongBrokenOnTheLastStep_ReturnsNull()
    {
        var transcript = new Transcript();
        transcript.Step("Read", """{"file_path":"/r/a.cs"}""", "\"class A {}\"");
        transcript.Step("Bash", """{"command":"dotnet build"}""", "\"error CS1002\"");
        transcript.Step("Read", """{"file_path":"/r/a.cs"}""", "\"class A {}\"");
        transcript.Step("Bash", """{"command":"dotnet build"}""", "\"error CS1002\"");
        transcript.Step("Read", """{"file_path":"/r/a.cs"}""", "\"class A {}\"");
        transcript.Step("Grep", """{"pattern":"CS1002"}""", "\"no matches\"");

        Assert.Null(Describe(transcript));
    }

    [Fact]
    public void Describe_LoopStampedEntirelyBeforeTheTurn_ReturnsNull()
    {
        // A resumed session: the loop happened in an EARLIER turn and must not kill this one.
        var transcript = new Transcript(TURN_START.AddHours(-1));

        for (var i = 0; i < 4; i++)
            transcript.Step("Bash", """{"command":"git status"}""", "\"nothing to commit\"");

        Assert.Null(Describe(transcript));
    }

    [Fact]
    public void Describe_RepeatSpanningTheTurnStart_CountsOnlyThisTurnsSteps()
    {
        var transcript = new Transcript(TURN_START.AddMinutes(-3));

        // Three before the floor, one after: only one step belongs to this turn.
        for (var i = 0; i < 3; i++)
            transcript.Step("Bash", """{"command":"git status"}""", "\"nothing to commit\"");

        transcript.Move_Clock_To(TURN_START);
        transcript.Step("Bash", """{"command":"git status"}""", "\"nothing to commit\"");

        Assert.Null(Describe(transcript));

        // Three more inside the turn make four that belong to it: the floor filtered, it did not blind.
        for (var i = 0; i < 3; i++)
            transcript.Step("Bash", """{"command":"git status"}""", "\"nothing to commit\"");

        Assert.NotNull(Describe(transcript));
    }

    [Fact]
    public void Describe_SidechainStepsInterleaved_AreIgnored()
    {
        // Sub-agent lines between the parent's identical calls neither break the parent's loop...
        var transcript = new Transcript();

        for (var i = 0; i < 4; i++)
        {
            transcript.Step("Bash", """{"command":"git status"}""", "\"nothing to commit\"");
            transcript.Step("Grep", $$"""{"pattern":"p{{i}}"}""", $"\"hit {i}\"", sidechain: true);
        }

        Assert.NotNull(Describe(transcript));
    }

    [Fact]
    public void Describe_LoopMadeOnlyOfSidechainLines_ReturnsNull()
    {
        // ...nor can a sub-agent's own repetition be read as the parent's.
        var transcript = new Transcript();

        for (var i = 0; i < 4; i++)
            transcript.Step("Bash", """{"command":"git status"}""", "\"nothing to commit\"", sidechain: true);

        Assert.Null(Describe(transcript));
    }

    [Fact]
    public void Describe_CallStillWaitingForItsResult_IsNotAStep()
    {
        var transcript = new Transcript();

        for (var i = 0; i < 3; i++)
            transcript.Step("Bash", """{"command":"git status"}""", "\"nothing to commit\"");

        var pendingId = transcript.Call("Bash", """{"command":"git status"}""");

        Assert.Null(Describe(transcript));

        // The same file once the result lands: the fourth step now exists, so it was the missing
        // result — not something else in the fixture — that kept the answer null.
        transcript.Result(pendingId, "\"nothing to commit\"");

        Assert.NotNull(Describe(transcript));
    }

    [Fact]
    public void Describe_SameCallIdWrittenTwice_CountsOnce()
    {
        var transcript = new Transcript();

        for (var i = 0; i < 3; i++)
            transcript.Step("Bash", """{"command":"git status"}""", "\"nothing to commit\"");

        transcript.Raw(transcript.Lines[^2]);

        Assert.Null(Describe(transcript));
    }

    [Fact]
    public void Describe_GarbageLinesAroundALoop_AreSkipped()
    {
        var transcript = new Transcript();
        transcript.Raw("not json at all");
        transcript.Raw("[1,2,3]");
        transcript.Raw("""{"type":"assistant","message":"a string, not an object"}""");
        transcript.Raw("""{"type":"user","message":{"content":[{"type":"tool_result"}]}}""");

        for (var i = 0; i < 4; i++)
        {
            transcript.Step("Bash", """{"command":"git status"}""", "\"nothing to commit\"");
            transcript.Raw("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":");
        }

        Assert.NotNull(Describe(transcript));
    }

    [Fact]
    public void Describe_OnlyGarbageLines_ReturnsNull()
    {
        var transcript = new Transcript();
        transcript.Raw("not json at all");
        transcript.Raw("{\"type\":\"assistant\"");
        transcript.Raw(" ");

        Assert.Null(Describe(transcript));
    }

    [Fact]
    public void Describe_EmptyFile_ReturnsNull()
    {
        File.WriteAllText(_transcript, "");

        Assert.Null(TranscriptLoop_Detector.Describe_Loop_OrNull(_transcript, TURN_START));
    }

    [Fact]
    public void Describe_MissingFileOrBlankPath_ReturnsNull()
    {
        Assert.Null(TranscriptLoop_Detector.Describe_Loop_OrNull(Path.Combine(_folder, "absent.jsonl"), TURN_START));
        Assert.Null(TranscriptLoop_Detector.Describe_Loop_OrNull("", TURN_START));
        Assert.Null(TranscriptLoop_Detector.Describe_Loop_OrNull(_folder, TURN_START));
    }

    [Fact]
    public void Describe_FileLargerThanTheTailWithTheLoopAtTheEnd_IsStillDetected()
    {
        var transcript = new Transcript();
        var padding = "{\"type\":\"user\",\"timestamp\":\"2026-09-11T10:00:00.000Z\",\"message\":{\"role\":\"user\",\"content\":\"" + new string('p', 900) + "\"}}";

        while (transcript.ByteCount < TranscriptLoop_Detector.TAIL_BYTES + 100_000)
            transcript.Raw(padding);

        for (var i = 0; i < 4; i++)
            transcript.Step("Bash", """{"command":"git status"}""", "\"nothing to commit\"");

        transcript.Write_To(_transcript);

        Assert.True(new FileInfo(_transcript).Length > TranscriptLoop_Detector.TAIL_BYTES);
        Assert.NotNull(TranscriptLoop_Detector.Describe_Loop_OrNull(_transcript, TURN_START));
    }

    [Fact]
    public void Describe_InputKeyOrderDiffers_IsTheSameCall()
    {
        var transcript = new Transcript();
        transcript.Step("Bash", """{"a":1,"b":2}""", "\"same\"");
        transcript.Step("Bash", """{"b":2,"a":1}""", "\"same\"");
        transcript.Step("Bash", """{"a":1,"b":2}""", "\"same\"");
        transcript.Step("Bash", """{"b":2 , "a":1}""", "\"same\"");

        Assert.NotNull(Describe(transcript));
    }

    [Fact]
    public void Describe_ResultAsContentBlocks_ComparesTheBlocks()
    {
        var transcript = new Transcript();

        for (var i = 0; i < 4; i++)
            transcript.Step("Read", """{"file_path":"/r/a.png"}""", """[{"type":"text","text":"same"}]""");

        Assert.NotNull(Describe(transcript));
    }

    [Fact]
    public void Describe_Reason_NeverQuotesTheInputOrTheResult()
    {
        var transcript = new Transcript();

        for (var i = 0; i < 4; i++)
            transcript.Step("Bash", """{"command":"echo SECRET-INPUT"}""", "\"SECRET-RESULT\"");

        var reason = Describe(transcript);

        Assert.NotNull(reason);
        Assert.DoesNotContain("SECRET", reason);
    }

    [Fact]
    public void Describe_PingPongBetweenTwoCallsOfTheSameTool_SaysTwo()
    {
        var steps = new List<(string Signature, string Observation, bool IsError, string ToolName)>();

        for (var i = 0; i < 3; i++)
        {
            steps.Add(("Bash\n{\"command\":\"a\"}", "h1/ok", false, "Bash"));
            steps.Add(("Bash\n{\"command\":\"b\"}", "h2/ok", false, "Bash"));
        }

        Assert.Equal("alternated between two Bash calls, with the same results, 3 times", TranscriptLoop_Detector.Describe_Loop_OrNull(steps));
    }

    [Fact]
    public void Describe_NoSteps_ReturnsNull()
    {
        Assert.Null(TranscriptLoop_Detector.Describe_Loop_OrNull(new List<(string, string, bool, string)>()));
    }

    string? Describe(Transcript transcript)
    {
        transcript.Write_To(_transcript);

        return TranscriptLoop_Detector.Describe_Loop_OrNull(_transcript, TURN_START);
    }

    /// <summary>Builds a transcript line by line; every line is stamped one second after the last.</summary>
    sealed class Transcript(DateTime? firstStampUtc = null)
    {
        DateTime _clock = firstStampUtc ?? TURN_START.AddSeconds(1);
        int _nextId;

        public List<string> Lines { get; } = [];

        public long ByteCount { get; private set; }

        public void Step(string toolName, string inputJson, string resultContentJson, bool? isError = null, bool sidechain = false)
        {
            var id = Call(toolName, inputJson, sidechain);
            Result(id, resultContentJson, isError, sidechain);
        }

        public string Call(string toolName, string inputJson, bool sidechain = false)
        {
            var id = $"toolu_{++_nextId:D6}";
            Raw($$$"""{"parentUuid":null,"isSidechain":{{{Json_Bool(sidechain)}}},"type":"assistant","timestamp":"{{{Next_Stamp()}}}","message":{"role":"assistant","content":[{"type":"tool_use","id":"{{{id}}}","name":"{{{toolName}}}","input":{{{inputJson}}}}]}}""");

            return id;
        }

        public void Result(string id, string resultContentJson, bool? isError = null, bool sidechain = false)
        {
            var errorField = isError == null ? "" : $",\"is_error\":{Json_Bool(isError.Value)}";
            Raw($$$"""{"parentUuid":null,"isSidechain":{{{Json_Bool(sidechain)}}},"type":"user","timestamp":"{{{Next_Stamp()}}}","message":{"role":"user","content":[{"tool_use_id":"{{{id}}}","type":"tool_result","content":{{{resultContentJson}}}{{{errorField}}}}]}}""");
        }

        /// <summary>The next line is stamped exactly <paramref name="stampUtc"/>.</summary>
        public void Move_Clock_To(DateTime stampUtc)
        {
            _clock = stampUtc.AddSeconds(-1);
        }

        public void Raw(string line)
        {
            Lines.Add(line);
            ByteCount += Encoding.UTF8.GetByteCount(line) + 1;
        }

        public void Write_To(string path)
        {
            File.WriteAllText(path, string.Join("\n", Lines) + "\n");
        }

        string Next_Stamp()
        {
            _clock = _clock.AddSeconds(1);

            return _clock.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        static string Json_Bool(bool value) => value ? "true" : "false";
    }
}
