using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// The rule these pin, stated once: A MEMO THAT RECORDS WORK AS DONE MUST NOT OUTLIVE A FAILED
/// ATTEMPT TO DO IT.
/// <para>
/// The bridge records "this was reported" in a flag, a set, a counter or a deleted marker file, and
/// almost always BEFORE the append that carries the report. While a failed append THREW, several of
/// those sites were protected by a try/catch they never had to think about. Returning a bool
/// instead removed that protection everywhere at once — the exception that used to stop the memo no
/// longer happens — so each site now has to check.
/// </para>
/// <para>
/// These tests exercise the LOCK, not a mock: the channel is genuinely locked by another holder, so
/// the append genuinely fails, which is the only way to show the memo survives it.
/// </para>
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class AppendFailureDoesNotMarkWorkDoneTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;

    public AppendFailureDoesNotMarkWorkDoneTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-memo-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
        File.WriteAllText(_channelFile, "seed\n");
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    /// <summary>
    /// Holds the channel's lock the way a foreign writer would, so appends against it genuinely fail
    /// for the whole budget.
    /// </summary>
    void Hold_ChannelLocked()
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);

        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow, "session", "another-writer"));
    }

    /// <summary>
    /// The contract the code documents in as many words — "the marker deliberately survives: a report
    /// that could not be made has not been made" — and which held only while a failed append threw
    /// into the surrounding catch.
    /// </summary>
    [Fact]
    public void AFailedAppend_LeavesTheGuardMarkerOnDisk_SoTheNextTickRetries()
    {
        var markerFile = Path.Combine(_tempFolder, "guard-not-in-force.marker");
        File.WriteAllText(markerFile, "a guard stopped working");

        Hold_ChannelLocked();

        var reported = ChannelAppender.Append_AppEntry(_channelFile, "guard not in force", "body", DateTime.Now);

        if (reported)
            File.Delete(markerFile);

        Assert.False(reported);
        Assert.True(File.Exists(markerFile), "the marker was deleted for a report that was never written — the retry is gone");
    }

    /// <summary>
    /// The disjoint half: when the append DOES land, the marker must go, or the same alert repeats
    /// every tick forever — which is the defect the marker file was introduced to stop.
    /// </summary>
    [Fact]
    public void ASuccessfulAppend_DeletesTheGuardMarker_SoTheAlertDoesNotRepeat()
    {
        var markerFile = Path.Combine(_tempFolder, "guard-not-in-force.marker");
        File.WriteAllText(markerFile, "a guard stopped working");

        var reported = ChannelAppender.Append_AppEntry(_channelFile, "guard not in force", "body", DateTime.Now);

        if (reported)
            File.Delete(markerFile);

        Assert.True(reported);
        Assert.False(File.Exists(markerFile));
    }

    /// <summary>
    /// The general shape, pinned once so the sweep has something to point at: a "already reported"
    /// set must only remember entries whose report actually landed.
    /// </summary>
    [Fact]
    public void AFailedAppend_DoesNotRecordTheItemAsAlreadyReported()
    {
        var alreadyReported = new HashSet<string>();

        Hold_ChannelLocked();

        var reported = ChannelAppender.Append_AppEntry(_channelFile, "3 entries are INVISIBLE", "body", DateTime.Now);

        if (reported)
            alreadyReported.Add("channel.md|## [12 FROM");

        Assert.False(reported);
        Assert.Empty(alreadyReported);
    }
}
