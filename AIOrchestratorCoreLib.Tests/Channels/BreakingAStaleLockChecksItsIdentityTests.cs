using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// BREAKING A STALE LOCK MUST HIT THE LOCK THAT WAS JUDGED, NOT WHATEVER IS AT THE PATH.
/// <para>
/// The comment justifying rename-over-delete claimed "only one rename can succeed". That is false:
/// each breaker renames to its own <c>.broken.{guid}</c>, so two breakers never compete for a
/// destination — what stops the second is finding the SOURCE gone. The distinction matters because
/// it is the whole reason a window exists: staleness is judged, then the move happens, and a holder
/// that releases in between leaves a NEW writer's live lock to be broken instead. rev-6 measured 0
/// natural hits in 400 trials and reproduced it with a forced scheduling point.
/// </para>
/// <para>
/// THESE TESTS DO NOT RACE THE SCHEDULER, which is why they are worth having on a machine that
/// cannot fork reliably. The judged token is a PARAMETER, so the interleaving that takes microseconds
/// to hit by luck is constructed directly: passing a token that does not match the lock at the path
/// IS the state that race produces.
/// </para>
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class BreakingAStaleLockChecksItsIdentityTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;
    readonly string _lockDirectory;
    readonly List<string> _lines = [];

    public BreakingAStaleLockChecksItsIdentityTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-break-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);

        _channelFile = Path.Combine(_tempFolder, "channel.md");
        File.WriteAllText(_channelFile, "seed\n");

        _lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);

        ChannelLock_Diagnostics.Set_Sink(line => { lock (_lines) _lines.Add(line); });
    }

    public void Dispose()
    {
        ChannelLock_Diagnostics.Clear_Sink();
        Directory.Delete(_tempFolder, recursive: true);
    }

    /// <summary>
    /// THE DEFECT ITSELF. The breaker judged token A; by the time it moves, the path holds a live
    /// lock with token B. The lock must survive and the near miss must be reported.
    /// </summary>
    [Fact]
    public void ALockReAcquiredBetweenTheJudgementAndTheMove_IsPutBackAndReported()
    {
        Hold_Lock("token-B-the-new-live-holder");

        var broke = ChannelFile_Lock.Try_BreakStale(_lockDirectory, "token-A-the-one-we-judged");

        Assert.False(broke, "a LIVE lock was broken: the token at the path was not the token judged stale.");

        Assert.True(
            Directory.Exists(_lockDirectory),
            "THE DEFECT: the new holder's live lock was renamed away and never put back. Two writers now believe "
            + "they hold this channel, which is the collision the whole protocol exists to prevent.");

        Assert.Equal("token-B-the-new-live-holder", Read_HeldToken());

        var line = Assert.Single(_lines);

        Assert.Contains("NEAR MISS", line);
        Assert.Contains("channel.md", line);
    }

    /// <summary>
    /// The ordinary case still works, or the guard would have made stale locks unbreakable — which
    /// is a worse failure than the one being fixed and is exactly the defect d436278/d39ad14 closed.
    /// </summary>
    [Fact]
    public void ALockStillCarryingTheJudgedToken_IsBrokenNormally()
    {
        Hold_Lock("token-A-the-one-we-judged");

        var broke = ChannelFile_Lock.Try_BreakStale(_lockDirectory, "token-A-the-one-we-judged");

        Assert.True(broke);
        Assert.False(Directory.Exists(_lockDirectory));

        // The evidence is kept beside the channel rather than deleted.
        Assert.NotEmpty(Directory.GetDirectories(_tempFolder, "*.broken.*"));

        Assert.Contains(_lines, line => line.Contains("broke a stale lock"));
    }

    /// <summary>
    /// A METADATA-LESS LOCK MUST STAY BREAKABLE. Only bash can create one, and a lock nobody can
    /// break is a channel nobody can ever write again — the unbreakable-lock defect from d39ad14.
    /// A guard that reintroduced it by refusing to verify would be a regression dressed as safety.
    /// </summary>
    [Fact]
    public void ALockWithNoReadableToken_IsStillBreakable()
    {
        Directory.CreateDirectory(_lockDirectory);
        File.WriteAllText(Path.Combine(_lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME), "pid=4242\n");

        var broke = ChannelFile_Lock.Try_BreakStale(_lockDirectory, judgedToken: null);

        Assert.True(broke, "a lock with no readable token became UNBREAKABLE — that is the d39ad14 defect returning.");
        Assert.False(Directory.Exists(_lockDirectory));
    }

    /// <summary>
    /// The second breaker of the pair finds the source gone. This is what actually serialises two
    /// breakers — the thing the old comment credited to the destination.
    /// </summary>
    [Fact]
    public void BreakingALockThatIsAlreadyGone_ReportsNothingAndBreaksNothing()
    {
        var broke = ChannelFile_Lock.Try_BreakStale(_lockDirectory, "token-A");

        Assert.False(broke);
        Assert.Empty(_lines);
    }

    void Hold_Lock(string token)
    {
        Directory.CreateDirectory(_lockDirectory);

        File.WriteAllText(
            Path.Combine(_lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow, "session", token));
    }

    string? Read_HeldToken()
    {
        var ownerText = File.ReadAllText(Path.Combine(_lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME));

        return ownerText
            .Split('\n')
            .FirstOrDefault(line => line.StartsWith("token=", StringComparison.Ordinal))
            ?.Substring("token=".Length)
            .Trim();
    }
}
