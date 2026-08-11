using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Logging;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Logging;

public class OrchestrationLogTests : IDisposable
{
    readonly string _tempFolder;
    readonly ISupervisionPaths _paths;

    public OrchestrationLogTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-log-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _paths = SupervisionPaths_Factory.Create(_tempFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    // Every file-writing test below logs at Warning, never Info: Info is exactly what the low-disk
    // guard drops, so an Info-based assertion would silently depend on the test machine's free
    // space. The guard's own policy is tested as a pure function instead, further down.

    [Fact]
    public void Log_Warning_AppendsOneJsonLine_AndRaisesEntryLogged()
    {
        var log = OrchestrationLog_Factory.Create(_paths);
        List<IOrchestrationLogEntry> raised = [];
        log.EntryLogged += entry => raised.Add(entry);

        log.Log_Warning("orch-x", "the bridge stalled");

        var line = Assert.Single(File.ReadAllLines(_paths.GlobalLogFile));
        var node = JsonNode.Parse(line) as JsonObject;

        Assert.NotNull(node);
        Assert.Equal("orch-x", (string?)node["orch"]);
        Assert.Equal("Warning", (string?)node["level"]);
        Assert.Equal("the bridge stalled", (string?)node["message"]);

        var raisedEntry = Assert.Single(raised);
        Assert.Equal("the bridge stalled", raisedEntry.Message);
        Assert.Equal(LogLevels.Warning, raisedEntry.Level);
    }

    [Fact]
    public void Write_GlobalLogAtCap_RotatesOldContentIntoPreviousGeneration()
    {
        Fill_PastCap(_paths.GlobalLogFile, "OLD-GENERATION-MARKER");
        var log = OrchestrationLog_Factory.Create(_paths);

        log.Log_Warning("", "written after rotation");

        var previousGenerationFile = Log_WriteGuards.Build_PreviousGenerationPath(_paths.GlobalLogFile);

        Assert.True(File.Exists(previousGenerationFile));
        Assert.Contains("OLD-GENERATION-MARKER", File.ReadAllText(previousGenerationFile));

        var liveText = File.ReadAllText(_paths.GlobalLogFile);

        Assert.DoesNotContain("OLD-GENERATION-MARKER", liveText);
        Assert.Contains("written after rotation", Assert.Single(File.ReadAllLines(_paths.GlobalLogFile)));
    }

    [Fact]
    public void Write_PerOrchestrationLogAtCap_RotatesThatFileToo()
    {
        var orchestrationFolder = _paths.Get_OrchestrationFolder("orch-x");
        Directory.CreateDirectory(orchestrationFolder);
        Fill_PastCap(_paths.Get_OrchestrationLogFile("orch-x"), "OLD-ORCH-MARKER");
        var log = OrchestrationLog_Factory.Create(_paths);

        log.Log_Warning("orch-x", "written after rotation");

        var previousGenerationFile =
            Log_WriteGuards.Build_PreviousGenerationPath(_paths.Get_OrchestrationLogFile("orch-x"));

        Assert.True(File.Exists(previousGenerationFile));
        Assert.Contains("OLD-ORCH-MARKER", File.ReadAllText(previousGenerationFile));
        Assert.Contains(
            "written after rotation",
            Assert.Single(File.ReadAllLines(_paths.Get_OrchestrationLogFile("orch-x"))));
    }

    [Fact]
    public void Write_WithPreviousGenerationAlreadyPresent_ReplacesItWithoutThrowing()
    {
        var previousGenerationFile = Log_WriteGuards.Build_PreviousGenerationPath(_paths.GlobalLogFile);
        File.WriteAllText(previousGenerationFile, "EVEN-OLDER-GENERATION\n");
        Fill_PastCap(_paths.GlobalLogFile, "CURRENT-GENERATION-MARKER");
        var log = OrchestrationLog_Factory.Create(_paths);

        log.Log_Warning("", "written after rotation");

        var previousGenerationText = File.ReadAllText(previousGenerationFile);

        Assert.Contains("CURRENT-GENERATION-MARKER", previousGenerationText);
        Assert.DoesNotContain("EVEN-OLDER-GENERATION", previousGenerationText);
        Assert.Contains("written after rotation", Assert.Single(File.ReadAllLines(_paths.GlobalLogFile)));
    }

    [Fact]
    public void EntryLogged_StillFires_WhenTheFileWriteCannotHappen()
    {
        var log = OrchestrationLog_Factory.Create(_paths);
        List<IOrchestrationLogEntry> raised = [];
        log.EntryLogged += entry => raised.Add(entry);

        // FileShare.None on Windows makes the append fail deterministically, no permissions needed.
        using (File.Open(_paths.GlobalLogFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            log.Log_Warning("", "nowhere to land");

        var raisedEntry = Assert.Single(raised);

        Assert.Equal("nowhere to land", raisedEntry.Message);
        Assert.Equal(0, new FileInfo(_paths.GlobalLogFile).Length);
    }

    [Fact]
    public void WriteFailures_AreReported_OnTheNextSuccessfulWrite()
    {
        var log = OrchestrationLog_Factory.Create(_paths);

        using (File.Open(_paths.GlobalLogFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            log.Log_Warning("", "lost one");
            log.Log_Warning("", "lost two");
        }

        log.Log_Warning("", "the disk is back");

        var lines = File.ReadAllLines(_paths.GlobalLogFile);

        Assert.Equal(2, lines.Length);
        Assert.Contains("log guard recovered", lines[0]);
        Assert.Contains("lost 2 entries to write failures", lines[0]);
        Assert.Contains("the disk is back", lines[1]);
    }

    [Fact]
    public void WriteFailures_AreReportedOnce_NotOnEverySubsequentWrite()
    {
        var log = OrchestrationLog_Factory.Create(_paths);

        using (File.Open(_paths.GlobalLogFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            log.Log_Warning("", "lost one");

        log.Log_Warning("", "first good line");
        log.Log_Warning("", "second good line");

        var recoveryLineCount = File.ReadAllLines(_paths.GlobalLogFile)
            .Count(line => line.Contains("log guard recovered"));

        Assert.Equal(1, recoveryLineCount);
    }

    [Fact]
    public void Should_Drop_Entry_UnknownFreeSpace_NeverBlocks()
    {
        Assert.False(Log_WriteGuards.Should_Drop_Entry(null, LogLevels.Info));
        Assert.False(Log_WriteGuards.Should_Drop_Entry(null, LogLevels.Warning));
        Assert.False(Log_WriteGuards.Should_Drop_Entry(null, LogLevels.Error));
    }

    [Fact]
    public void Should_Drop_Entry_BelowThreshold_DropsInfoAndKeepsWarningAndError()
    {
        var lowFreeBytes = Log_WriteGuards.LOW_DISK_THRESHOLD_BYTES - 1;

        Assert.True(Log_WriteGuards.Should_Drop_Entry(lowFreeBytes, LogLevels.Info));
        Assert.True(Log_WriteGuards.Should_Drop_Entry(0, LogLevels.Info));
        Assert.False(Log_WriteGuards.Should_Drop_Entry(lowFreeBytes, LogLevels.Warning));
        Assert.False(Log_WriteGuards.Should_Drop_Entry(lowFreeBytes, LogLevels.Error));
        Assert.False(Log_WriteGuards.Should_Drop_Entry(0, LogLevels.Error));
    }

    [Fact]
    public void Should_Drop_Entry_AtOrAboveThreshold_KeepsEverything()
    {
        Assert.False(Log_WriteGuards.Should_Drop_Entry(Log_WriteGuards.LOW_DISK_THRESHOLD_BYTES, LogLevels.Info));
        Assert.False(Log_WriteGuards.Should_Drop_Entry(long.MaxValue, LogLevels.Info));
    }

    [Fact]
    public void Build_RecoveryMessage_OrNull_NothingSwallowed_ReturnsNull()
    {
        Assert.Null(Log_WriteGuards.Build_RecoveryMessage_OrNull(0, 0, ""));
    }

    [Fact]
    public void Build_RecoveryMessage_OrNull_ReportsBothCountsAndTheLastFailure()
    {
        var message = Log_WriteGuards.Build_RecoveryMessage_OrNull(7, 3, "IOException: file is locked");

        Assert.NotNull(message);
        Assert.Contains("dropped 7 entries", message);
        Assert.Contains("512 MB", message);
        Assert.Contains("lost 3 entries to write failures", message);
        Assert.Contains("IOException: file is locked", message);
    }

    [Fact]
    public void Build_RecoveryMessage_OrNull_LongFailureText_IsBounded()
    {
        var message = Log_WriteGuards.Build_RecoveryMessage_OrNull(0, 1, new string('z', 5000));

        Assert.NotNull(message);
        Assert.DoesNotContain(new string('z', Log_WriteGuards.MAX_FAILURE_MESSAGE_CHARS + 1), message);
    }

    [Fact]
    public void Build_PreviousGenerationPath_KeepsTheExtension_AndSitsBesideTheLog()
    {
        var previousGenerationFile = Log_WriteGuards.Build_PreviousGenerationPath(_paths.GlobalLogFile);

        Assert.Equal("orchestrator-global.log.1.jsonl", Path.GetFileName(previousGenerationFile));
        Assert.Equal(_tempFolder, Path.GetDirectoryName(previousGenerationFile));
    }

    /// <summary>
    /// Writes a marker line plus enough filler to put the file at or past the rotation cap, so the
    /// very next append has to rotate it.
    /// </summary>
    static void Fill_PastCap(string filePath, string markerLine)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? "");

        using var writer = new StreamWriter(filePath, append: false);
        writer.Write(markerLine);
        writer.Write('\n');

        var chunk = new string('x', 64 * 1024);

        for (var writtenBytes = 0L; writtenBytes <= Log_WriteGuards.MAX_LOG_BYTES; writtenBytes += chunk.Length)
            writer.Write(chunk);
    }
}
