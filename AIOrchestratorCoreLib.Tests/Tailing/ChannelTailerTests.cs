using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Tailing;

public class ChannelTailerTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;
    readonly IDiscoveredChannel _channel;

    public ChannelTailerTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-tailer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
        _channel = DiscoveredChannel_Factory.Create_ForImplementer("orch-x", "imp-1", _channelFile);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void Poll_FirstSighting_SkipsExistingHistory()
    {
        File.WriteAllText(_channelFile, "## [1] FROM supervisor — d — old entry\n\nold body\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();

        var result = tailer.Poll([_channel]);
        var quietResult1 = tailer.Poll([_channel]);
        var quietResult2 = tailer.Poll([_channel]);

        Assert.Empty(result.CompletedAppends);
        Assert.Empty(quietResult1.CompletedAppends);
        Assert.Empty(quietResult2.CompletedAppends);
    }

    [Fact]
    public void Poll_AppendedEntry_EmittedAfterQuietPolls()
    {
        File.WriteAllText(_channelFile, "seed\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile, "## [1] FROM implementer — d — report\n\nall green\n");

        var readPoll = tailer.Poll([_channel]);
        var quietPoll1 = tailer.Poll([_channel]);
        var quietPoll2 = tailer.Poll([_channel]);

        Assert.Empty(readPoll.CompletedAppends);
        Assert.Empty(quietPoll1.CompletedAppends);

        var append = Assert.Single(quietPoll2.CompletedAppends);
        var entry = Assert.Single(append.Entries);
        Assert.Equal("report", entry.Subject);
        Assert.Equal("all green", entry.Body);
    }

    [Fact]
    public void Poll_NextHeaderArrives_CompletesPreviousEntryImmediately()
    {
        File.WriteAllText(_channelFile, "seed\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile,
            "## [1] FROM supervisor — d — orders\n\ndo X\n\n" +
            "## [2] FROM implementer — d — in progress\n");

        var result = tailer.Poll([_channel]);

        var append = Assert.Single(result.CompletedAppends);
        var entry = Assert.Single(append.Entries);
        Assert.Equal(1, entry.Index);
        Assert.Equal("orders", entry.Subject);
    }

    [Fact]
    public void Poll_EntryEmittedOnce_NeverDuplicated()
    {
        File.WriteAllText(_channelFile, "seed\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile, "## [1] FROM implementer — d — report\n\nbody\n");

        var emittedCount = 0;

        for (var i = 0; i < 6; i++)
            emittedCount += tailer.Poll([_channel]).CompletedAppends.Sum(a => a.Entries.Count);

        Assert.Equal(1, emittedCount);
    }

    [Fact]
    public void Poll_TruncatedFile_ReportsAnomalyAndRecovers()
    {
        File.WriteAllText(_channelFile, "some long seed content here\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.WriteAllText(_channelFile, "short\n");

        var result = tailer.Poll([_channel]);

        Assert.Contains(_channelFile, result.TruncatedFiles);
    }

    [Fact]
    public void Get_OffsetsSnapshot_RestartWithPersistedOffsets_DoesNotReMirrorOldEntries()
    {
        File.WriteAllText(_channelFile, "seed\n");
        var tailer = ChannelTailer_Factory.Create_Fresh();
        tailer.Poll([_channel]);

        File.AppendAllText(_channelFile, "## [1] FROM implementer — d — report\n\nbody\n");
        tailer.Poll([_channel]);
        tailer.Poll([_channel]);
        tailer.Poll([_channel]);

        var restartedTailer = ChannelTailer_Factory.Create(tailer.Get_OffsetsSnapshot());

        var afterRestart1 = restartedTailer.Poll([_channel]);
        var afterRestart2 = restartedTailer.Poll([_channel]);
        var afterRestart3 = restartedTailer.Poll([_channel]);

        Assert.Empty(afterRestart1.CompletedAppends);
        Assert.Empty(afterRestart2.CompletedAppends);
        Assert.Empty(afterRestart3.CompletedAppends);
    }
}
