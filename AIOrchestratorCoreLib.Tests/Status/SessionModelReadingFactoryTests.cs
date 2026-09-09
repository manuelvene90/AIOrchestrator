using AIOrchestratorCoreLib.Status.SessionModelReading;
using AIOrchestratorCoreLib.Usage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// Reading WHAT MODEL AND EFFORT one session is actually running out of the probe file it writes
/// beside itself (.usage.json). Requested for the pulse, 2026-09-09: a crew's cost and pace are set
/// by the dial each session sits at, and until now the only place that showed was the session's own
/// terminal.
///
/// THE FIXTURES ARE THE LIVE PAYLOAD SHAPE — Claude Code 2.1.266, ai-orchestrator-21/solo-1 —
/// trimmed to the blocks this reader looks at with their neighbours left in. Same rule as
/// SessionContextUsageFactoryTests, for the same reason: a reader proved by an invented shape is
/// proved of nothing.
/// </summary>
public class SessionModelReadingFactoryTests : IDisposable
{
    readonly string _folder;

    public SessionModelReadingFactoryTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"aiorch-model-reading-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ItReadsTheModelAndTheEffortClaudeCodeReported()
    {
        var reading = SessionModelReading_Factory.Create_OrNull(Write_Probe(PAYLOAD_FABLE_XHIGH, withBom: false));

        Assert.NotNull(reading);
        Assert.Equal("Fable 5.1", reading.ModelDisplayName);
        Assert.Equal("xhigh", reading.EffortLevel);
    }

    /// <summary>
    /// EVERY PROBE FILE ON DISK HAS A BOM — the status line writes them with PowerShell 5.1's
    /// `-Encoding utf8`. The raw-string readers underneath refuse a BOM outright (pinned in
    /// RateLimitsReaderTests); this reading survives only because the shared file read strips it
    /// first. If that read is ever "simplified" to File.ReadAllText, this is what reddens instead of
    /// every model field on the pulse silently vanishing.
    /// </summary>
    [Fact]
    public void ABomWrittenByTheProbeDoesNotBlindIt()
    {
        var reading = SessionModelReading_Factory.Create_OrNull(Write_Probe(PAYLOAD_FABLE_XHIGH, withBom: true));

        Assert.NotNull(reading);
        Assert.Equal("Fable 5.1", reading.ModelDisplayName);
        Assert.Equal("xhigh", reading.EffortLevel);
    }

    /// <summary>
    /// An older Claude Code, or a model with no dial, writes no `effort` block at all. The model is
    /// still a reading — half of one is worth more than none — and the effort is UNKNOWN, never a
    /// guessed default.
    /// </summary>
    [Fact]
    public void AnOlderPayloadWithoutTheEffortDialStillNamesTheModel()
    {
        var reading = SessionModelReading_Factory.Create_OrNull(Write_Probe(PAYLOAD_OPUS_NO_EFFORT, withBom: false));

        Assert.NotNull(reading);
        Assert.Equal("Opus 5", reading.ModelDisplayName);
        Assert.Null(reading.EffortLevel);
    }

    [Fact]
    public void AProbeFileThatIsNotThereIsUnknown()
    {
        Assert.Null(SessionModelReading_Factory.Create_OrNull(Path.Combine(_folder, "never-written.usage.json")));
    }

    /// <summary>
    /// An effort with no model behind it is not a reading. The model name is the reading's subject;
    /// "xhigh" of nothing in particular would put a dial on the pulse with no session to attach it to.
    /// </summary>
    [Fact]
    public void APayloadWithoutAModelIsUnknownEvenWhenItCarriesAnEffort()
    {
        Assert.Null(SessionModelReading_Factory.Create_OrNull(
            Write_Probe("{\"cost\":{\"total_cost_usd\":1.5},\"effort\":{\"level\":\"high\"}}", withBom: false)));
    }

    /// <summary>
    /// These files are rewritten by a live session on every render, so a reader will sometimes catch
    /// one mid-write. Half a file is unknown, never a throw that takes a status push down with it.
    /// </summary>
    [Fact]
    public void AHalfWrittenFileIsUnknownRatherThanAThrow()
    {
        Assert.Null(SessionModelReading_Factory.Create_OrNull(Write_Probe("{\"model\":{\"display_na", withBom: false)));
    }

