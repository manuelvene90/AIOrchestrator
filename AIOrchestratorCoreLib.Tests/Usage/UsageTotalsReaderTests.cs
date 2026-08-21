using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Usage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Usage;

/// <summary>
/// Guards the figures behind the cards, the detail window and the /tokens and /cost commands —
/// all of them read through this one component.
/// </summary>
public class UsageTotalsReaderTests : IDisposable
{
    readonly string _tempFolder;

    public UsageTotalsReaderTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-usage-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void Read_Tokens_SumsEveryTokenFieldShape_WhereverItSitsInThePayload()
    {
        var file = Write_UsageFile("""
            {
              "model": { "display_name": "Opus" },
              "cost": { "total_cost_usd": 1.25 },
              "usage": {
                "input_tokens": 100,
                "output_tokens": 20,
                "cache_creation_input_tokens": 5,
                "cache_read_input_tokens": 7,
                "nested": { "total_output_tokens": 3 },
                "not_a_token_field": 9999
              }
            }
            """);

        Assert.Equal(135, UsageTotals_Reader.Read_Tokens_OrNull(file));
        Assert.Equal(1.25, UsageTotals_Reader.Read_Cost_OrNull(file));
    }

    /// <summary>
    /// THE REAL PAYLOAD, AND THE DEFECT IT HID. Claude Code reports the totals AND itemises the same
    /// tokens under current_usage; the old recursive sum added both and reported exactly 2.00x on
    /// every live probe file. These are the actual numbers from a 2.1.238 session on 2026-08-21:
    /// 342,655 + 3 in the pair, and 2 + 3 + 444 + 342,209 = 342,658 in the breakdown - the same
    /// tokens, twice.
    ///
    /// The test above this one is why it survived so long: its fixture is a top-level "usage" object
    /// that no Claude Code payload has ever contained, so the suite never saw a real shape.
    /// </summary>
    [Fact]
    public void Read_Tokens_CountsTheWindowOnce_NotItsOwnBreakdownAsWell()
    {
        var file = Write_UsageFile("""
            {
              "model": { "display_name": "Opus 5" },
              "cost": { "total_cost_usd": 3.73 },
              "context_window": {
                "total_input_tokens": 342655,
                "total_output_tokens": 3,
                "context_window_size": 1000000,
                "current_usage": {
                  "input_tokens": 2,
                  "output_tokens": 3,
                  "cache_creation_input_tokens": 444,
                  "cache_read_input_tokens": 342209
                },
                "used_percentage": 34
              }
            }
            """);

        Assert.Equal(342658, UsageTotals_Reader.Read_Tokens_OrNull(file));
    }

    /// <summary>
    /// A window carrying ONLY the itemisation is read from the itemisation - it is an alternative
    /// spelling of the same figure, never something to add to a pair that is not there.
    /// </summary>
    [Fact]
    public void Read_Tokens_FallsBackToTheBreakdownWhenThereIsNoTotalsPair()
    {
        var file = Write_UsageFile("""
            {
              "context_window": {
                "current_usage": {
                  "input_tokens": 10,
                  "output_tokens": 20,
                  "cache_creation_input_tokens": 30,
                  "cache_read_input_tokens": 40
                }
              }
            }
            """);

        Assert.Equal(100, UsageTotals_Reader.Read_Tokens_OrNull(file));
    }

    [Fact]
    public void Read_MissingOrGarbageFile_ReturnsNull_NeverThrows()
    {
        var missing = Path.Combine(_tempFolder, "absent.usage.json");
        var garbage = Write_UsageFile("not json at all");

        Assert.Null(UsageTotals_Reader.Read_Tokens_OrNull(missing));
        Assert.Null(UsageTotals_Reader.Read_Cost_OrNull(missing));
        Assert.Null(UsageTotals_Reader.Read_Tokens_OrNull(garbage));
        Assert.Null(UsageTotals_Reader.Read_Cost_OrNull(garbage));
    }

    [Theory]
    [InlineData(999, "999 tok")]
    [InlineData(1_500, "1.5k tok")]
    [InlineData(2_400_000, "2.4M tok")]
    public void Format_Tokens_ScalesTheUnit(long tokens, string expected)
    {
        Assert.Equal(expected, UsageTotals_Reader.Format_Tokens(tokens));
    }

    /// <summary>
    /// /cost's breakdown. A source that never wrote a probe file (here: the communicator) must be
    /// absent rather than reported as a zero — a $0.00 line reads as "this session was free", not
    /// as "this session never ran".
    /// </summary>
    [Fact]
    public void Build_PerSourceTotals_ReportsEveryLiveSource_AndSumsToTheOrchestrationTotal()
    {
        var paths = SupervisionPaths_Factory.Create(_tempFolder);
        var store = OrchestrationSessionStore_Factory.Create(paths);

        store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");
        store.Add_Implementer("arb-fix");

        var session = store.Get_Session("arb-fix");
        var memberId = session.Members[0].MemberId;

        Write_ProbeFile(Path.Combine(paths.Get_OrchestrationFolder("arb-fix"), UsageTotals_Reader.SESSION_USAGE_FILE), 2.00, 100);
        Write_ProbeFile(Path.Combine(paths.Get_ImplementerFolder("arb-fix", memberId), UsageTotals_Reader.SESSION_USAGE_FILE), 3.00, 200);

        var perSource = UsageTotals_Reader.Build_PerSourceTotals(paths, session);

        Assert.Equal(2, perSource.Count);
        Assert.Equal(("supervisor", 2.00, 100L), perSource[0]);
        Assert.Equal((memberId, 3.00, 200L), perSource[1]);

        // The two readings must never disagree: the total IS the breakdown summed.
        Assert.Equal((5.00, 300L), UsageTotals_Reader.Build_OrchestrationTotals(paths, session));
    }

    /// <summary>
    /// A respawned session rewrites its probe file with the NEW session's smaller totals. The
    /// per-source line has to fold the dead session in, exactly as the summed total does, or
    /// /cost would show a member's spend dropping while it keeps working.
    /// </summary>
    [Fact]
    public void Build_PerSourceTotals_FoldsInASourceThatRespawnedAndResetItsFile()
    {
        var paths = SupervisionPaths_Factory.Create(_tempFolder);
        var store = OrchestrationSessionStore_Factory.Create(paths);

        store.Create_Orchestration("arb-fix", "Arb Studio", @"C:\repos\arb");
        var session = store.Get_Session("arb-fix");

        var supervisorFile = Path.Combine(paths.Get_OrchestrationFolder("arb-fix"), UsageTotals_Reader.SESSION_USAGE_FILE);

        Write_ProbeFile(supervisorFile, 4.00, 500);
        Assert.Equal(("supervisor", 4.00, 500L), UsageTotals_Reader.Build_PerSourceTotals(paths, session)[0]);

        // Respawn: fewer tokens than last time is the reset signal.
        Write_ProbeFile(supervisorFile, 1.00, 50);

        Assert.Equal(("supervisor", 5.00, 550L), UsageTotals_Reader.Build_PerSourceTotals(paths, session)[0]);
    }

    static void Write_ProbeFile(string path, double cost, long tokens)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new Exception($"Probe path has no directory: '{path}'"));

        File.WriteAllText(path, $$"""
            {
              "cost": { "total_cost_usd": {{cost}} },
              "usage": { "input_tokens": {{tokens}} }
            }
            """);
    }

    string Write_UsageFile(string content)
    {
        var path = Path.Combine(_tempFolder, $"{Guid.NewGuid():N}.usage.json");
        File.WriteAllText(path, content);
        return path;
    }
}
