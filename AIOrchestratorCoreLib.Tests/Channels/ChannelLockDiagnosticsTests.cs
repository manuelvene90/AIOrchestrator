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
/// <para>
/// EACH CASE CAPTURES ON ITS OWN ASYNC FLOW, and that is what stopped this class from being the
/// suite's named intermittent. It used to wire the PROCESS-WIDE sink in its constructor;
/// <c>BridgeEngineModel.Run_Async</c> rewires that sink at every engine start, dozens of Bridge tests
/// start a real engine, and xUnit runs collections in parallel — so the capture was stolen mid-test
/// and the first case below failed with "Assert.Single() Failure: The collection was empty" about a
/// lock that had behaved perfectly. Measured on this branch 2026-09-09: five of six full runs red
/// before the change, zero of six after. The capture is opened in the METHOD and not in the
/// constructor because an ambient value has to be set on the flow the assertions run on.
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
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    ChannelLock_Diagnostics.Capture Capture_Diagnostics()
    {
        return ChannelLock_Diagnostics.Capture_OnThisFlow(line => { lock (_lines) _lines.Add(line); });
    }

    [Fact]
    public void GivingUpOnAHeldLock_ReportsTheChannelTheReasonAndTheWait()
    {
        using var capture = Capture_Diagnostics();

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
        using var capture = Capture_Diagnostics();

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
        using var capture = Capture_Diagnostics();

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
        using var capture = Capture_Diagnostics();

        var acquired = ChannelFile_Lock.Try_Run_WithLock(_channelFile, TimeSpan.FromSeconds(2), () => { }, out _);

        Assert.True(acquired);
        Assert.Empty(_lines);
    }

    /// <summary>
    /// THE PROPERTY THE WHOLE CHANGE EXISTS FOR, pinned directly rather than inferred from the cases
    /// above going green: a capture belongs to ONE async flow, so a report made on a flow that never
    /// opened one cannot land in it. That is what makes a capture unstealable — the theft was another
    /// test's engine rewiring a shared sink, and there is no longer a shared thing to rewire.
    /// <para>
    /// Both halves are asserted. Only checking that the foreign line is absent would pass just as well
    /// if captures had stopped working altogether, so the case also pins that this flow's own report
    /// DOES arrive, and pins the exact contents rather than a count.
    /// </para>
    /// <para>
    /// The other direction — an unscoped report reaching the process-wide sink — is deliberately NOT
    /// pinned here. Asserting it means calling <c>Set_Sink</c>, and a test that does that is exactly
    /// the collision this branch removed: any parallel engine start would take the sink back and the
    /// case would fail intermittently for a reason that is not its subject.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ACaptureOnOneFlow_IsInvisibleToEveryOtherFlow()
    {
        var captured = new List<string>();
        var captureIsOpen = new TaskCompletionSource();
        var foreignReportIsDone = new TaskCompletionSource();

        var capturingFlow = Task.Run(async () =>
        {
            using var capture = ChannelLock_Diagnostics.Capture_OnThisFlow(
                line => { lock (captured) captured.Add(line); });

            captureIsOpen.SetResult();

            await foreignReportIsDone.Task;

            ChannelLock_Diagnostics.Report("this flow's own report");
        });

        var foreignFlow = Task.Run(async () =>
        {
            await captureIsOpen.Task;

            // Started from the test's flow, which holds no capture — the shape of every Bridge test
            // that starts an engine while this class is asserting.
            ChannelLock_Diagnostics.Report("a report from a flow that never opened a capture");

            foreignReportIsDone.SetResult();
        });

        await Task.WhenAll(capturingFlow, foreignFlow);

        Assert.Equal(["this flow's own report"], captured);
    }
}
