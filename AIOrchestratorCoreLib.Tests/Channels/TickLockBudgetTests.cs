using System.Diagnostics;
using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// A TICK'S WAITING MUST BE BOUNDED BY ONE ALLOWANCE, NOT BY THE NUMBER OF CONTENDED CHANNELS.
/// <para>
/// <c>DEFAULT_BUDGET</c> was cut under the 2 s mirror tick so one contended append could not outlive
/// one tick. That fixed the single call and left the multiplication: <c>Execute_MirrorTick_Async</c>
/// is one sequential await chain and four of its steps append inside a
/// <c>foreach (session) -&gt; foreach (member)</c> nest, so the tick's worst case was
/// <c>appends × 1500 ms</c> with the member count as the multiplier. Ten members is ~15 s of waiting
/// inside a 2 s loop — and the poll, the mirror, the tailer, compaction and the status push all sit
/// behind that same chain, so contention on several channels stopped being a slow channel and became
/// a stopped bridge.
/// </para>
/// <para>
/// THE TIMING ASSERTIONS ARE RATIOS, NOT STOPWATCHES. This repo has twice refused a test that pins
/// WHEN something happens, and rightly. What is pinned here is that N contended channels cost about
/// ONE allowance rather than N budgets — the two outcomes differ by a factor of four, and each bound
/// is set far from both so that a slow machine cannot turn a pass into a fail. The machine these run
/// on is currently short of memory and forks unreliably; a test that needed a quiet machine to be
/// correct would be worthless here.
/// </para>
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class TickLockBudgetTests : IDisposable
{
    /// <summary>Four contended channels — the shape of a tick nesting members inside sessions.</summary>
    const int CONTENDED_CHANNEL_COUNT = 4;

    static readonly TimeSpan PER_CALL_BUDGET = TimeSpan.FromMilliseconds(400);
    static readonly TimeSpan TICK_ALLOWANCE = TimeSpan.FromMilliseconds(400);

    readonly string _tempFolder;

    public TickLockBudgetTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-tick-allowance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    /// <summary>
    /// THE HEADLINE. Without an allowance these four blocked appends cost 4 × 400 ms; with one they
    /// cost about 400 ms in total, because the first exhausts it and the rest fail fast.
    /// <para>
    /// The control for this is deleting <c>Open_TickAllowance</c> from the call below, which takes
    /// the elapsed time back above the unbounded bound and reddens it.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void TheTicksWaitingIsBoundedByOneAllowance_NotByTheNumberOfContendedChannels()
    {
        var channels = Create_LockedChannels(CONTENDED_CHANNEL_COUNT);

        var stopwatch = Stopwatch.StartNew();

        using (ChannelWrite_Lock.Open_TickAllowance(TICK_ALLOWANCE))
        {
            foreach (var channel in channels)
            {
                var wrote = ChannelWrite_Lock.Try_Run_Serialised(channel, PER_CALL_BUDGET, () => { }, out _);

                // Every one of them is blocked for its whole budget. If any of these were true the
                // elapsed-time assertion below would be measuring the wrong thing entirely.
                Assert.False(wrote);
            }
        }

        stopwatch.Stop();

        // 1600 ms unbounded against ~400 ms bounded. 1000 ms sits far from both.
        Assert.True(
            stopwatch.ElapsedMilliseconds < 1000,
            $"THE DEFECT: {CONTENDED_CHANNEL_COUNT} contended channels spent {stopwatch.ElapsedMilliseconds} ms of one "
            + $"tick's waiting. The allowance is {TICK_ALLOWANCE.TotalMilliseconds} ms and the per-call budget is "
            + $"{PER_CALL_BUDGET.TotalMilliseconds} ms, so this is the per-CALL budget multiplying by the channel "
            + "count — which is the whole failure the allowance exists to stop.");
    }

    /// <summary>
    /// The same four channels WITHOUT an allowance, which is what every session, the inbound loop and
    /// every other caller does. This is the other half of the ratio: it proves the bound above comes
    /// from the allowance rather than from the machine happening to be fast, and it pins that opening
    /// no allowance leaves the per-call budget exactly as it was.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void WithNoAllowanceOpen_ThePerCallBudgetIsUntouchedAndTheyMultiply()
    {
        var channels = Create_LockedChannels(CONTENDED_CHANNEL_COUNT);

        var stopwatch = Stopwatch.StartNew();

        foreach (var channel in channels)
            Assert.False(ChannelWrite_Lock.Try_Run_Serialised(channel, PER_CALL_BUDGET, () => { }, out _));

        stopwatch.Stop();

        // Four calls each waiting out 400 ms. Anything near the bounded figure would mean an
        // allowance had leaked in from somewhere and was silently capping unrelated callers.
        Assert.True(
            stopwatch.ElapsedMilliseconds > 1200,
            $"an allowance leaked into a flow that never opened one: {CONTENDED_CHANNEL_COUNT} blocked appends took "
            + $"only {stopwatch.ElapsedMilliseconds} ms. Sessions and the inbound loop rely on the per-call budget "
            + "being exactly what they asked for.");

        Assert.Null(ChannelWrite_Lock.Get_RemainingTickAllowance());
    }

    /// <summary>
    /// A SPENT ALLOWANCE MUST NOT STOP THE TICK WRITING — it only stops it WAITING. An uncontended
    /// write charges ~0 ms, so the degraded tick still delivers everything that is free.
    /// <para>
    /// This is the case that separates "bounded" from "broken", and it carries no timing assertion at
    /// all: if a spent allowance silenced healthy channels, the fix would be worse than the defect.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void AnExhaustedAllowanceStillWritesAnUncontendedChannel()
    {
        var blocked = Create_LockedChannels(1).Single();
        var free = Create_Channel("free-channel.md");

        using (ChannelWrite_Lock.Open_TickAllowance(TICK_ALLOWANCE))
        {
            Assert.False(ChannelWrite_Lock.Try_Run_Serialised(blocked, PER_CALL_BUDGET, () => { }, out _));

            Assert.Equal(TimeSpan.Zero, ChannelWrite_Lock.Get_RemainingTickAllowance());

            var wrote = ChannelWrite_Lock.Try_Run_Serialised(
                free, PER_CALL_BUDGET, () => File.AppendAllText(free, "written\n"), out _);

            Assert.True(wrote, "an exhausted allowance refused an UNCONTENDED write — the tick would stop writing "
                + "healthy channels, which is a worse failure than the one being fixed.");
        }

        Assert.Contains("written", File.ReadAllText(free));
    }

    /// <summary>
    /// THE ALLOWANCE MUST SURVIVE AN await, WHICH IS THE ENTIRE REASON IT IS AN <c>AsyncLocal</c> AND
    /// NOT A FIELD. The mirror tick awaits roughly a dozen times between its first append and its
    /// last; an allowance that reset at the first await would bound nothing and would look correct in
    /// any test that did not await.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheAllowanceSurvivesTheAwaitsATickMakesBetweenItsAppends()
    {
        var channels = Create_LockedChannels(2);

        using (ChannelWrite_Lock.Open_TickAllowance(TICK_ALLOWANCE))
        {
            Assert.False(ChannelWrite_Lock.Try_Run_Serialised(channels[0], PER_CALL_BUDGET, () => { }, out _));

            var afterFirstAppend = ChannelWrite_Lock.Get_RemainingTickAllowance();

            await Task.Yield();
            await Task.Delay(20);

            Assert.Equal(
                afterFirstAppend,
                ChannelWrite_Lock.Get_RemainingTickAllowance());

            var stopwatch = Stopwatch.StartNew();

            Assert.False(ChannelWrite_Lock.Try_Run_Serialised(channels[1], PER_CALL_BUDGET, () => { }, out _));

            stopwatch.Stop();

            // Spent before the await, so this one must fail fast on the far side of it.
            Assert.True(
                stopwatch.ElapsedMilliseconds < 200,
                $"the allowance did not survive the awaits: the second blocked append waited {stopwatch.ElapsedMilliseconds} ms "
                + "on an allowance that was already exhausted before the await.");
        }
    }

    /// <summary>
    /// Disposing restores what was there before, so a tick cannot strand an allowance that then caps
    /// every later caller in the flow — a leak that would look exactly like the lock being broken.
    /// </summary>
    [Fact]
    public void DisposingTheScopeRestoresTheAbsenceOfAnAllowance()
    {
        Assert.Null(ChannelWrite_Lock.Get_RemainingTickAllowance());

        using (ChannelWrite_Lock.Open_TickAllowance(TICK_ALLOWANCE))
            Assert.NotNull(ChannelWrite_Lock.Get_RemainingTickAllowance());

        Assert.Null(ChannelWrite_Lock.Get_RemainingTickAllowance());
    }

    /// <summary>
    /// Locks each channel the way a foreign writer would — a real lock directory with real metadata,
    /// so the appends block on the same path the bridge blocks on rather than on a stub.
    /// </summary>
    List<string> Create_LockedChannels(int count)
    {
        var channels = new List<string>();

        for (var index = 0; index < count; index++)
        {
            var channelFile = Create_Channel($"channel-{index}.md");
            var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(channelFile);

            Directory.CreateDirectory(lockDirectory);

            File.WriteAllText(
                Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
                ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow, "session"));

            channels.Add(channelFile);
        }

        return channels;
    }

    string Create_Channel(string fileName)
    {
        var channelFile = Path.Combine(_tempFolder, fileName);

        File.WriteAllText(channelFile, "seed\n");

        return channelFile;
    }
}
