using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// The cross-process half of the append protocol. Agent sessions are separate OS processes, so the
/// lock they and the app contend for has to live in the filesystem; these pin the .NET side of that
/// contract. The bash side is pinned against this same lock in <see cref="ChannelAppendHelperInteropTests"/>,
/// because a protocol only one side implements correctly is not a protocol.
/// </summary>
public class ChannelFileLockTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;

    public ChannelFileLockTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-file-lock-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
        File.WriteAllText(_channelFile, "seed\n");
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void Try_Run_WithLock_WhenFree_RunsTheWriteAndReleasesAfterwards()
    {
        var ran = false;

        var acquired = ChannelFile_Lock.Try_Run_WithLock(
            _channelFile,
            TimeSpan.FromSeconds(2),
            () => ran = true,
            out _);

        Assert.True(acquired);
        Assert.True(ran);
        Assert.False(Directory.Exists(ChannelFile_Lock.Build_LockDirectoryPath(_channelFile)));
    }

    [Fact]
    public void Try_Run_WithLock_WhenTheWriteThrows_StillReleasesTheLock()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ChannelFile_Lock.Try_Run_WithLock(
                _channelFile,
                TimeSpan.FromSeconds(2),
                () => throw new InvalidOperationException("the write failed"),
                out _));

        Assert.False(Directory.Exists(ChannelFile_Lock.Build_LockDirectoryPath(_channelFile)));
    }

    /// <summary>
    /// A held lock must be respected, and the caller must be told it could not write. Returning
    /// false rather than writing anyway is the whole point: an unlocked append under contention is
    /// the collision the protocol exists to prevent.
    /// </summary>
    [Fact]
    public void Try_Run_WithLock_WhenHeldAndFresh_DoesNotRunTheWrite()
    {
        Hold_LockExternally(heldSinceUtc: DateTime.UtcNow);

        var ran = false;

        var acquired = ChannelFile_Lock.Try_Run_WithLock(
            _channelFile,
            TimeSpan.FromMilliseconds(600),
            () => ran = true,
            out var waited);

        Assert.False(acquired);
        Assert.False(ran);
        Assert.True(waited >= TimeSpan.FromMilliseconds(400), $"gave up after only {waited.TotalMilliseconds} ms");
    }

    /// <summary>
    /// A holder that died leaves its lock behind forever. Breaking it is what keeps one killed
    /// session from wedging every writer on that channel.
    /// </summary>
    [Fact]
    public void Try_Run_WithLock_WhenHeldButStale_BreaksItAndRunsTheWrite()
    {
        var staleSince = DateTime.UtcNow.AddSeconds(-(ChannelFile_Lock.STALE_SECONDS + 30));
        Hold_LockExternally(heldSinceUtc: staleSince);

        var ran = false;

        var acquired = ChannelFile_Lock.Try_Run_WithLock(
            _channelFile,
            TimeSpan.FromSeconds(5),
            () => ran = true,
            out _);

        Assert.True(acquired);
        Assert.True(ran);
    }

    /// <summary>
    /// A lock directory whose owner file has not been written yet is a holder mid-acquire, not a
    /// dead one — treating "no metadata" as stale would break locks that are microseconds old.
    /// </summary>
    [Fact]
    public void Try_Run_WithLock_WhenHeldWithNoOwnerFileYet_TreatsItAsAliveNotStale()
    {
        Directory.CreateDirectory(ChannelFile_Lock.Build_LockDirectoryPath(_channelFile));

        var acquired = ChannelFile_Lock.Try_Run_WithLock(
            _channelFile,
            TimeSpan.FromMilliseconds(600),
            () => { },
            out _);

        Assert.False(acquired);
    }

    /// <summary>
    /// A lock directory with no owner file is treated as a writer mid-acquire, so it is never
    /// stale — and an OLD one is then unbreakable by both implementations forever. The bash side
    /// can create exactly that state: it mkdirs the lock and then writes the metadata, and a hard
    /// kill in between (the app tree-kills every session on exit, which does not run bash's EXIT
    /// trap) leaves the directory behind empty. Every later writer then waits out its budget and
    /// declines, and the channel is permanently write-dead with nothing saying so.
    /// <para>
    /// So the age of the DIRECTORY is the fallback when there is no metadata to read. A writer
    /// legitimately mid-acquire is microseconds old and unaffected.
    /// </para>
    /// </summary>
    [Fact]
    public void Try_Run_WithLock_WhenHeldWithNoOwnerFileAndTheDirectoryIsOld_BreaksItInstead()
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);
        Directory.SetLastWriteTimeUtc(lockDirectory, DateTime.UtcNow.AddSeconds(-(ChannelFile_Lock.STALE_SECONDS + 30)));

        var ran = false;
        var acquired = ChannelFile_Lock.Try_Run_WithLock(_channelFile, TimeSpan.FromSeconds(5), () => ran = true, out _);

        Assert.True(acquired, "an abandoned metadata-less lock was never breakable — the channel would be write-dead forever");
        Assert.True(ran);
    }

    void Hold_LockExternally(DateTime heldSinceUtc)
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);

        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            $"pid=999999\nutc={heldSinceUtc:yyyy-MM-ddTHH:mm:ssZ}\nrole=test-holder\n");
    }
}
