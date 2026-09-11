using System.Diagnostics;
using AIOrchestratorCoreLib.Bridge.ChannelChangeWaker;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE WAIT AT THE BOTTOM OF THE MIRROR LOOP, on its own.
///
/// <para>
/// Measured on the VPS on 2026-09-09: 11–12 s median from the owner's Telegram message to their
/// supervisor's turn starting, and the mirror tick is paid TWICE on that path — once by the tick that
/// writes their message into the channel, once by the tick that hands the supervisor's answer back.
/// The owner's decision was to react to the file and keep the 2 s tick as the safety net, which is
/// what this class is: a wait that ends on a channel write OR on the tick, whichever comes first.
/// </para>
/// <para>
/// The tick is the CEILING, never the quantum — every assertion below is written so that a machine
/// where the watcher produces nothing at all still behaves exactly as the loop did before.
/// </para>
/// </summary>
public class ChannelChangeWakerTests : IDisposable
{
    /// <summary>
    /// Far longer than any wake this test measures, so "it returned early" cannot be the tick
    /// arriving on time.
    /// </summary>
    const int A_TICK_NOBODY_REACHES = 10_000;

    readonly string _root;

    public ChannelChangeWakerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aiorch-waker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "orch-1"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }
    }

    /// <summary>
    /// THE CLAIM ITSELF: an append ends the wait, and it does so in a fraction of the tick that would
    /// otherwise have discovered it. In a SUBFOLDER, because that is where every channel file lives —
    /// the root holds config and state, the traffic is one folder down.
    /// </summary>
    [RequiresFileSystemWatcherEventsFact]
    public async Task AnAppendedChannelFile_EndsTheWait()
    {
        using var waker = ChannelChangeWaker_Factory.Create(_root, _ => { });

        var stopwatch = Stopwatch.StartNew();
        var wait = waker.Wait_ForChangeOrTick_Async(A_TICK_NOBODY_REACHES, CancellationToken.None);

        await Task.Delay(200);
        File.AppendAllText(Path.Combine(_root, "orch-1", "owner-channel.md"), "\n## [1] FROM supervisor — x\nbody\n");

        await wait;

        Assert.True(
            stopwatch.ElapsedMilliseconds < 2_000,
            $"the wait ran for {stopwatch.ElapsedMilliseconds} ms — the append did not end it.");
    }

    /// <summary>
    /// THE OTHER HALF, and without it the one above would be satisfied by a wait that had simply
    /// stopped waiting: with nothing writing, the tick is served in full. This is also the whole
    /// behaviour on a machine whose watcher never fires.
    /// </summary>
    [Fact]
    public async Task NothingChanging_ServesTheWholeTick()
    {
        using var waker = ChannelChangeWaker_Factory.Create(_root, _ => { });

        var stopwatch = Stopwatch.StartNew();

        await waker.Wait_ForChangeOrTick_Async(600, CancellationToken.None);

        Assert.True(
            stopwatch.ElapsedMilliseconds >= 500,
            $"the wait ended after {stopwatch.ElapsedMilliseconds} ms with nothing to wake it — a tick is a tick.");
    }

    /// <summary>
    /// A BURST IS ONE ARRIVAL. Six appends in a row are one crew filing one round of reports, and the
    /// loop must not run six ticks for them: the wake is debounced, and what follows it is the fixed
    /// short sequence the tailer needs to release a trailing entry — never one per event.
    ///
    /// <para>
    /// Pinned as a COUNT rather than a duration so it cannot be satisfied by a slow machine: after the
    /// burst's own sequence, the very next wait serves a full tick, which is what says the waker is
    /// not still working through a queue of events.
    /// </para>
    /// </summary>
    [RequiresFileSystemWatcherEventsFact]
    public async Task ABurstOfAppends_CostsTheSameFewWakesAsOne()
    {
        using var waker = ChannelChangeWaker_Factory.Create(_root, _ => { });

        var channelFile = Path.Combine(_root, "orch-1", "owner-channel.md");

        for (var index = 0; index < 6; index++)
            File.AppendAllText(channelFile, $"\n## [{index}] FROM imp-1 — x\nbody\n");

        var shortWaits = 0;

        // Ten rounds is far more than the sequence can be and still be called debounced; the loop below
        // stops at the first full tick, so the count IS the sequence.
        for (var round = 0; round < 10; round++)
        {
            var stopwatch = Stopwatch.StartNew();

            await waker.Wait_ForChangeOrTick_Async(1_500, CancellationToken.None);

            if (stopwatch.ElapsedMilliseconds >= 1_400)
                break;

            shortWaits++;
        }

        Assert.True(
            shortWaits is > 0 and <= ChannelChangeWaker_Factory.MAXIMUM_WAKES_PER_BURST,
            $"six appends produced {shortWaits} short waits — one arrival may cost at most "
                + $"{ChannelChangeWaker_Factory.MAXIMUM_WAKES_PER_BURST}, and at least one, or nothing reacted at all.");
    }

    /// <summary>
    /// A ROOT THAT IS NOT THERE IS NOT A DEAD LOOP. The watcher is best-effort by construction — inotify
    /// runs out of watches, a network filesystem reports nothing, a folder can simply not exist yet — and
    /// the contract in every one of those cases is the loop the bridge had before: serve the tick, say
    /// once why, carry on. It must never throw out of the factory, because that would take the mirror
    /// loop down for an optimisation.
    /// </summary>
    [Fact]
    public async Task ARootThatCannotBeWatched_StillServesTheTickAndSaysSoOnce()
    {
        var lines = new List<string>();

        using var waker = ChannelChangeWaker_Factory.Create(
            Path.Combine(_root, "a-folder-that-was-never-created"),
            lines.Add);

        var stopwatch = Stopwatch.StartNew();

        await waker.Wait_ForChangeOrTick_Async(400, CancellationToken.None);

        Assert.True(stopwatch.ElapsedMilliseconds >= 300, $"the tick was not served: {stopwatch.ElapsedMilliseconds} ms.");
        Assert.Contains(lines, line => line.Contains("watcher", StringComparison.OrdinalIgnoreCase));
        Assert.Single(lines);
    }

    /// <summary>
    /// A WATCH THAT IS LOST IS NOTICED, SAID ONCE, AND RE-ARMED — and this is the finding whose subject
    /// is a platform this machine is not.
    ///
    /// <para>
    /// Probed on macOS on 2026-09-09: deleting and recreating the supervision root cost the merged waker
    /// nothing at all — it was reacting again 306 ms later, and it logged NOTHING, so nobody would have
    /// known either way. On Linux, which is what the VPS runs, inotify drops the watch descriptor when
    /// the watched directory goes and .NET does not re-add it; the merged waker had no re-arm path, so
    /// the optimisation would have died silently for the life of the mirror loop and the bridge would
    /// have fallen back to its 2 s tick with no line to say so. THE LINUX HALF IS INFERRED, NOT MEASURED
    /// HERE — what this test can pin on any platform is that the loss is DETECTED, reported exactly once,
    /// and followed by a watch that works.
    /// </para>
    /// <para>
    /// It fails on the merged code for the first of those three: no line was ever written.
    /// </para>
    /// </summary>
    [RequiresFileSystemWatcherEventsFact]
    [Trait("Speed", "Slow")]
    public async Task ARootDeletedAndRecreated_IsNoticedOnceAndTheWatchComesBack()
    {
        List<string> lines = [];

        using var waker = ChannelChangeWaker_Factory.Create(_root, line => { lock (lines) lines.Add(line); });

        // One wait first, so the watcher is armed and its first validity check is behind us.
        await waker.Wait_ForChangeOrTick_Async(50, CancellationToken.None);

        Directory.Delete(_root, recursive: true);

        // Long enough for a validity check to see the root MISSING, which is one of the two detectors;
        // the other (a creation stamp that has moved) covers the case where both halves happen inside
        // one check period, so the test does not depend on which of them fires.
        await Drive_Waits_Async(waker, TimeSpan.FromMilliseconds(ChannelChangeWaker_Factory.VALIDITY_CHECK_MILLISECONDS + 500));

        Directory.CreateDirectory(Path.Combine(_root, "orch-1"));

        await Drive_Waits_Async(waker, TimeSpan.FromMilliseconds((2 * ChannelChangeWaker_Factory.VALIDITY_CHECK_MILLISECONDS) + 500));

        Assert.Single(lines);
        Assert.Contains("armed again", lines[0], StringComparison.OrdinalIgnoreCase);

        // THE HALF THAT MATTERS ON THE VPS: the replacement watch actually watches. A re-arm that only
        // logged would be a line saying the optimisation is back when it is not.
        var stopwatch = Stopwatch.StartNew();
        var wait = waker.Wait_ForChangeOrTick_Async(A_TICK_NOBODY_REACHES, CancellationToken.None);

        await Task.Delay(200);
        File.AppendAllText(Path.Combine(_root, "orch-1", "owner-channel.md"), "\n## [1] FROM supervisor — x\nbody\n");

        await wait;

        Assert.True(
            stopwatch.ElapsedMilliseconds < 2_000,
            $"after the re-arm the wait ran for {stopwatch.ElapsedMilliseconds} ms — the replacement watch is not watching.");

        // Said ONCE: a folder that stays replaced would otherwise write this line every check.
        await Drive_Waits_Async(waker, TimeSpan.FromMilliseconds((2 * ChannelChangeWaker_Factory.VALIDITY_CHECK_MILLISECONDS) + 500));

        Assert.Single(lines);
    }

    /// <summary>
    /// Runs the waker's wait in short ticks for <paramref name="forHowLong"/>, which is the only way the
    /// validity check is reached: it runs between full waits and nowhere else, so a test that merely
    /// slept would exercise nothing.
    /// </summary>
    static async Task Drive_Waits_Async(IChannelChangeWaker waker, TimeSpan forHowLong)
    {
        var until = DateTime.UtcNow + forHowLong;

        while (DateTime.UtcNow < until)
            await waker.Wait_ForChangeOrTick_Async(100, CancellationToken.None);
    }

    /// <summary>A cancelled tick ends the wait the way the loop above it already knows how to catch.</summary>
    [Fact]
    public async Task ACancelledToken_EndsTheWaitAsACancellation()
    {
        using var waker = ChannelChangeWaker_Factory.Create(_root, _ => { });
        using var cancellation = new CancellationTokenSource(150);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waker.Wait_ForChangeOrTick_Async(A_TICK_NOBODY_REACHES, cancellation.Token));
    }
}
