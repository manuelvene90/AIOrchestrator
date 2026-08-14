using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// The lock now mediates every channel write in the system, and its failure modes were all silent:
/// a wedged channel said nothing, a broken lock said nothing, and a give-up said nothing to anyone
/// who did not inspect the returned bool — which 23 of 24 call sites discard.
/// <para>
/// A well-built mechanism whose failures cannot be observed is the shape this repo has repeatedly
/// paid for, so these pin that each failure emits one line naming the channel, the reason and the
/// wait. They assert on CONTENT, not merely that something was emitted: a diagnostic that does not
/// say which channel or why is the silence again in a longer form.
/// </para>
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class ChannelLockDiagnosticsTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;
    readonly List<string> _lines = [];

    public ChannelLockDiagnosticsTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-lock-diagnostics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
        File.WriteAllText(_channelFile, "seed\n");

        ChannelLock_Diagnostics.Set_Sink(line => { lock (_lines) _lines.Add(line); });
    }

    public void Dispose()
    {
        ChannelLock_Diagnostics.Clear_Sink();
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void GivingUpOnAHeldLock_ReportsTheChannelTheReasonAndTheWait()
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);
        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow, "session"));

        ChannelFile_Lock.Try_Run_WithLock(_channelFile, TimeSpan.FromMilliseconds(300), () => { }, out _);

        var line = Assert.Single(_lines);

        Assert.Contains("channel.md", line);
        Assert.Contains("could not acquire", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ms", line);

        // The holder is the actionable part: a wedged channel is diagnosed by knowing WHO holds it.
        Assert.Contains("4242", line);
    }

    [Fact]
    public void BreakingAStaleLock_SaysSoAndNamesTheHolderItBroke()
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);
        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow.AddSeconds(-(ChannelFile_Lock.STALE_SECONDS + 30)), "session"));

        var acquired = ChannelFile_Lock.Try_Run_WithLock(_channelFile, TimeSpan.FromSeconds(5), () => { }, out _);

        Assert.True(acquired);
        Assert.Contains(_lines, l => l.Contains("broke", StringComparison.OrdinalIgnoreCase) && l.Contains("channel.md") && l.Contains("4242"));
    }

    [Fact]
    public void BreakingAnAbandonedMetadataLessLock_SaysThatIsWhatItWas()
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);
        Directory.SetLastWriteTimeUtc(lockDirectory, DateTime.UtcNow.AddSeconds(-(ChannelFile_Lock.STALE_SECONDS + 30)));

        ChannelFile_Lock.Try_Run_WithLock(_channelFile, TimeSpan.FromSeconds(5), () => { }, out _);

        Assert.Contains(_lines, l => l.Contains("no owner file", StringComparison.OrdinalIgnoreCase) && l.Contains("channel.md"));
    }

    /// <summary>
    /// The disjoint half: an ordinary uncontended write must stay silent, or the log becomes a
    /// firehose nobody reads and the real failures are buried in it.
    /// </summary>
    [Fact]
    public void AnUncontendedWrite_SaysNothingAtAll()
    {
        var acquired = ChannelFile_Lock.Try_Run_WithLock(_channelFile, TimeSpan.FromSeconds(2), () => { }, out _);

        Assert.True(acquired);
        Assert.Empty(_lines);
    }
}
