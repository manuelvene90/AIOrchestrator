using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// Compaction rewrites the live channel file WHOLE. An append that lands between the read it
/// builds the new file from and the rename that installs it is written to content the rename then
/// replaces — the entry is gone, with nothing anywhere recording that it existed. This pins that
/// compaction takes the same gate the appender takes, so the two cannot overlap in this process.
/// </summary>
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

    [Fact]
    public void Compaction_WaitsForTheChannelGate_InsteadOfRewritingUnderAWriter()
    {
        Write_ChannelWith(entryCount: 120);

        var compactionStarted = new ManualResetEventSlim(false);
        var compactionFinished = new ManualResetEventSlim(false);

        ChannelWrite_Lock.Run_Serialised(_channelFile, () =>
        {
            var compaction = Task.Run(() =>
            {
                compactionStarted.Set();
                Channel_Compactor.Compact_IfNeeded(_channelFile);
                compactionFinished.Set();
            });

            Assert.True(compactionStarted.Wait(TimeSpan.FromSeconds(5)), "compaction task never started");

            // The gate is held here. A compaction that ignores it rewrites the file underneath this
            // block and signals well inside a second; one that takes the gate cannot signal at all
            // until this block returns.
            Assert.False(
                compactionFinished.Wait(TimeSpan.FromSeconds(2)),
                "compaction rewrote the channel while a writer held the gate");
        });

        Assert.True(compactionFinished.Wait(TimeSpan.FromSeconds(10)), "compaction never completed after the gate was released");
    }

    void Write_ChannelWith(int entryCount)
    {
        var text = string.Empty;

        for (var index = 1; index <= entryCount; index++)
            text += $"## [{index}] FROM supervisor — 2026-08-13 10:00 — subject {index}\n\nbody of entry {index}\n\n";

        File.WriteAllText(_channelFile, text);
    }
}
