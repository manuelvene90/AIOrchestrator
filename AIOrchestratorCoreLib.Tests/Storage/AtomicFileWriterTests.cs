using AIOrchestratorCoreLib.Storage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Storage;

public class AtomicFileWriterTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _targetFile;

    public AtomicFileWriterTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-atomic-writer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _targetFile = Path.Combine(_tempFolder, "state.json");
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void Write_AllText_NewFile_WritesTheContents()
    {
        Atomic_FileWriter.Write_AllText(_targetFile, "{\"a\":1}");

        Assert.Equal("{\"a\":1}", File.ReadAllText(_targetFile));
    }

    [Fact]
    public void Write_AllText_ExistingFile_ReplacesTheContents()
    {
        File.WriteAllText(_targetFile, "old and longer content");

        Atomic_FileWriter.Write_AllText(_targetFile, "new");

        Assert.Equal("new", File.ReadAllText(_targetFile));
    }

    [Fact]
    public void Write_AllText_MissingFolder_CreatesIt()
    {
        var nestedFile = Path.Combine(_tempFolder, "nested", "state.json");

        Atomic_FileWriter.Write_AllText(nestedFile, "value");

        Assert.Equal("value", File.ReadAllText(nestedFile));
    }

    [Fact]
    public void Write_AllText_LeavesNoTempFileBehind()
    {
        Atomic_FileWriter.Write_AllText(_targetFile, "first");
        Atomic_FileWriter.Write_AllText(_targetFile, "second");

        Assert.Empty(Directory.GetFiles(_tempFolder, $"*{Atomic_FileWriter.TEMP_FILE_SUFFIX}"));
    }

    [Fact]
    public void Write_AllText_WhenTheTargetCannotBeReplaced_ThrowsAndLeavesTheOriginalIntact()
    {
        File.WriteAllText(_targetFile, "the original content");

        // An open handle that permits reading but NOT deleting: the write reaches the rename and is
        // refused there — the moment the old truncate-then-write lost the file. Sharing reads is
        // what lets this test then prove the original survived, while the missing Delete right is
        // what makes the rename fail.
        using var openHandle = new FileStream(_targetFile, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.ThrowsAny<Exception>(() => Atomic_FileWriter.Write_AllText(_targetFile, "replacement"));

        Assert.Equal("the original content", File.ReadAllText(_targetFile));
        Assert.Empty(Directory.GetFiles(_tempFolder, $"*{Atomic_FileWriter.TEMP_FILE_SUFFIX}"));
    }
}
