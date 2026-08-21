using AIOrchestratorCoreLib.Status.SessionContextUsage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// Reading one session's context pressure out of the probe file it writes beside itself.
///
/// THE FIXTURES ARE REAL PAYLOAD SHAPES, taken from a live .usage.json on 2026-08-21 (Claude Code
/// 2.1.238) rather than invented: the reader that these replaced was proved by a fixture with a
/// top-level "usage" object that no Claude Code payload has ever contained, which is how a
/// double-counting bug stayed invisible to a green suite.
/// </summary>
public class SessionContextUsageFactoryTests : IDisposable
{
    readonly string _folder;

    public SessionContextUsageFactoryTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"aiorch-context-tests-{Guid.NewGuid():N}");
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
    public void ItReadsThePercentageClaudeCodeReported()
    {
        var reading = SessionContextUsage_Factory.Create_OrNull(Write_Probe(PAYLOAD_AT_8_PERCENT, withBom: false));

        Assert.NotNull(reading);
        Assert.Equal(8, reading.UsedPercent);
    }

    /// <summary>
    /// EVERY PROBE FILE ON DISK HAS A BOM, and this is the test that says so out loud. The status
    /// line script writes them with PowerShell 5.1's `-Encoding utf8`, which emits one; the JSON
    /// parser this reader sits on top of throws on a leading BOM character. It survives only because
    /// the shared file read strips it — so if that read is ever "simplified" to File.ReadAllText
    /// with an explicit encoding, this test is what fails instead of every context figure in the app
    /// silently going blank.
    /// </summary>
    [Fact]
    public void ABomWrittenByTheProbeDoesNotBlindIt()
    {
        var reading = SessionContextUsage_Factory.Create_OrNull(Write_Probe(PAYLOAD_AT_8_PERCENT, withBom: true));

        Assert.NotNull(reading);
        Assert.Equal(8, reading.UsedPercent);
    }

    [Fact]
    public void AProbeFileThatIsNotThereIsUnknownRatherThanZero()
    {
        Assert.Null(SessionContextUsage_Factory.Create_OrNull(Path.Combine(_folder, "never-written.usage.json")));
    }

    /// <summary>
    /// An older Claude Code whose status line carries no context_window at all. Unknown, not 0% —
    /// a zero would render as a completely empty window on every surface.
    /// </summary>
    [Fact]
    public void APayloadWithoutContextDataIsUnknown()
    {
        Assert.Null(SessionContextUsage_Factory.Create_OrNull(
            Write_Probe("{\"model\":{\"display_name\":\"Opus 5\"},\"cost\":{\"total_cost_usd\":1.5}}", withBom: false)));
    }

    /// <summary>
    /// These files are rewritten by a live session on every render, so a reader will sometimes catch
    /// one mid-write. Half a file is unknown, never a throw that takes a status push down with it.
    /// </summary>
    [Fact]
    public void AHalfWrittenFileIsUnknownRatherThanAThrow()
    {
        Assert.Null(SessionContextUsage_Factory.Create_OrNull(Write_Probe("{\"context_window\":{\"used_per", withBom: false)));
    }

    [Fact]
    public void ItDatesTheReadingByTheProbeFilesOwnWriteTime()
    {
        var path = Write_Probe(PAYLOAD_AT_8_PERCENT, withBom: false);
        var reading = SessionContextUsage_Factory.Create_OrNull(path);

        Assert.NotNull(reading);
        Assert.Equal(File.GetLastWriteTimeUtc(path), reading.ProbeTimeUtc);
    }

    /// <summary>The live shape, trimmed to what this reader looks at, with its neighbours left in.</summary>
    const string PAYLOAD_AT_8_PERCENT =
        "{\"session_id\":\"277a9896\",\"model\":{\"id\":\"claude-opus-5\",\"display_name\":\"Opus 5\"},"
        + "\"version\":\"2.1.238\",\"cost\":{\"total_cost_usd\":3.73},"
        + "\"context_window\":{\"total_input_tokens\":79558,\"total_output_tokens\":230,"
        + "\"context_window_size\":1000000,\"current_usage\":{\"input_tokens\":2,\"output_tokens\":230,"
        + "\"cache_creation_input_tokens\":1140,\"cache_read_input_tokens\":78416},"
        + "\"used_percentage\":8,\"remaining_percentage\":92},\"exceeds_200k_tokens\":false}";

    string Write_Probe(string json, bool withBom)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.usage.json");

        File.WriteAllText(path, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));

        return path;
    }
}
