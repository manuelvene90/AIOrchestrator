using AIOrchestratorCoreLib.Diagnostics;
using AIOrchestratorCoreLib.Running.TurnLog;
using AIOrchestratorCoreLib.Running.TurnResult;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE TURN LOG BUFFERS ITS LINES — WITHOUT EVER BEING BEHIND WHEN SOMEBODY LOOKS.
///
/// <para>
/// A streaming turn emits a line per tool call, per tool result and per assistant chunk. Each one
/// used to cost a <c>CreateDirectory</c>, a stat and an open/append/close of its own, and a long turn
/// emits hundreds. They are now buffered per file and written in one append.
/// </para>
/// <para>
/// THE BUFFER IS THE RISK AND THESE TESTS ARE ABOUT THE RISK, not about the saving. What this file
/// exists to prevent is a tail that answers "what is it doing right now" with everything except the
/// most recent thing — so it pins the three moments the buffer must be empty: when a turn's result is
/// written (either kind of turn), when anybody reads the log, and at shutdown. And it pins that the
/// text survives whole and in order, because a reordered or half-written line is worse than a slow
/// one.
/// </para>
/// <para>
/// THE COUNTING TESTS NEVER CALL <c>Flush_All</c>. The counters are per-async-flow, but a flush of
/// EVERY file would write out whatever another test class buffered on its own flow and charge those
/// syscalls here — the same cross-talk <c>CHANNEL_LOCK_COLLECTION</c> documents for the lock's
/// process-wide sink. Tests in one class never run concurrently with each other, so this class's own
/// files are safe; other classes' files are not this class's to flush.
/// </para>
/// </summary>
public class TurnLogBufferedAppendTests : IDisposable
{
    const string REQUEST_ID = "repo-1/imp-1/7";

    readonly string _tempFolder;
    readonly string _logFile;

    public TurnLogBufferedAppendTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-turnlog-buffer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _logFile = Path.Combine(_tempFolder, TurnLog_Store.FILE_NAME);
    }

    public void Dispose()
    {
        // Whatever a test left buffered belongs to a file that is about to be deleted; writing it out
        // first keeps the delete from racing a later flush of a path that no longer exists.
        TurnLog_Store.Flush_All();
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void ABurstOfStreamLines_CostsOneWrite_NotOnePerLine()
    {
        const int LINES = 60;

        using var counting = TickIo_Counters.Begin_Scope();

        for (var index = 0; index < LINES; index++)
            TurnLog_Store.Append_StreamEvent(_logFile, REQUEST_ID, $$"""{"type":"assistant","n":{{index}}}""");

        // Three syscalls per flush — the folder, the roll stat, the append — so a burst that fits in
        // one flush window may cost at most a couple of flushes on a machine slow enough to cross it.
        // Before this change the same burst cost 3 per LINE.
        Assert.True(
            counting.Counts.TurnLogFileSyscalls < LINES,
            $"{LINES} streamed lines cost {counting.Counts.TurnLogFileSyscalls} filesystem calls — the "
            + "lines are not being buffered at all, which is the open/write/close per line this change removes.");

        // And nothing was lost to the buffering.
        Assert.Equal(LINES, TurnLog_Store.Read_LastRecords(_logFile, LINES * 2).Count);
    }

    /// <summary>
    /// The control for the assertion above: a counter that is not wired to the write would also read
    /// "fewer than one per line" — it would read zero for everything (CLAUDE.md decision 20).
    /// </summary>
    [Fact]
    public void TheSyscallCounter_IsWiredToARealWrite()
    {
        using var counting = TickIo_Counters.Begin_Scope();

        TurnLog_Store.Append_TurnResult(_logFile, REQUEST_ID, Build_Result());

        Assert.True(counting.Counts.TurnLogFileSyscalls > 0);
        Assert.True(File.Exists(_logFile));
    }

    /// <summary>
    /// A PRINT turn ends at <c>Append_TurnResult</c>. Read from the raw file rather than through
    /// <c>Read_LastRecords</c>, which flushes and would therefore pass whether or not the result was
    /// ever written on its own.
    /// </summary>
    [Fact]
    public void ThePrintTurnsResult_IsOnDiskTheMomentItIsWritten()
    {
        TurnLog_Store.Append_StreamEvent(_logFile, REQUEST_ID, """{"type":"assistant"}""");
        TurnLog_Store.Append_TurnResult(_logFile, REQUEST_ID, Build_Result());

        Assert.Contains("\"subtype\":\"success\"", File.ReadAllText(_logFile));
    }

    /// <summary>
    /// A STREAMED turn has no <c>Append_TurnResult</c> — its result arrives as one more stream event.
    /// It is the same guarantee and it has to be pinned separately, because it is a different code
    /// path and it is the one the bridge-driven sessions actually take.
    /// </summary>
    [Fact]
    public void AStreamedTurnsOwnResultEvent_IsOnDiskTheMomentItIsWritten()
    {
        TurnLog_Store.Append_StreamEvent(_logFile, REQUEST_ID, """{"type":"assistant"}""");
        TurnLog_Store.Append_StreamEvent(_logFile, REQUEST_ID, """{"type":"result","subtype":"success"}""");

        var onDisk = File.ReadAllText(_logFile);

        Assert.Contains("\"type\":\"result\"", onDisk);

        // The events BEFORE the result go out with it: a result on disk without the turn that
        // produced it would be a tail that answers "why did that fail" with nothing.
        Assert.Contains("\"type\":\"assistant\"", onDisk);
    }

    /// <summary>
    /// AND SO DOES THE CLOSING TURN, which is a third caller and arrived after the buffer did. P2's
    /// closing turn writes its result through this same store on the way out of a session — the last
    /// thing that session ever writes, with no later turn to flush behind it — so a closing result left
    /// in a 200 ms buffer would be a turn the tail never shows. It shares the private overload with
    /// <c>Append_TurnResult</c> today; this pins the guarantee rather than the sharing, so splitting
    /// them fails here instead of going quiet.
    /// </summary>
    [Fact]
    public void TheClosingTurnsResult_IsOnDiskTheMomentItIsWritten()
    {
        TurnLog_Store.Append_StreamEvent(_logFile, REQUEST_ID, """{"type":"assistant"}""");
        TurnLog_Store.Append_ClosingTurnResult(_logFile, REQUEST_ID, Build_Result());

        var onDisk = File.ReadAllText(_logFile);

        Assert.Contains("\"subtype\":\"success\"", onDisk);

        // Tagged as the closing turn and not as an ordinary one: the tail has to be able to say which
        // of the two it is looking at.
        Assert.Contains($"\"{TurnLog_Store.KIND_KEY}\":\"{TurnLog_Store.KIND_CLOSING_TURN}\"", onDisk);
    }

    [Fact]
    public void AReader_SeesWhatIsStillBuffered()
    {
        TurnLog_Store.Append_StreamEvent(_logFile, REQUEST_ID, """{"type":"assistant","n":1}""");

        var records = TurnLog_Store.Read_LastRecords(_logFile, 10);

        Assert.Single(records);
        Assert.Equal(REQUEST_ID, TurnLog_Store.Read_RequestId_OrNull(records[0]));
    }

    [Fact]
    public void Shutdown_WritesOutWhateverIsStillBuffered()
    {
        TurnLog_Store.Append_StreamEvent(_logFile, REQUEST_ID, """{"type":"assistant","n":1}""");

        TurnLog_Store.Flush_All();

        Assert.Contains("\"n\":1", File.ReadAllText(_logFile));
    }

    /// <summary>
    /// The file is read line by line and the reader spans two files, so ORDER is not a nicety. Two
    /// flushes of one file running concurrently would interleave their text; the buffer is written
    /// under the same lock that fills it, and this is what says so.
    /// </summary>
    [Fact]
    public void EveryLineSurvives_InTheOrderItWasWritten()
    {
        const int LINES = 250;

        for (var index = 0; index < LINES; index++)
            TurnLog_Store.Append_StreamEvent(_logFile, REQUEST_ID, $$"""{"type":"assistant","n":{{index}}}""");

        TurnLog_Store.Append_TurnResult(_logFile, REQUEST_ID, Build_Result());

        var records = TurnLog_Store.Read_LastRecords(_logFile, LINES * 2);

        Assert.Equal(LINES + 1, records.Count);

        for (var index = 0; index < LINES; index++)
            Assert.Equal(index, records[index]["n"]!.GetValue<int>());

        Assert.Equal(TurnLog_Store.KIND_TURN, records[^1][TurnLog_Store.KIND_KEY]!.GetValue<string>());
    }

    /// <summary>A line that is not JSON is evidence too, and buffering must not swallow it.</summary>
    [Fact]
    public void AnUnparsableLine_IsStillKept()
    {
        TurnLog_Store.Append_StreamEvent(_logFile, REQUEST_ID, "garbage, not json");

        var records = TurnLog_Store.Read_LastRecords(_logFile, 10);

        Assert.Single(records);
        Assert.Equal("unparsed", records[0]["type"]!.GetValue<string>());
    }

    static ITurnResult Build_Result()
    {
        return TurnResult_Factory.Create(
            exitCode: 0,
            timedOut: false,
            isError: false,
            subtype: "success",
            resultText: "done",
            sessionId: "session-1",
            totalCostUsd: 0.01,
            durationMs: 1234,
            durationApiMs: 1000,
            numTurns: 1,
            apiErrorStatus: null,
            rawStdout: string.Empty,
            rawStderr: string.Empty,
            elapsed: TimeSpan.FromSeconds(1));
    }
}
