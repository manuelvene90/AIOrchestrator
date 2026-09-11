using System.Text;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Tailing.ChannelTailer;
using Xunit;
using Xunit.Abstractions;

namespace AIOrchestratorCoreLib.Tests.Tailing;

/// <summary>
/// HOW LONG A WRITER MAY STALL MID-ENTRY BEFORE THE TAILER TEARS IT — measured against a REAL writer
/// and a REAL poll cadence, because the subject is a duration and nothing else can pin one.
///
/// <para>
/// WHY THIS FILE EXISTS. On 2026-09-09 the mirror loop gained a
/// <c>ChannelChangeWaker</c>: an append ends the loop's wait at once and is followed by two 200 ms
/// settling pulses. <c>ChannelTailerModel</c>'s protection against releasing a half-written entry was
/// a POLL COUNT — two quiet polls — so the guarantee it bought had always been two times the poll
/// interval, and the waker moved that interval from 2000 ms to 200 ms without touching the constant.
/// The protection shrank 12× in a change that never mentioned it. Swept on the merged code, a writer
/// stalling 300 ms was still safe and one stalling 400 ms was TORN; on the pre-merge cadence the same
/// writer was safe to 4000 ms.
/// </para>
/// <para>
/// AND A TEAR IS NOT A DELAY. The remainder of the entry arrives with no header of its own, so
/// <c>Extract_CompleteEntries</c> reads it as noise and CLEARS it — the tailer's own comment on
/// <c>HeldTrailingEntryFiles</c> calls that "strictly worse than a delay". The half already mirrored
/// is on the owner's phone, the rest is gone, and the channel file on disk is perfectly intact, so
/// nothing afterwards can tell.
/// </para>
/// <para>
/// NOTHING IN THE SUITE COULD SEE IT, which is why this file is written the awkward way. The other
/// tailer tests poll synchronously with no sleeps, so a quiet poll costs nothing and any count is
/// satisfied instantly; <c>AChannelAppendWakesTheBridgeTests</c> writes each entry in ONE call, so no
/// writer is ever mid-entry. Only a stalling writer against wall-clock polling reproduces it.
/// </para>
/// <para>
/// IT SWEEPS RATHER THAN ASSERTING ONE NUMBER, and reports the whole table whether it passes or fails
/// — the defect was a boundary that moved silently, so the evidence that matters is where the boundary
/// IS, not that one point of it is on the right side.
/// </para>
/// </summary>
public class AStalledWriterNeverTearsATrailingEntryTests : IDisposable
{
    /// <summary>
    /// The cadence the merged waker polls at — <c>ChannelChangeWaker_Factory.SETTLING_PULSE_MILLISECONDS</c>.
    /// Hard-coded rather than referenced: this test's job is to hold the tailer's guarantee steady
    /// WHILE that number moves, so borrowing it would make the two move together and prove nothing.
    /// </summary>
    const int POLL_MILLISECONDS = 200;

    /// <summary>
    /// How long a writer may pause between two writes of the SAME entry and still be safe. The pre-merge
    /// guarantee was 2 × the 2000 ms mirror tick, and the longest of these sits a full second inside it
    /// so a loaded machine cannot decide the result. On the merged code only the first survives.
    /// </summary>
    static readonly int[] STALL_MILLISECONDS = [300, 1000, 2000, 3000];

    readonly ITestOutputHelper _output;
    readonly string _tempFolder;

    public AStalledWriterNeverTearsATrailingEntryTests(ITestOutputHelper output)
    {
        _output = output;
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-stall-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void AWriterStallingMidEntry_HasItsWholeEntryMirrored()
    {
        List<string> report = [];
        List<string> torn = [];

        foreach (var stall in STALL_MILLISECONDS)
        {
            var outcome = Run_OneStall(stall);

            report.Add($"  stall {stall,5} ms → {outcome}");

            if (!outcome.StartsWith("whole", StringComparison.Ordinal))
                torn.Add($"{stall} ms");
        }

        var sweep = $"sweep at a {POLL_MILLISECONDS} ms poll cadence:{Environment.NewLine}{string.Join(Environment.NewLine, report)}";

        _output.WriteLine(sweep);

        Assert.True(
            torn.Count == 0,
            $"a writer that stalled mid-entry had it torn at {string.Join(", ", torn)}.{Environment.NewLine}{sweep}");
    }

    /// <summary>
    /// Writes ONE entry in two halves with <paramref name="stallMilliseconds"/> between them, polling
    /// throughout, and says what the tailer eventually emitted. The poller runs on this thread and the
    /// writer on another, exactly as the bridge and a session sit: nothing here coordinates them.
    /// </summary>
    string Run_OneStall(int stallMilliseconds)
    {
        var channelFile = Path.Combine(_tempFolder, $"channel-{stallMilliseconds}.md");
        var channel = DiscoveredChannel_Factory.Create_ForImplementer("orch-x", "imp-1", channelFile);

        File.WriteAllText(channelFile, "# CHANNEL\n\n---\n");

        var tailer = ChannelTailer_Factory.Create_Fresh();

        // First sighting anchors the cursor at the current end, so the entry must be written after it.
        tailer.Poll([channel]);

        var writer = Task.Run(() =>
        {
            File.AppendAllText(channelFile, "\n## [1] FROM implementer — 2026-09-09 12:00 — report\n\nFIRST-HALF\n");
            Thread.Sleep(stallMilliseconds);
            File.AppendAllText(channelFile, "SECOND-HALF\n");
        });

        // The whole quiet period on top of the stall, plus the writer's own head start and a generous
        // margin: what is being measured is WHAT comes out, never how fast.
        var deadline = DateTime.UtcNow.AddMilliseconds(stallMilliseconds + 12_000);

        StringBuilder emitted = new();

        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(POLL_MILLISECONDS);

            var result = tailer.Poll([channel]);

            foreach (var append in result.CompletedAppends)
            {
                foreach (var entry in append.Entries)
                    emitted.Append(entry.Body);

                tailer.Confirm_Append(append.Channel.FilePath);
            }

            // Emitted and the writer is done: whatever the tailer was going to say, it has said.
            if (emitted.Length > 0 && writer.IsCompleted)
                break;
        }

        writer.GetAwaiter().GetResult();

        var body = emitted.ToString();

        if (body.Length == 0)
            return "NOTHING emitted at all";

        if (body.Contains("FIRST-HALF", StringComparison.Ordinal) && body.Contains("SECOND-HALF", StringComparison.Ordinal))
            return "whole entry";

        return $"TORN — emitted '{body.Replace("\n", "⏎", StringComparison.Ordinal)}'";
    }
}
