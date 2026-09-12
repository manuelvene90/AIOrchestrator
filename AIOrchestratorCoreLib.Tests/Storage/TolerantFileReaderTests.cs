using System.Diagnostics;
using AIOrchestratorCoreLib.Storage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Storage;

/// <summary>
/// THE READ HALF OF THE ATOMIC WRITER, ON A PLATFORM THAT ENFORCES SHARING.
///
/// <para>
/// Every assertion here is about a collision Linux does not have: Windows honours FileShare, so a
/// reader and <see cref="Atomic_FileWriter"/>'s rename can lock each other out, and the print-runner
/// suite paid for it with an <c>IOException … because it is being used by another process</c> that
/// landed on a different test each run. The cases below hold the file open the way the writer does
/// and assert the reader still answers — and, just as importantly, that the reader being open does
/// not stop the writer.
/// </para>
/// <para>
/// They run on every OS. On Linux the locks below are advisory and the reads would succeed anyway,
/// so nothing here can fail there — that is a weaker test on that platform, not a skipped one, and a
/// regression on Windows is caught where it happens.
/// </para>
/// </summary>
public class TolerantFileReaderTests : IDisposable
{
    readonly string _folder;
    readonly string _file;

    public TolerantFileReaderTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"aiorch-tolerant-reader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
        _file = Path.Combine(_folder, "print-session.json");
        File.WriteAllText(_file, "{\"session_id\":\"abc\"}");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }
    }

    [Fact]
    public void APlainFile_ReadsExactly()
    {
        Assert.Equal("{\"session_id\":\"abc\"}", Tolerant_FileReader.Read_AllText(_file));
    }

    /// <summary>
    /// THE COLLISION ITSELF. <c>File.ReadAllText</c> asks for <c>FileShare.Read</c> and throws against
    /// a writer holding the file for write; this reader must not — on Windows that exception WAS the
    /// red, and on Linux this case is simply a read.
    /// </summary>
    [Fact]
    public void AFileAWriterIsHoldingOpen_IsStillRead()
    {
        using var writerHandle = new FileStream(_file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal("{\"session_id\":\"abc\"}", Tolerant_FileReader.Read_AllText(_file));
    }

    /// <summary>
    /// THE OTHER DIRECTION, WHICH A TOLERANT READER ALONE DOES NOT FIX. Found by this test rather than
    /// assumed: on Windows a replacing rename needs DELETE on the target and an open reader refuses it,
    /// so <c>File.Move(overwrite: true)</c> throws <c>UnauthorizedAccessException</c> even against a
    /// reader that granted <c>FileShare.Delete</c>. Making only the reader tolerant would therefore
    /// have moved the red from the reader onto the writer and looked like a fix — the writer's rename
    /// carries the same backoff, and this pins it.
    /// <para>
    /// The reader is released on another thread partway through that backoff, because that is the real
    /// shape: a read of these files is microseconds, never a hold.
    /// </para>
    /// </summary>
    [Fact]
    public void AReaderHoldingTheFileForAMoment_DoesNotCostTheAtomicWriterItsRename()
    {
        var readerHandle = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var release = Task.Run(async () =>
        {
            await Task.Delay(50);
            readerHandle.Dispose();
        });

        Atomic_FileWriter.Write_AllText(_file, "{\"session_id\":\"def\"}");

        release.Wait();

        Assert.Equal("{\"session_id\":\"def\"}", Tolerant_FileReader.Read_AllText(_file));
    }

    /// <summary>
    /// And it still THROWS on a hold that never goes: a write that did not happen must never be
    /// reported as one, and the caller's file is left with its old contents rather than a mix.
    /// </summary>
    [Fact]
    public void AReaderThatNeverLetsGo_FailsTheWrite_LeavingTheOldContents()
    {
        if (!OperatingSystem.IsWindows())
            return; // Elsewhere the rename simply succeeds over the open handle, which is the point of the case.

        using var readerHandle = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        Assert.ThrowsAny<Exception>(() => Atomic_FileWriter.Write_AllText(_file, "{\"session_id\":\"def\"}"));

        Assert.Equal("{\"session_id\":\"abc\"}", Tolerant_FileReader.Read_AllText(_file));
    }

    /// <summary>
    /// A file that is unopenable only for a moment is READ, not defaulted. The exclusive hold below is
    /// released on another thread partway through the backoff, which is the writer's rename in
    /// miniature.
    /// </summary>
    [Fact]
    public void AFileLockedExclusivelyForAMoment_IsReadOnceTheLockGoes()
    {
        var exclusive = new FileStream(_file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var release = Task.Run(async () =>
        {
            await Task.Delay(50);
            exclusive.Dispose();
        });

        var contents = Tolerant_FileReader.Read_AllText(_file);

        release.Wait();

        Assert.Equal("{\"session_id\":\"abc\"}", contents);
    }

    /// <summary>
    /// IT GIVES UP AND THROWS — it never answers empty. A caller whose file must parse needs the
    /// exception; an empty string there reads as "there is no session", which is a different and
    /// wrong fact. Bounded, too: a permanent lock must not cost more than the backoff budget.
    /// </summary>
    [Fact]
    public void AFileLockedForGood_ThrowsRatherThanAnsweringEmpty_AndGivesUpQuickly()
    {
        if (!OperatingSystem.IsWindows())
            return; // FileShare.None is not enforced elsewhere, so there is no lock here to lose to.

        using var exclusive = new FileStream(_file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var stopwatch = Stopwatch.StartNew();

        Assert.Throws<IOException>(() => Tolerant_FileReader.Read_AllText(_file));

        Assert.True(stopwatch.ElapsedMilliseconds < 3_000, $"the reader spent {stopwatch.ElapsedMilliseconds} ms on a lock it was never going to win");
    }

    /// <summary>
    /// UnauthorizedAccessException IS RETRIED, and this is the case that matters most: Windows answers
    /// an open of a file already marked for deletion with STATUS_DELETE_PENDING, which reaches .NET as
    /// UnauthorizedAccessException and NOT as an IOException. That is the delete-pending window this
    /// class was written for, so a reader that retried only IOException would have missed it.
    /// <para>
    /// STATED HONESTLY: a real delete-pending race cannot be constructed on demand — it is a window of
    /// microseconds inside somebody else's rename, and a test that tried to hit it would be a
    /// coin toss. What is constructed here is a genuine UnauthorizedAccessException that GOES AWAY: on
    /// Windows, opening a directory as a file throws it, so the path starts as a folder and becomes a
    /// file partway through the backoff. That exercises the new catch and the recovery for real; it
    /// does not claim to be the delete-pending case itself.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnauthorizedAccess_ThatGoesAway_IsRetriedRatherThanThrown()
    {
        if (!OperatingSystem.IsWindows())
            return; // Opening a directory as a file is not UnauthorizedAccessException elsewhere.

        var path = Path.Combine(_folder, "becomes-a-file.json");
        Directory.CreateDirectory(path);

        var release = Task.Run(async () =>
        {
            await Task.Delay(50);
            Directory.Delete(path);
            File.WriteAllText(path, "{\"session_id\":\"ghi\"}");
        });

        var contents = Tolerant_FileReader.Read_AllText(path);

        release.Wait();

        Assert.Equal("{\"session_id\":\"ghi\"}", contents);
    }

    /// <summary>
    /// And when it does NOT go away it is rethrown WITH ITS OWN TYPE — an UnauthorizedAccessException
    /// is not an IOException, and a caller (or a human reading the log) is told which of the two
    /// happened. Bounded, like the IOException case.
    /// </summary>
    [Fact]
    public void AnUnauthorizedAccess_ThatNeverGoesAway_IsRethrownAsItself_AndGivesUpQuickly()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(_folder, "stays-a-directory");
        Directory.CreateDirectory(path);

        var stopwatch = Stopwatch.StartNew();

        Assert.Throws<UnauthorizedAccessException>(() => Tolerant_FileReader.Read_AllText(path));

        Assert.True(stopwatch.ElapsedMilliseconds < 3_000, $"the reader spent {stopwatch.ElapsedMilliseconds} ms on an access denial that was never going to lift");
    }

    // NOT PINNED HERE, and said rather than faked: the same claim for Safe_FileReader and
    // UsageTotals_Reader.Read_Text_Safe cannot be tested through the directory trick above, because
    // both guard on File.Exists first and that is FALSE for a folder — the tolerant reader is never
    // reached. In the real delete-pending case File.Exists is true and it is reached, but that window
    // cannot be constructed on demand. What those two callers get from this change is the retry inside
    // Read_AllText, which the two cases above pin directly; there is no separate behaviour of theirs
    // left to assert.

    /// <summary>
    /// An absent file is an ANSWER, not a race: it throws at once rather than spending the backoff on
    /// every absent-file probe the mirror tick makes by design.
    /// </summary>
    [Fact]
    public void AMissingFile_ThrowsImmediately_WithoutSpendingTheBackoff()
    {
        var stopwatch = Stopwatch.StartNew();

        Assert.Throws<FileNotFoundException>(() => Tolerant_FileReader.Read_AllText(Path.Combine(_folder, "not-here.json")));

        Assert.True(stopwatch.ElapsedMilliseconds < 150, $"an absent file cost {stopwatch.ElapsedMilliseconds} ms — it is being retried as though it were a lock");
    }

    /// <summary>
    /// The swallowing reader keeps its contract on top of the tolerant one: empty for an absent file,
    /// and the contents for one a writer is holding — which it used to fail at.
    /// </summary>
    [Fact]
    public void TheSwallowingReader_StillDefaultsForAnAbsentFile_ButNotForABusyOne()
    {
        Assert.Equal(string.Empty, Safe_FileReader.Read_AllText_OrEmpty(Path.Combine(_folder, "not-here.json")));

        using var writerHandle = new FileStream(_file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal("{\"session_id\":\"abc\"}", Safe_FileReader.Read_AllText_OrEmpty(_file));
    }
}
