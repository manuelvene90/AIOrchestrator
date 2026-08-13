using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// The app appends to a channel from ~35 call sites across two loops that run CONCURRENTLY
/// (BridgeEngine starts the mirror loop and the inbound loop and awaits both), and compaction
/// rewrites the same file from the mirror loop. None of that was serialised.
/// <para>
/// Scope of what these tests pin, stated exactly: they cover writes made by THIS PROCESS. A
/// session is a separate OS process appending with its own tooling and no in-process lock can
/// reach it — that is a different mechanism, deliberately not claimed here.
/// </para>
/// </summary>
public class ChannelConcurrentWriteTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;

    public ChannelConcurrentWriteTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-concurrent-write-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    /// <summary>
    /// Two appenders that both read the same "last index" both write it. Reproduces the duplicate
    /// [72] found in imp-2's channel on 2026-08-13, from the app side rather than the session side.
    /// </summary>
    [Fact]
    public void ParallelAppends_EveryEntryGetsItsOwnIndex()
    {
        const int APPEND_COUNT = 40;

        Parallel.For(0, APPEND_COUNT, i =>
        {
            ChannelAppender.Append_AppEntry(_channelFile, $"subject {i}", $"body {i}", DateTime.Now);
        });

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));
        var indices = entries.Select(e => e.Index).ToList();

        Assert.Equal(APPEND_COUNT, entries.Count);
        Assert.Equal(APPEND_COUNT, indices.Distinct().Count());
    }

    /// <summary>
    /// The failure this closes is not a duplicate index but a LOST entry: File.AppendAllText opens
    /// the target deny-write, so a second concurrent appender in the same process does not
    /// interleave — it throws, and the entry it was carrying is never written at all.
    /// </summary>
    [Fact]
    public void ParallelAppends_NoAppenderThrows()
    {
        const int APPEND_COUNT = 40;

        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, APPEND_COUNT, i =>
        {
            try
            {
                ChannelAppender.Append_AppEntry(_channelFile, $"subject {i}", $"body {i}", DateTime.Now);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        });

        Assert.Empty(failures);
    }
}
