using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class BridgeStateStoreTests : IDisposable
{
    readonly string _tempFolder;
    readonly ISupervisionPaths _paths;
    readonly RecordingLog _log;

    public BridgeStateStoreTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-bridgestate-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _paths = SupervisionPaths_Factory.Create(_tempFolder);
        _log = new RecordingLog();
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsOffsetsAndLastUpdateId()
    {
        Dictionary<string, long> offsets = new()
        {
            ["C:\\chan\\a.md"] = 128L,
            ["C:\\chan\\b.md"] = 0L,
        };

        BridgeState_Store.Save(_paths, offsets, 987654321L);

        var (loadedOffsets, loadedLastUpdateId) = BridgeState_Store.Load_OrEmpty(_paths);

        Assert.Equal(987654321L, loadedLastUpdateId);
        Assert.Equal(2, loadedOffsets.Count);
        Assert.Equal(128L, loadedOffsets["C:\\chan\\a.md"]);
        Assert.Equal(0L, loadedOffsets["C:\\chan\\b.md"]);
    }

    [Fact]
    public void Load_OrEmpty_MissingFile_ReturnsEmptyState()
    {
        var (offsets, lastUpdateId) = BridgeState_Store.Load_OrEmpty(_paths, _log);

        Assert.Empty(offsets);
        Assert.Equal(0L, lastUpdateId);
        Assert.Empty(_log.Errors);
    }

    [Fact]
    public void Load_OrEmpty_ZeroByteFile_ReturnsEmptyStateAndDoesNotQuarantine()
    {
        File.WriteAllText(_paths.BridgeStateFile, string.Empty);

        var (offsets, lastUpdateId) = BridgeState_Store.Load_OrEmpty(_paths, _log);

        Assert.Empty(offsets);
        Assert.Equal(0L, lastUpdateId);
        Assert.True(File.Exists(_paths.BridgeStateFile), "an empty cursor file is normal and must be left alone");
        Assert.Empty(Find_QuarantinedFiles());
        Assert.Empty(_log.Errors);
    }

    [Fact]
    public void Load_OrEmpty_HalfWrittenJson_ReturnsEmptyStateAndQuarantinesTheBadFile()
    {
        const string HALF_WRITTEN = "{\n  \"fileOffsets\": {\n    \"C:\\\\chan\\\\a.md\": 12";
        File.WriteAllText(_paths.BridgeStateFile, HALF_WRITTEN);

        var (offsets, lastUpdateId) = BridgeState_Store.Load_OrEmpty(_paths, _log);

        Assert.Empty(offsets);
        Assert.Equal(0L, lastUpdateId);

        Assert.False(File.Exists(_paths.BridgeStateFile), "the corrupt text must not be left at the live path");

        var quarantinedFile = Assert.Single(Find_QuarantinedFiles());
        Assert.Equal(HALF_WRITTEN, File.ReadAllText(quarantinedFile));

        var errorMessage = Assert.Single(_log.Errors);
        Assert.Contains(quarantinedFile, errorMessage);
    }

    [Fact]
    public void Load_OrEmpty_JsonOfTheWrongShape_ReturnsEmptyStateAndQuarantinesTheBadFile()
    {
        File.WriteAllText(_paths.BridgeStateFile, "[1, 2, 3]");

        var (offsets, lastUpdateId) = BridgeState_Store.Load_OrEmpty(_paths, _log);

        Assert.Empty(offsets);
        Assert.Equal(0L, lastUpdateId);
        Assert.False(File.Exists(_paths.BridgeStateFile));
        Assert.Single(Find_QuarantinedFiles());
        Assert.Single(_log.Errors);
    }

    [Fact]
    public void Load_OrEmpty_HalfWrittenJson_WithoutALog_StillDoesNotThrow()
    {
        File.WriteAllText(_paths.BridgeStateFile, "{\"lastUpdateId\": ");

        var (offsets, lastUpdateId) = BridgeState_Store.Load_OrEmpty(_paths);

        Assert.Empty(offsets);
        Assert.Equal(0L, lastUpdateId);
    }

    [Fact]
    public void Save_AfterCorruption_WritesACleanCursorAgain()
    {
        File.WriteAllText(_paths.BridgeStateFile, "{\"fileOffsets\": {\"a\": 1");
        BridgeState_Store.Load_OrEmpty(_paths, _log);

        BridgeState_Store.Save(_paths, new Dictionary<string, long> { ["C:\\chan\\a.md"] = 7L }, 42L);

        var (offsets, lastUpdateId) = BridgeState_Store.Load_OrEmpty(_paths, _log);

        Assert.Equal(42L, lastUpdateId);
        Assert.Equal(7L, offsets["C:\\chan\\a.md"]);
    }

    [Fact]
    public void Save_LeavesNoTempFileDebris()
    {
        BridgeState_Store.Save(_paths, new Dictionary<string, long> { ["C:\\chan\\a.md"] = 1L }, 1L);
        BridgeState_Store.Save(_paths, new Dictionary<string, long> { ["C:\\chan\\a.md"] = 2L }, 2L);

        Assert.Empty(Directory.GetFiles(_tempFolder, "*.tmp"));
        Assert.Single(Directory.GetFiles(_tempFolder));
    }

    string[] Find_QuarantinedFiles()
    {
        return Directory.GetFiles(_tempFolder, "*corrupt*");
    }

    /// <summary>Captures what the store reported, so "corruption is visible" can be asserted.</summary>
    sealed class RecordingLog : IOrchestrationLog
    {
        public List<string> Warnings { get; } = [];
        public List<string> Errors { get; } = [];

        public void Log_Info(string orchId, string message)
        {
        }

        public void Log_Warning(string orchId, string message)
        {
            Warnings.Add(message);
        }

        public void Log_Error(string orchId, string message, Exception? exception)
        {
            Errors.Add(message);
        }

        public event Action<IOrchestrationLogEntry>? EntryLogged
        {
            add { }
            remove { }
        }
    }
}
