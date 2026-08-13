using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// Compaction rewrites the live channel file WHOLE. An append that lands between the read it
/// builds the new file from and the rename that installs it is written to content the rename then
/// replaces — the entry is gone, with nothing anywhere recording that it existed. This pins that
/// compaction takes the same gate the appender takes, so the two cannot overlap in this process.
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class ChannelCompactionSerialisationTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;

    public ChannelCompactionSerialisationTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-compaction-lock-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    /// <summary>
    /// While another writer holds the gate, compaction must DECLINE — return null and leave the
    /// file byte-for-byte alone — rather than rewrite it underneath them. It is housekeeping with
    /// no deadline, so giving up and retrying is the correct behaviour, not a degraded one.
    /// </summary>
    [Fact]
    public void Compaction_WhileAWriterHoldsTheGate_DeclinesAndLeavesTheFileUntouched()
    {
        Write_ChannelWith(entryCount: 120);

        var textBefore = File.ReadAllText(_channelFile);
        long? resultWhileHeld = null;

        var held = ChannelWrite_Lock.Try_Run_Serialised(_channelFile, TimeSpan.FromSeconds(5), () =>
        {
            var compaction = Task.Run(() => resultWhileHeld = Channel_Compactor.Compact_IfNeeded(_channelFile));

            Assert.True(compaction.Wait(TimeSpan.FromSeconds(20)), "compaction never returned while the gate was held");
        }, out _);

        Assert.True(held, "the test could not take the gate it is testing against");
        Assert.Null(resultWhileHeld);
        Assert.Equal(textBefore, File.ReadAllText(_channelFile));
    }

    /// <summary>
    /// The other half, and it has to be here: a compactor that simply never compacted would pass
    /// the test above. Once the gate is free the same call must actually do the work.
    /// </summary>
    [Fact]
    public void Compaction_OnceTheGateIsFree_CompactsNormally()
    {
        Write_ChannelWith(entryCount: 120);

        var newLength = Channel_Compactor.Compact_IfNeeded(_channelFile);

        Assert.NotNull(newLength);

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));

        Assert.Equal(45, entries.Count);
    }

    void Write_ChannelWith(int entryCount)
    {
        var text = string.Empty;

        for (var index = 1; index <= entryCount; index++)
            text += $"## [{index}] FROM supervisor — 2026-08-13 10:00 — subject {index}\n\nbody of entry {index}\n\n";

        File.WriteAllText(_channelFile, text);
    }
}