    /// <summary>
    /// A blank name in the FILE is unknown, not a throw: the file path is the tolerant one, and the
    /// rule that a reading must name its model is applied here as "no reading" rather than as the
    /// exception the in-hand overload raises.
    /// </summary>
    [Fact]
    public void ABlankModelNameInTheFileIsUnknownRatherThanAThrow()
    {
        Assert.Null(SessionModelReading_Factory.Create_OrNull(
            Write_Probe("{\"model\":{\"id\":\"claude-fable-5-1\",\"display_name\":\"   \"},\"effort\":{\"level\":\"xhigh\"}}", withBom: false)));
    }

    /// <summary>
    /// The in-hand overload REFUSES a blank model by name. A reading without a model has no
    /// subject, and letting one through would make every consumer check for an empty string on top
    /// of the null it already checks for — two spellings of "unknown", which is the thing the
    /// non-nullable member exists to prevent.
    /// </summary>
    [Fact]
    public void ABlankModelNameInHandIsRefusedByName()
    {
        var empty = Assert.Throws<ArgumentException>(() => SessionModelReading_Factory.Create("", "xhigh"));
        var blank = Assert.Throws<ArgumentException>(() => SessionModelReading_Factory.Create("   ", null));

        Assert.Contains("modelDisplayName", empty.Message);
        Assert.Contains("modelDisplayName", blank.Message);
    }

    /// <summary>A blank effort is the same as an absent one: unknown, stored as null, never as "".</summary>
    [Fact]
    public void ABlankEffortIsStoredAsUnknown()
    {
        Assert.Null(SessionModelReading_Factory.Create("Fable 5.1", "").EffortLevel);
        Assert.Null(SessionModelReading_Factory.Create("Fable 5.1", "   ").EffortLevel);
        Assert.Null(SessionModelReading_Factory.Create("Fable 5.1", null).EffortLevel);
    }

    [Fact]
    public void FiguresInHandAreKeptAsGiven()
    {
        var reading = SessionModelReading_Factory.Create("Fable 5.1", "xhigh");

        Assert.Equal("Fable 5.1", reading.ModelDisplayName);
        Assert.Equal("xhigh", reading.EffortLevel);
    }

    /// <summary>
    /// The front door the engine actually knocks on is the same reading. UsageTotals_Reader is the
    /// ONE reader of probe figures (item 10), and it DELEGATES here rather than parsing again — the
    /// same shape as Read_ContextUsage_OrNull, pinned so the delegation cannot quietly become a copy.
    /// </summary>
    [Fact]
    public void TheUsageReadersFrontDoorIsTheSameReading()
    {
        var reading = UsageTotals_Reader.Read_ModelReading_OrNull(Write_Probe(PAYLOAD_FABLE_XHIGH, withBom: true));

        Assert.NotNull(reading);
        Assert.Equal("Fable 5.1", reading.ModelDisplayName);
        Assert.Equal("xhigh", reading.EffortLevel);

        Assert.Null(UsageTotals_Reader.Read_ModelReading_OrNull(Path.Combine(_folder, "never-written.usage.json")));
    }

    /// <summary>The live shape, trimmed to what this reader looks at, with its neighbours left in.</summary>
    const string PAYLOAD_FABLE_XHIGH =
        "{\"session_id\":\"7f34ac2b\",\"effort\":{\"level\":\"xhigh\"},"
        + "\"model\":{\"id\":\"claude-fable-5-1\",\"display_name\":\"Fable 5.1\"},\"version\":\"2.1.266\","
        + "\"cost\":{\"total_cost_usd\":18.23},\"context_window\":{\"used_percentage\":20,\"remaining_percentage\":80}}";

    /// <summary>The 2.1.238 shape SessionContextUsageFactoryTests uses — no effort block existed yet.</summary>
    const string PAYLOAD_OPUS_NO_EFFORT =
        "{\"session_id\":\"277a9896\",\"model\":{\"id\":\"claude-opus-5\",\"display_name\":\"Opus 5\"},"
        + "\"version\":\"2.1.238\",\"cost\":{\"total_cost_usd\":3.73},"
        + "\"context_window\":{\"used_percentage\":8,\"remaining_percentage\":92}}";

    string Write_Probe(string json, bool withBom)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.usage.json");

        File.WriteAllText(path, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));

        return path;
    }
}
